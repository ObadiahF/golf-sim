using System.Collections.Generic;
using UnityEngine;

namespace GolfSim.Ball
{
    /// <summary>Where a simulated putt ends.</summary>
    public struct PuttPrediction
    {
        public Vector3 end;
        /// <summary>It dropped (only when a cup was given).</summary>
        public bool holed;
        /// <summary>It was still rolling when the step cap ran out (e.g. down a slope too steep to stop on).</summary>
        public bool stillMoving;
        public float time;
    }

    /// <summary>
    /// Rolls a copy of a putt over the hole's terrain with the same BallPhysics steps as GolfBall (launch with the lie,
    /// the short hop, bounce, then Roll on the terrain normal and surface), without touching the real ball, trees or
    /// wind. A coarse roll step keeps it cheap enough to re-run while the player aims (the short launch hop always
    /// uses GolfBall.Step: a coarse step lands it too steeply and costs speed); GolfBall.Step reproduces the ball.
    /// </summary>
    public static class PuttPredictor
    {
        public const float CoarseStep = 0.01f;
        public const float MaxTime = 15f; // s: longer than any putt that stops (1500 coarse steps)

        /// <summary>
        /// Simulates a putt from origin along aim. cup: the pin to drop into (null rolls over it). path: if given,
        /// gets a point every pathSpacing metres of travel, starting at the origin and ending at the rest point.
        /// </summary>
        public static PuttPrediction Simulate(TerrainSurfaceMap map, BallPhysicsSettings settings, Vector3 origin, Vector3 aim,
                                              ShotData shot, Vector3? cup = null, List<Vector3> path = null,
                                              float pathSpacing = 0.1f, float step = CoarseStep, float maxTime = MaxTime)
        {
            var lie = settings.LieFor(map.Contains(origin) ? map.SurfaceAt(origin) : "green", shot.ballSpeed);
            var s = BallPhysics.Launch(origin, aim, lie.Apply(shot));
            bool flying = true;
            float sinceDot = 0f;
            path?.Clear();
            path?.Add(origin);
            var result = new PuttPrediction { stillMoving = true };
            float time = 0f;
            while (time < maxTime)
            {
                var from = s.position;
                float dt = flying ? Mathf.Min(step, GolfBall.Step) : step;
                time += dt;
                if (Step(ref s, ref flying, map, settings, dt, cup, ref result)) break;
                float moved = Flat(s.position - from).magnitude;
                sinceDot += moved;
                if (path != null && sinceDot >= pathSpacing)
                {
                    path.Add(s.position);
                    sinceDot = 0f;
                }
            }
            result.end = result.holed ? result.end : s.position;
            result.time = time;
            if (path != null && (path.Count == 1 || path[path.Count - 1] != result.end)) path.Add(result.end);
            return result;
        }

        /// <summary>One step; true when the putt is over (stopped, holed, in a hazard or off the map).</summary>
        static bool Step(ref BallState s, ref bool flying, TerrainSurfaceMap map, BallPhysicsSettings settings, float dt,
                         Vector3? cup, ref PuttPrediction result)
        {
            if (flying)
            {
                BallPhysics.Fly(ref s, dt, settings, Vector3.zero);
                if (!map.Contains(s.position)) return Stop(ref result);
                float ground = map.HeightAt(s.position) + BallPhysicsSettings.Radius;
                if (s.position.y > ground) return false;
                s.position.y = ground;
                var landing = settings.For(map.SurfaceAt(s.position));
                if (landing.hazard) return Stop(ref result);
                if (InCup(s, cup, ref result)) return true;
                var normal = map.NormalAt(s.position);
                BallPhysics.Bounce(ref s, normal, landing);
                if (Vector3.Dot(s.velocity, normal) < GolfBall.RollSpeed)
                {
                    s.velocity = Vector3.ProjectOnPlane(s.velocity, normal);
                    flying = false;
                }
                return false;
            }

            if (InCup(s, cup, ref result)) return true;
            var surface = settings.For(map.SurfaceAt(s.position));
            if (surface.hazard) return Stop(ref result);
            bool moving = BallPhysics.Roll(ref s, dt, map.NormalAt(s.position), surface, BallPhysics.LipPull(s.position, cup));
            if (!map.Contains(s.position)) return Stop(ref result);
            s.position.y = map.HeightAt(s.position) + BallPhysicsSettings.Radius;
            return !moving && (InCup(s, cup, ref result) || Stop(ref result));
        }

        static bool Stop(ref PuttPrediction result)
        {
            result.stillMoving = false;
            return true;
        }

        static bool InCup(BallState s, Vector3? cup, ref PuttPrediction result)
        {
            if (cup is not Vector3 pin) return false;
            if (Flat(s.position - pin).magnitude > GolfBall.CupRadius || Flat(s.velocity).magnitude > GolfBall.CupCaptureSpeed) return false;
            result.holed = true;
            result.end = pin;
            return Stop(ref result);
        }

        /// <summary>
        /// The putter ball speed that finishes `overshoot` metres past the pin (measured along the line from the ball to
        /// the pin) when struck along aim on this terrain: bisection, starting from the flat-green estimate.
        /// </summary>
        public static float SolveSpeed(TerrainSurfaceMap map, BallPhysicsSettings settings, Vector3 origin, Vector3 aim,
                                       Vector3 pin, float overshoot = PuttModel.Overshoot, float step = CoarseStep, int iterations = 12)
        {
            var toPin = Flat(pin - origin);
            float target = toPin.magnitude + overshoot;
            var line = toPin.sqrMagnitude > 1e-6f ? toPin.normalized : Flat(aim).normalized;
            float Reach(float speed) =>
                Vector3.Dot(Flat(Simulate(map, settings, origin, aim, PuttModel.PuttAt(speed), step: step).end - origin), line);

            float guess = PuttModel.SpeedFor(target, PuttModel.GreenStimp(settings));
            float lo = guess * 0.5f, hi = guess * 1.5f;
            for (int k = 0; k < 6 && Reach(lo) > target; k++) lo *= 0.5f;
            for (int k = 0; k < 6 && Reach(hi) < target && hi < 20f; k++) hi *= 1.6f;
            for (int k = 0; k < iterations; k++)
            {
                float mid = 0.5f * (lo + hi);
                if (Reach(mid) < target) lo = mid;
                else hi = mid;
            }
            return 0.5f * (lo + hi);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
