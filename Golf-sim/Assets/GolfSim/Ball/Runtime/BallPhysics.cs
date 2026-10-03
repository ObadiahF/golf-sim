using GolfSim.Course;
using UnityEngine;

namespace GolfSim.Ball
{
    /// <summary>What BallPhysics.Cup did this step.</summary>
    public enum CupContact
    {
        /// <summary>The ball isn't down in the hole: carry on flying or rolling.</summary>
        Clear,
        /// <summary>The ball is in the hole's opening, falling, on the lip or against the liner (the step is done).</summary>
        Inside,
        /// <summary>It reached the bottom of the cup.</summary>
        Holed,
    }

    public struct BallState
    {
        public Vector3 position;
        public Vector3 velocity;
        public Vector3 spin; // angular velocity, rad/s (world axis)
    }

    /// <summary>
    /// Pure golf-ball physics, no Unity physics engine:
    ///  * flight: gravity, drag and Magnus lift (coefficients fitted to TrackMan data), spin decay;
    ///  * bounce: Penner's turf model (restitution falls with impact speed, the turf crater tilts the
    ///    contact plane, friction trades speed for spin, so wedges check and can spin back);
    ///  * roll: rolling resistance per surface plus slope (5/7 g for a rolling sphere), so putts break;
    ///  * the cup: a real hole. Over the opening there is no ground, so the ball drops; it rattles off the rim and
    ///    the liner (with its spin, so a fast ball climbs the far lip and hops out), and is holed at the bottom.
    /// The flight step must match Tools/ball_physics/calibrate.py.
    /// </summary>
    public static class BallPhysics
    {
        const float Rpm = 2f * Mathf.PI / 60f;
        const float AirViscosity = 1.85e-5f;

        public static BallState Launch(Vector3 origin, Vector3 targetDirection, ShotData shot)
        {
            var flat = Vector3.ProjectOnPlane(targetDirection, Vector3.up).normalized;
            var aim = Quaternion.AngleAxis(shot.launchDirection, Vector3.up) * flat;
            var right = Vector3.Cross(Vector3.up, aim);
            var dir = Quaternion.AngleAxis(-shot.launchAngle, right) * aim;
            return new BallState
            {
                position = origin,
                velocity = dir * shot.ballSpeed,
                // Backspin turns about -right (top of the ball moving back); + sidespin about +up curves right.
                spin = (-right * shot.backspin + Vector3.up * shot.sidespin) * Rpm,
            };
        }

        public static void Fly(ref BallState s, float dt, BallPhysicsSettings p, Vector3 wind)
        {
            var air = s.velocity - wind;
            float speed = air.magnitude;
            var accel = Vector3.down * BallPhysicsSettings.Gravity;
            if (speed > 0.01f)
            {
                float rho = p.AirDensity;
                float k = 0.5f * rho * BallPhysicsSettings.Area / BallPhysicsSettings.Mass;
                float spinRate = s.spin.magnitude;
                float sp = BallPhysicsSettings.Radius * spinRate / speed;
                float re = rho * speed * 2f * BallPhysicsSettings.Radius / AirViscosity;
                float cd = p.cd0 + p.cdSpin * sp + p.cdRe * (1e5f / re);
                float cl = sp > 0f ? p.clScale * Mathf.Pow(sp, p.clPower) : 0f;
                accel += -k * cd * speed * air;
                if (spinRate > 1e-3f)
                    accel += k * cl * speed * speed * Vector3.Cross(s.spin / spinRate, air / speed);
            }
            s.velocity += accel * dt;
            s.position += s.velocity * dt;
            s.spin *= Mathf.Exp(-dt / p.spinDecayTime);
        }

        /// <summary>Penner (2002) bounce off turf. Returns false if the ball wasn't moving into the surface.</summary>
        public static bool Bounce(ref BallState s, Vector3 normal, BallPhysicsSettings.SurfaceResponse surface)
        {
            float r = BallPhysicsSettings.Radius;
            float vIn = -Vector3.Dot(s.velocity, normal);
            if (vIn <= 0f) return false;

            var tangent = s.velocity + vIn * normal;
            float vAlong = tangent.magnitude;
            var t = vAlong > 1e-4f ? tangent / vAlong : Vector3.ProjectOnPlane(Vector3.forward, normal).normalized;
            var axis = Vector3.Cross(normal, t); // backspin turns about -axis
            float speed = s.velocity.magnitude;

            // The ball digs a small crater; its far wall rises ahead of the ball, so relative to the
            // effective (tilted) surface the impact is steeper: incidence = impact + crater angle.
            float impactDeg = Mathf.Atan2(vIn, vAlong) * Mathf.Rad2Deg;
            float craterDeg = Mathf.Min(surface.compliance * 15.4f * (speed / 18.6f) * (impactDeg / 44.4f), 85f - impactDeg);
            craterDeg = Mathf.Max(craterDeg, 0f);
            float c = craterDeg * Mathf.Deg2Rad, impact = (impactDeg + craterDeg) * Mathf.Deg2Rad;
            float vx = speed * Mathf.Cos(impact), vy = speed * Mathf.Sin(impact);

            float backspin = Vector3.Dot(s.spin, -axis);
            float e = Restitution(vy) * surface.restitution;
            float mu = surface.friction;
            float vx2, backspin2;
            if (mu < 2f * (vx + r * backspin) / (7f * (1f + e) * vy))
            {
                vx2 = vx - mu * (1f + e) * vy;                         // slides throughout the impact
                backspin2 = backspin - 5f * mu / (2f * r) * (1f + e) * vy;
            }
            else
            {
                vx2 = (5f * vx - 2f * r * backspin) / 7f;               // grips and leaves rolling
                backspin2 = -vx2 / r;
            }
            float vy2 = e * vy;

            float outAlong = vx2 * Mathf.Cos(c) - vy2 * Mathf.Sin(c);
            float outUp = vx2 * Mathf.Sin(c) + vy2 * Mathf.Cos(c);
            s.velocity = t * outAlong + normal * Mathf.Max(outUp, 0f);

            var sideSpin = s.spin - Vector3.Dot(s.spin, axis) * axis;
            s.spin = -axis * backspin2 + sideSpin * 0.5f; // turf scrubs off much of the sidespin
            return true;
        }

        /// <summary>Coefficient of restitution on a firm green vs. normal impact speed (Penner).</summary>
        static float Restitution(float vNormal) =>
            vNormal < 20f ? Mathf.Clamp(0.510f - 0.0375f * vNormal + 0.000903f * vNormal * vNormal, 0.12f, 0.6f) : 0.12f;

        /// <summary>
        /// One rolling step on the ground. Returns false once the ball has come to rest. pull: extra acceleration along
        /// the ground, e.g. LipPull toward a hole the ball hangs over.
        /// </summary>
        public static bool Roll(ref BallState s, float dt, Vector3 normal, BallPhysicsSettings.SurfaceResponse surface, Vector3 pull = default)
        {
            const float g = BallPhysicsSettings.Gravity;
            var downhill = Vector3.ProjectOnPlane(Vector3.down * g, normal) * (5f / 7f) + Vector3.ProjectOnPlane(pull, normal);
            var v = Vector3.ProjectOnPlane(s.velocity, normal);
            float resistance = surface.rolling * g * Vector3.Dot(normal, Vector3.up);

            if (v.magnitude < 0.03f && downhill.magnitude <= resistance * 1.2f)
            {
                s.velocity = Vector3.zero;
                s.spin = Vector3.zero;
                return false; // static friction holds it on this slope
            }
            var accel = downhill - (v.sqrMagnitude > 1e-6f ? v.normalized * resistance : Vector3.zero);
            var next = v + accel * dt;
            if (Vector3.Dot(next, v) < 0f && downhill.magnitude <= resistance) next = Vector3.zero; // don't reverse on flat ground
            s.velocity = next;
            s.position += next * dt;
            s.spin = Vector3.Cross(normal, next) / BallPhysicsSettings.Radius; // rolling without slipping
            return true;
        }

        const float LipRestitution = 0.3f; // a ball off the rim or the liner keeps this much of its speed into it
        const float LipFriction = 0.4f;

        /// <summary>True if this point is over the hole's opening (where the green has no ground).</summary>
        public static bool OverCup(Vector3 position, Vector3 pin) => Flat(position - pin).magnitude < GolfBall.CupRadius;

        /// <summary>
        /// One step of the ball in the hole, if it is down in it: its centre over the opening and no higher than a
        /// ball resting on the green. There it falls freely and bounces off the cup: the rim (a circle at the green's
        /// height all round) and below it the liner wall, contact taking the ball's spin into account (a rolling ball
        /// hitting the far lip climbs it: the speed it needs to hop out is about 1.9 m/s dead centre, less off centre,
        /// and a ball catching the edge spins round it). It is Holed on reaching the bottom of the cup. Returns Clear,
        /// without touching the ball, anywhere else.
        /// </summary>
        public static CupContact Cup(ref BallState s, float dt, Vector3 pin, TerrainSurfaceMap map)
        {
            const float r = BallPhysicsSettings.Radius, R = GolfBall.CupRadius;
            if (!OverCup(s.position, pin) || s.position.y > map.HeightAt(s.position) + r + 0.002f) return CupContact.Clear;

            s.velocity += Vector3.down * (BallPhysicsSettings.Gravity * dt);
            s.position += s.velocity * dt;

            float floor = map.HeightAt(pin) - CupBuilder.Depth;
            var flat = Flat(s.position - pin);
            float d = flat.magnitude;
            var outward = d > 1e-5f ? flat / d : Flat(s.velocity).sqrMagnitude > 1e-8f ? Flat(s.velocity).normalized : Vector3.forward;
            if (s.position.y - r <= floor)
            {
                var rest = pin + outward * Mathf.Min(d, R - r);
                s.position = new Vector3(rest.x, floor + r, rest.z);
                s.velocity = Vector3.zero;
                return CupContact.Holed;
            }

            // The nearest point of the cup: on the rim in the ball's direction, or below it on the liner.
            var rim = pin + outward * R;
            rim.y = map.HeightAt(rim);
            Vector3 normal;
            float depth;
            if (s.position.y < rim.y)
            {
                if (d <= R - r) return CupContact.Inside; // dropping clear of the liner
                normal = -outward;
                depth = d - (R - r);
            }
            else
            {
                var fromRim = s.position - rim;
                float gap = fromRim.magnitude;
                if (gap >= r || gap < 1e-6f) return CupContact.Inside;
                normal = fromRim / gap;
                depth = r - gap;
            }
            s.position += normal * depth;

            float into = Vector3.Dot(s.velocity, normal);
            if (into < 0f)
            {
                float impulse = -(1f + LipRestitution) * into;
                s.velocity += normal * impulse;
                // Friction at the contact point (the ball's spin included) up to the point where it rolls on the lip.
                var slip = s.velocity + Vector3.Cross(s.spin, -normal * r);
                slip -= Vector3.Dot(slip, normal) * normal;
                float slipSpeed = slip.magnitude;
                if (slipSpeed > 1e-6f)
                {
                    var friction = -slip / slipSpeed * Mathf.Min(LipFriction * impulse, 2f / 7f * slipSpeed);
                    s.velocity += friction;
                    s.spin += Vector3.Cross(-normal, friction) * (5f / (2f * r));
                }
            }
            return CupContact.Inside;
        }

        /// <summary>
        /// The hole's pull on a ball hanging over its edge: with part of it over the cup the ball tips in, harder the
        /// further it overhangs (5/7 g with a whole ball radius over). Zero clear of the hole, or with no hole.
        /// Without it a ball creeping up to the cup could stop half over it and stay there.
        /// </summary>
        public static Vector3 LipPull(Vector3 position, Vector3? cup)
        {
            if (cup is not Vector3 pin) return Vector3.zero;
            var toCup = Vector3.ProjectOnPlane(pin - position, Vector3.up);
            float offset = toCup.magnitude, r = BallPhysicsSettings.Radius;
            float overhang = GolfBall.CupRadius + r - offset;
            if (overhang <= 0f || offset < 1e-4f) return Vector3.zero;
            return toCup / offset * (5f / 7f * BallPhysicsSettings.Gravity * Mathf.Clamp01(overhang / r));
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
