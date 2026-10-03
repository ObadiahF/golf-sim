using System;
using GolfSim.Course;
using UnityEngine;

namespace GolfSim.Ball
{
    public enum BallStatus { Ready, Flying, Rolling, Stopped, Holed, InWater, OutOfBounds }

    [Serializable]
    public class ShotResult
    {
        public float carry;      // m, horizontal from launch to first landing
        public float total;      // m, horizontal from launch to rest
        public float apex;       // m above the launch point
        public float offline;    // m at rest, + right of the target line
        public float landAngle;  // deg below horizontal at first landing
        public float flightTime; // s until first landing, or until it left the map in the air
        public string restingSurface;
        public bool landed;      // false if it left the map in the air (no land angle)
        public string lie;       // surface it was hit from
        public float lieSpeedKept = 1f;
        public bool hitTree;     // trunk, shrub or canopy
        public bool hitRock;

        /// <summary>"Rough −12%": the lie it was hit from and the ball speed it cost.</summary>
        public string LieLabel => LieEffect.Describe(lie, lieSpeedKept);
    }

    /// <summary>
    /// The golf ball. Hit() launches it from where it lies toward the pin (turned by aimOffset), the lie adjusting the
    /// launch (BallPhysicsSettings.lies); it then flies, bounces and rolls on the hole's terrain using BallPhysics at a
    /// fixed 2 ms step (frame-rate independent), hitting the hole's trees and rocks (ObstacleField). The chance parts
    /// (canopies, scatter) come from a per-shot seed, so a shot replays identically from the same spot and seed.
    /// </summary>
    public class GolfBall : MonoBehaviour
    {
        public const float Step = 0.002f;           // same step the model was calibrated with
        const int MaxStepsPerFrame = 2000;   // never fall more than 4 s behind after a hitch
        public const float RollSpeed = 0.35f;       // m/s off the ground below which bouncing turns into rolling
        public const float CupRadius = 0.054f;
        public const float CupCaptureSpeed = 1.6f;  // m/s; faster balls lip out
        const float RestAgainstSpeed = 0.15f; // m/s; a rolling ball this slow after hitting a trunk or rock stops against it
        const float MaxShotTime = 60f;       // s; a safety net, no real shot gets near it
        const float TeeRadius = 1f;          // m; a ball this close to the tee marker is teed up (clean lie)

        public BallPhysicsSettings settings;
        [Tooltip("Wind speed in m/s.")] public float windSpeed;
        [Tooltip("Direction the wind blows toward, degrees clockwise from north.")] public float windHeading;
        [Tooltip("Where the shot is aimed: degrees right (+) or left (-) of the line to the pin.")] public float aimOffset;
        [Tooltip("Trees, shrubs and rocks stop and deflect the ball.")] public bool collideWithObstacles = true;

        public BallStatus Status { get; private set; }
        public ShotResult Result { get; private set; } = new ShotResult();
        /// <summary>The shot as it came in (before the lie adjusted it); hit it again with Seed to replay.</summary>
        public ShotData LastShot { get; private set; }
        /// <summary>The random seed of the last shot (canopy hits, rebound scatter).</summary>
        public uint Seed { get; private set; }
        public Vector3 LaunchPoint => origin;
        public bool InMotion => Status == BallStatus.Flying || Status == BallStatus.Rolling;

        /// <summary>Flat unit vector the next shot starts along: the line to the pin turned by aimOffset.</summary>
        public Vector3 AimDirection
        {
            get
            {
                if (!Bind()) return Vector3.forward;
                var toPin = Vector3.ProjectOnPlane(hole.PinWorld - transform.position, Vector3.up).normalized;
                return Quaternion.AngleAxis(aimOffset, Vector3.up) * toPin;
            }
        }

        public event Action<GolfBall> ShotStarted;
        public event Action<GolfBall> Landed;
        public event Action<GolfBall> ShotFinished;
        /// <summary>The ball hit a tree (trunk, shrub or canopy) or a rock; for sounds and commentary.</summary>
        public event Action<GolfBall, ObstacleHit> HitObstacle;
        /// <summary>The ball was put down at rest (reset to the tee, a drop, the next turn).</summary>
        public event Action<GolfBall> Placed;

        /// <summary>
        /// The settings the next shot uses: the asset (or the defaults) with the server's live physics profile on top
        /// (BallPhysicsProfile). The asset itself is never changed.
        /// </summary>
        public BallPhysicsSettings Settings => BallPhysicsProfile.Resolve(settings ? settings : BallPhysicsSettings.Defaults);
        /// <summary>The settings the current (or last) shot was hit with: a profile change mid-shot waits for the next one.</summary>
        public BallPhysicsSettings ShotSettings => shotSettings ? shotSettings : Settings;

        /// <summary>The surface the ball sits on: "tee" when teed up, else the terrain's surface (or "out of bounds").</summary>
        public string Lie
        {
            get
            {
                if (onTee) return "tee";
                if (!Bind()) return "";
                return map.Contains(transform.position) ? map.SurfaceAt(transform.position) : "out of bounds";
            }
        }

        /// <summary>How the current lie would change this shot (e.g. "Rough −12%").</summary>
        public LieEffect LieEffectFor(ShotData shot) => Settings.LieFor(Lie, shot.ballSpeed);

        /// <summary>The hole's trees and rocks as the ball sees them (null before the hole is bound).</summary>
        public ObstacleField Obstacles => Bind() ? obstacles : null;
        Vector3 Wind => Quaternion.Euler(0f, windHeading, 0f) * Vector3.forward * windSpeed;

        HoleInfo hole;
        TerrainSurfaceMap map;
        ObstacleField obstacles;
        BallState state;
        ShotRandom rng;
        BallPhysicsSettings shotSettings;
        Vector3 origin, aim;
        float simTime, accumulator;
        bool landed, onTee;

        void Start() => ResetToTee();

        public void ResetToTee()
        {
            if (!Bind()) return;
            var tee = hole.TeeWorld;
            tee.y = map.HeightAt(tee) + BallPhysicsSettings.Radius;
            PlaceAt(tee);
        }

        /// <summary>
        /// Hits the ball from where it lies (every input ends here, so the lie is applied once for all of them). A hit
        /// while the ball is moving is ignored. seed: the random seed for this shot (default: from the shot and spot).
        /// </summary>
        public void Hit(ShotData shot, uint? seed = null)
        {
            if (InMotion || !Bind()) return;
            if (Status is BallStatus.Holed or BallStatus.InWater or BallStatus.OutOfBounds) ResetToTee();

            origin = transform.position;
            aim = AimDirection;
            shotSettings = Settings;
            var lie = shotSettings.LieFor(Lie, shot.ballSpeed);
            state = BallPhysics.Launch(origin, aim, lie.Apply(shot));
            LastShot = shot;
            Seed = seed ?? ShotRandom.SeedFor(shot, origin);
            rng = new ShotRandom(Seed);
            Result = new ShotResult { lie = lie.surface, lieSpeedKept = lie.speed };
            landed = onTee = false;
            simTime = accumulator = 0f;
            Status = BallStatus.Flying;
            ShotStarted?.Invoke(this);
        }

        void Update() => Advance(Time.deltaTime);

        /// <summary>Runs the simulation forward by this much time (fixed 2 ms physics steps inside).</summary>
        public void Advance(float seconds)
        {
            if (!InMotion) return;
            accumulator += seconds;
            for (int i = 0; i < MaxStepsPerFrame && accumulator >= Step && InMotion; i++)
            {
                Simulate(Step);
                accumulator -= Step;
                // Spin the visible ball with its angular velocity.
                float rate = state.spin.magnitude;
                if (rate > 0f) transform.rotation = Quaternion.AngleAxis(rate * Step * Mathf.Rad2Deg, state.spin / rate) * transform.rotation;
            }
            transform.position = state.position;
        }

        void Simulate(float dt)
        {
            simTime += dt;
            if (simTime > MaxShotTime) { GiveUp(); return; }
            var from = state.position;
            if (Status == BallStatus.Flying)
            {
                BallPhysics.Fly(ref state, dt, shotSettings, Wind);
                HitObstacles(from, rolling: false);
                Result.apex = Mathf.Max(Result.apex, state.position.y - origin.y);
                if (!map.Contains(state.position)) { Finish(BallStatus.OutOfBounds); return; }
                float ground = map.HeightAt(state.position) + BallPhysicsSettings.Radius;
                if (state.position.y <= ground)
                {
                    state.position.y = ground;
                    TouchDown();
                }
                return;
            }

            if (InCup()) return;
            var surface = shotSettings.For(map.SurfaceAt(state.position));
            if (surface.hazard) { Finish(BallStatus.InWater); return; }
            bool moving = BallPhysics.Roll(ref state, dt, map.NormalAt(state.position), surface, BallPhysics.LipPull(state.position, hole.PinWorld));
            if (moving && HitObstacles(from, rolling: true))
            {
                // Rolled into a trunk or rock: off a rock's slope it can pop back up into the air.
                var normal = map.NormalAt(state.position);
                if (Vector3.Dot(state.velocity, normal) > RollSpeed) { Status = BallStatus.Flying; return; }
                state.velocity = Vector3.ProjectOnPlane(state.velocity, normal);
                if (state.velocity.magnitude < RestAgainstSpeed) moving = false;
            }
            if (!map.Contains(state.position)) { Finish(BallStatus.OutOfBounds); return; }
            state.position.y = map.HeightAt(state.position) + BallPhysicsSettings.Radius;
            if (!moving && !InCup()) Finish(BallStatus.Stopped);
        }

        /// <summary>Tests this step against the trees and rocks; on a hit records it and raises HitObstacle.</summary>
        bool HitObstacles(Vector3 from, bool rolling)
        {
            if (!collideWithObstacles || obstacles == null) return false;
            if (obstacles.Collide(ref state, from, ref rng, rolling) is not { } hit) return false;
            if (hit.IsRock) Result.hitRock = true;
            else Result.hitTree = true;
            HitObstacle?.Invoke(this, hit);
            return true;
        }

        /// <summary>Safety net for a shot that never settles: put it on the ground where it is and call it stopped.</summary>
        void GiveUp()
        {
            Debug.LogWarning($"[GolfBall] Shot still moving after {MaxShotTime} s at {state.position}; stopping it.");
            if (!map.Contains(state.position)) { Finish(BallStatus.OutOfBounds); return; }
            state.position.y = map.HeightAt(state.position) + BallPhysicsSettings.Radius;
            Finish(BallStatus.Stopped);
        }

        void TouchDown()
        {
            if (!landed)
            {
                landed = true;
                var flat = Flat(state.velocity);
                Result.carry = Flat(state.position - origin).magnitude;
                Result.landAngle = Mathf.Atan2(-state.velocity.y, flat.magnitude) * Mathf.Rad2Deg;
                Result.flightTime = simTime;
                Landed?.Invoke(this);
            }

            var surface = shotSettings.For(map.SurfaceAt(state.position));
            if (surface.hazard) { Finish(BallStatus.InWater); return; }
            if (InCup()) return;

            var normal = map.NormalAt(state.position);
            BallPhysics.Bounce(ref state, normal, surface);
            if (Vector3.Dot(state.velocity, normal) < RollSpeed)
            {
                state.velocity = Vector3.ProjectOnPlane(state.velocity, normal);
                Status = BallStatus.Rolling;
            }
        }

        bool InCup()
        {
            var offset = Flat(state.position - hole.PinWorld);
            if (offset.magnitude > CupRadius || Flat(state.velocity).magnitude > CupCaptureSpeed) return false;
            state.position = hole.PinWorld + Vector3.down * 0.1f; // drop to the bottom of the cup
            Finish(BallStatus.Holed);
            return true;
        }

        void Finish(BallStatus status)
        {
            Status = status;
            state.velocity = Vector3.zero;
            transform.position = state.position;
            var travel = Flat(state.position - origin);
            Result.total = travel.magnitude;
            Result.offline = Vector3.Dot(travel, Vector3.Cross(Vector3.up, aim));
            Result.landed = landed;
            if (!landed)
            {
                // Left the map in the air: it carried at least this far, for this long; there is no land angle.
                Result.carry = Result.total;
                Result.flightTime = simTime;
            }
            Result.restingSurface = map.Contains(state.position) ? map.SurfaceAt(state.position) : "out of bounds";
            ShotFinished?.Invoke(this);
        }

        /// <summary>Puts the ball at rest on the ground at this spot (drops, practice, tests).</summary>
        public void PlaceOnGround(Vector3 position)
        {
            if (!Bind()) return;
            position.y = map.HeightAt(position) + BallPhysicsSettings.Radius;
            PlaceAt(position);
        }

        void PlaceAt(Vector3 position)
        {
            state = new BallState { position = position };
            transform.position = position;
            origin = position;
            onTee = Flat(position - hole.TeeWorld).magnitude < TeeRadius; // teed up anywhere on the tee: a clean lie
            Status = BallStatus.Ready;
            Placed?.Invoke(this);
        }

        /// <summary>Finds the hole and its terrain; rebuilds the surface map when the hole is regenerated.</summary>
        bool Bind()
        {
            if (!hole) hole = FindAnyObjectByType<HoleInfo>();
            var terrain = hole ? hole.GetComponentInChildren<Terrain>() : null;
            if (!terrain)
            {
                Debug.LogWarning("[GolfBall] No hole with a terrain in the scene. Generate one with Golf > Course Builder.");
                return false;
            }
            if (map == null || map.Terrain != terrain) map = new TerrainSurfaceMap(terrain, hole);
            if (obstacles == null || obstacles.hole != hole) obstacles = new ObstacleField(hole, Settings.obstacles);
            return true;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
