using System.Collections.Generic;
using GolfSim.Ball;
using UnityEngine;

namespace GolfSim.Game
{
    public enum ShotEventKind { Strike, Contact, Obstacle, Holed, Water, OutOfBounds, Rest }

    /// <summary>Something that happened during a recorded shot (for replay cameras, slow motion and sounds).</summary>
    public struct ShotEvent
    {
        public ShotEventKind kind;
        public float time;
        public Vector3 position;
        /// <summary>Impact speed (contacts, obstacles) or ball speed (strike), m/s.</summary>
        public float speed;
        /// <summary>Contacts: the surface hit. Obstacles: "leaves", "trunk" or "rock".</summary>
        public string surface;
        /// <summary>Contacts: the first landing.</summary>
        public bool first;
    }

    /// <summary>Who hit a shot and how: captured when the shot starts, enough to simulate it again exactly.</summary>
    public class ShotSetup
    {
        public ShotData shot;
        public uint seed;
        public Vector3 launch;
        public float aimOffset, windSpeed, windHeading;
        public bool collide;
        public BallPhysicsSettings settings;
        public string player, club, lie;

        public static ShotSetup From(GolfBall ball, string player, string club) => new ShotSetup
        {
            shot = ball.LastShot, seed = ball.Seed, launch = ball.LaunchPoint, aimOffset = ball.aimOffset,
            windSpeed = ball.windSpeed, windHeading = ball.windHeading, collide = ball.collideWithObstacles,
            settings = ball.settings, player = player, club = club, lie = ball.Result.lie,
        };
    }

    /// <summary>
    /// The path of one shot, sampled every few ms of simulation time, with its events (landing, bounces, trees,
    /// the cup...). It is made by simulating the shot again from the same spot with the same seed, so it matches the
    /// shot that was played exactly, however the game's frames ran. Playback (the replay) only reads it.
    /// </summary>
    public class ShotRecording
    {
        public const float SampleTime = 2f * GolfBall.Step;
        const float MaxTime = 60f;

        public readonly ShotSetup setup;
        public readonly List<Vector3> samples = new List<Vector3>();
        public readonly List<ShotEvent> events = new List<ShotEvent>();
        public BallStatus end;
        public ShotResult result;
        public Vector3 pin;
        /// <summary>Closest the ball came to the hole while on the ground (lip-outs and near misses), m.</summary>
        public float closestToPin = float.MaxValue;

        public float Duration => (samples.Count - 1) * SampleTime;
        public Vector3 Launch => samples[0];
        public Vector3 Rest => samples[samples.Count - 1];
        /// <summary>First landing time, or -1 (a putt, or it left the hole in the air).</summary>
        public float LandTime => Find(ShotEventKind.Contact) is { } e ? e.time : -1f;
        public Vector3 LandPoint => Find(ShotEventKind.Contact) is { } e ? e.position : Rest;
        /// <summary>First tree or rock hit time, or -1.</summary>
        public float ObstacleTime => Find(ShotEventKind.Obstacle) is { } e ? e.time : -1f;
        public bool Holed => end == BallStatus.Holed;
        public bool Water => end == BallStatus.InWater;
        /// <summary>In the water: when (-1 otherwise) and where the ball went through the water's surface.</summary>
        public float SplashTime { get; private set; } = -1f;
        public Vector3 SplashPoint { get; private set; }
        public float Apex { get; private set; }
        public float Carry => result.carry;
        public float Total => result.total;
        public float StartToPin => Flat(Launch - pin).magnitude;
        public float RestToPin => Flat(Rest - pin).magnitude;
        /// <summary>A rolled shot: never more than a few cm off the ground (putts, bumps).</summary>
        public bool Rolled => Apex < 0.3f;
        /// <summary>Flat unit direction of the shot: launch to landing (or to rest).</summary>
        public Vector3 Direction
        {
            get
            {
                var d = Flat(LandPoint - Launch);
                if (d.sqrMagnitude < 1f) d = Flat(Rest - Launch);
                if (d.sqrMagnitude < 0.01f) d = Flat(pin - Launch);
                return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
            }
        }

        ShotRecording(ShotSetup setup) => this.setup = setup;

        public ShotEvent? Find(ShotEventKind kind)
        {
            foreach (var e in events)
                if (e.kind == kind) return e;
            return null;
        }

        /// <summary>The ball's position at this time (held at the launch before 0 and at rest after the end).</summary>
        public Vector3 PositionAt(float time)
        {
            float f = Mathf.Clamp(time / SampleTime, 0f, samples.Count - 1);
            int i = Mathf.Min((int)f, samples.Count - 2);
            return i < 0 ? samples[0] : Vector3.LerpUnclamped(samples[i], samples[i + 1], f - i);
        }

        /// <summary>Velocity at this time from the samples (m/s), zero at rest.</summary>
        public Vector3 VelocityAt(float time)
        {
            if (time <= 0f || time >= Duration) return Vector3.zero;
            const float h = SampleTime * 2f;
            return (PositionAt(time + h) - PositionAt(time - h)) / (2f * h);
        }

        /// <summary>
        /// Simulates the shot again on a hidden ball, step by step, recording where it went and what it hit.
        /// live: the ball that played it (its result is kept; the recording is checked against where it stopped).
        /// </summary>
        public static ShotRecording Simulate(ShotSetup setup, GolfBall live, Vector3 pin)
        {
            var rec = new ShotRecording(setup) { pin = pin, result = live.Result, end = live.Status };
            var go = new GameObject("Replay Simulation") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var ghost = go.AddComponent<GolfBall>();
                ghost.settings = setup.settings;
                ghost.collideWithObstacles = setup.collide;
                ghost.windSpeed = setup.windSpeed;
                ghost.windHeading = setup.windHeading;
                ghost.aimOffset = setup.aimOffset;
                ghost.PlaceOnGround(setup.launch);
                ghost.HitObstacle += (b, hit) => rec.events.Add(new ShotEvent
                {
                    kind = ShotEventKind.Obstacle, time = rec.Duration, position = hit.point, speed = hit.impactSpeed,
                    surface = hit.canopy ? "leaves" : hit.IsRock ? "rock" : "trunk",
                });
                ghost.Hit(setup.shot, setup.seed);
                rec.Record(ghost);
                if (Vector3.Distance(ghost.transform.position, live.transform.position) > 0.05f || ghost.Status != live.Status)
                    Debug.LogWarning($"[ShotRecording] The replay ended at {ghost.transform.position} ({ghost.Status}), the shot at " +
                                     $"{live.transform.position} ({live.Status}); showing the replay anyway.");
                rec.end = ghost.Status;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
            return rec;
        }

        void Record(GolfBall ghost)
        {
            var strike = ghost.transform.position;
            samples.Add(strike);
            events.Add(new ShotEvent { kind = ShotEventKind.Strike, position = strike, speed = setup.shot.ballSpeed });
            Vector3 before = strike, last = strike;
            bool flying = true, landed = false;
            int steps = 0;
            while (ghost.InMotion && steps * GolfBall.Step < MaxTime)
            {
                ghost.Advance(GolfBall.Step);
                steps++;
                var p = ghost.transform.position;
                float time = steps * GolfBall.Step;
                // A contact: the ball was coming down while flying and now goes up again, or it starts rolling.
                bool down = p.y < last.y - 1e-5f, wasDown = last.y < before.y - 1e-5f;
                // (Water, out of bounds and the cup are their own events at the end.)
                bool onGround = ghost.Status is BallStatus.Rolling or BallStatus.Stopped;
                if (flying && ((wasDown && !down && ghost.Status == BallStatus.Flying && NearGround(last)) || onGround))
                {
                    var at = onGround ? p : last;
                    events.Add(new ShotEvent
                    {
                        kind = ShotEventKind.Contact, time = time, position = at, speed = (last - before).magnitude / GolfBall.Step,
                        surface = CourseSurface.At(at), first = !landed,
                    });
                    landed = true;
                }
                flying = ghost.Status == BallStatus.Flying;
                if (!flying && ghost.InMotion) closestToPin = Mathf.Min(closestToPin, Flat(p - pin).magnitude);
                Apex = Mathf.Max(Apex, p.y - strike.y);
                before = last;
                last = p;
                if (steps % 2 == 0 || !ghost.InMotion) samples.Add(p);
            }
            if (samples.Count < 2) samples.Add(last);
            var kind = ghost.Status switch
            {
                BallStatus.Holed => ShotEventKind.Holed,
                BallStatus.InWater => ShotEventKind.Water,
                BallStatus.OutOfBounds => ShotEventKind.OutOfBounds,
                _ => ShotEventKind.Rest,
            };
            if (ghost.Status == BallStatus.Holed) closestToPin = 0f;
            events.Add(new ShotEvent { kind = kind, time = Duration, position = last, surface = CourseSurface.At(last) });
            if (ghost.Status == BallStatus.InWater) FindSplash();
        }

        /// <summary>The ball ends on the hazard's bed, under the water: back along the path to where it crossed the surface.</summary>
        void FindSplash()
        {
            float level = CourseSurface.WaterLevelAt(Rest) ?? Rest.y;
            int i = samples.Count - 1;
            while (i > 0 && samples[i - 1].y < level) i--;
            if (i == 0)
            {
                SplashTime = 0f;
                SplashPoint = new Vector3(Launch.x, level, Launch.z);
                return;
            }
            Vector3 above = samples[i - 1], below = samples[i];
            float u = above.y > below.y ? Mathf.Clamp01((above.y - level) / (above.y - below.y)) : 1f;
            SplashTime = (i - 1 + u) * SampleTime;
            var p = Vector3.Lerp(above, below, u);
            SplashPoint = new Vector3(p.x, level, p.z);
        }

        static bool NearGround(Vector3 p) => p.y - CourseSurface.GroundAt(p) < BallPhysicsSettings.Radius + 0.03f;

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
