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
        public float flightTime; // s until first landing
        public string restingSurface;
    }

    /// <summary>
    /// The golf ball. Hit() launches it from where it lies toward the pin; it then flies, bounces and
    /// rolls on the hole's terrain using BallPhysics at a fixed 2 ms step (frame-rate independent).
    /// </summary>
    public class GolfBall : MonoBehaviour
    {
        const float Step = 0.002f;           // same step the model was calibrated with
        const int MaxStepsPerFrame = 2000;   // never fall more than 4 s behind after a hitch
        const float RollSpeed = 0.35f;       // m/s off the ground below which bouncing turns into rolling
        const float CupRadius = 0.054f;
        const float CupCaptureSpeed = 1.6f;  // m/s; faster balls lip out

        public BallPhysicsSettings settings;
        [Tooltip("Wind speed in m/s.")] public float windSpeed;
        [Tooltip("Direction the wind blows toward, degrees clockwise from north.")] public float windHeading;

        public BallStatus Status { get; private set; }
        public ShotResult Result { get; private set; } = new ShotResult();
        public ShotData LastShot { get; private set; }
        public Vector3 LaunchPoint => origin;
        public bool InMotion => Status == BallStatus.Flying || Status == BallStatus.Rolling;

        public event Action<GolfBall> ShotStarted;
        public event Action<GolfBall> Landed;
        public event Action<GolfBall> ShotFinished;

        BallPhysicsSettings Settings => settings ? settings : BallPhysicsSettings.Defaults;
        Vector3 Wind => Quaternion.Euler(0f, windHeading, 0f) * Vector3.forward * windSpeed;

        HoleInfo hole;
        TerrainSurfaceMap map;
        BallState state;
        Vector3 origin, aim;
        float simTime, accumulator;
        bool landed;

        void Start() => ResetToTee();

        public void ResetToTee()
        {
            if (!Bind()) return;
            var tee = hole.TeeWorld;
            tee.y = map.HeightAt(tee) + BallPhysicsSettings.Radius;
            PlaceAt(tee);
        }

        public void Hit(ShotData shot)
        {
            if (InMotion || !Bind()) return;
            if (Status is BallStatus.Holed or BallStatus.InWater or BallStatus.OutOfBounds) ResetToTee();

            origin = transform.position;
            aim = Vector3.ProjectOnPlane(hole.PinWorld - origin, Vector3.up).normalized;
            state = BallPhysics.Launch(origin, aim, shot);
            LastShot = shot;
            Result = new ShotResult();
            landed = false;
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
            if (Status == BallStatus.Flying)
            {
                BallPhysics.Fly(ref state, dt, Settings, Wind);
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
            var surface = Settings.For(map.SurfaceAt(state.position));
            if (surface.hazard) { Finish(BallStatus.InWater); return; }
            bool moving = BallPhysics.Roll(ref state, dt, map.NormalAt(state.position), surface);
            if (!map.Contains(state.position)) { Finish(BallStatus.OutOfBounds); return; }
            state.position.y = map.HeightAt(state.position) + BallPhysicsSettings.Radius;
            if (!moving) Finish(BallStatus.Stopped);
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

            var surface = Settings.For(map.SurfaceAt(state.position));
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
            if (!landed) Result.carry = Result.total;
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
            Status = BallStatus.Ready;
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
            return true;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
