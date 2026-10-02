using UnityEngine;

namespace GolfSim.Ball
{
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
    ///  * roll: rolling resistance per surface plus slope (5/7 g for a rolling sphere), so putts break.
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

        /// <summary>One rolling step on the ground. Returns false once the ball has come to rest.</summary>
        public static bool Roll(ref BallState s, float dt, Vector3 normal, BallPhysicsSettings.SurfaceResponse surface)
        {
            const float g = BallPhysicsSettings.Gravity;
            var downhill = Vector3.ProjectOnPlane(Vector3.down * g, normal) * (5f / 7f);
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
    }
}
