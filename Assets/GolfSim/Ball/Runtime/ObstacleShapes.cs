using UnityEngine;

namespace GolfSim.Ball
{
    /// <summary>One obstacle in world space: its solid part (trunk / body cylinder or rock dome) and its tree canopy.</summary>
    struct ObstacleBody
    {
        public Vector3 basePosition; // on the ground
        public float radius;         // solid trunk / body / rock radius
        public float top;            // world y of the top
        public byte kind;
        public bool rock;            // dome instead of an upright cylinder
        public Crown crown;          // trees only (radius 0 = none), heights above the base

        public float Height => top - basePosition.y;
        public float Extent => Mathf.Max(radius, crown.radius);
    }

    /// <summary>Ball-vs-obstacle geometry. The ball is a point; every solid is grown by the ball radius.</summary>
    static class ObstacleShapes
    {
        const float R = BallPhysicsSettings.Radius;
        const float CapTilt = 1f; // a trunk / shrub top sheds the ball sideways (45°) instead of balancing it

        /// <summary>First contact of the segment a→b with the obstacle's solid part: t in [0, 1] along it, and the outward normal.</summary>
        public static bool Solid(in ObstacleBody o, Vector3 a, Vector3 b, out float t, out Vector3 normal) =>
            o.rock ? Dome(o, a, b, out t, out normal) : Cylinder(o, a, b, out t, out normal);

        /// <summary>True if the point is inside the solid part (grown by the ball radius, less a hair).</summary>
        public static bool Inside(in ObstacleBody o, Vector3 p)
        {
            var d = p - o.basePosition;
            if (o.rock)
            {
                float a = o.radius + R - 1e-3f, h = o.Height + R - 1e-3f;
                return d.y >= 0f && (d.x * d.x + d.z * d.z) / (a * a) + d.y * d.y / (h * h) < 1f;
            }
            float rr = o.radius + R - 1e-3f;
            return d.y < o.Height && d.x * d.x + d.z * d.z < rr * rr;
        }

        /// <summary>True if the point is inside the tree's crown.</summary>
        public static bool InCanopy(in ObstacleBody o, Vector3 p)
        {
            float r = o.crown.RadiusAt(p.y - o.basePosition.y);
            float dx = p.x - o.basePosition.x, dz = p.z - o.basePosition.z;
            return dx * dx + dz * dz < r * r;
        }

        /// <summary>Upright cylinder from the ground to the top: side wall, or a top that sheds the ball.</summary>
        static bool Cylinder(in ObstacleBody o, Vector3 a, Vector3 b, out float t, out Vector3 normal)
        {
            t = 0f;
            normal = Vector3.zero;
            float rr = o.radius + R;
            float px = a.x - o.basePosition.x, pz = a.z - o.basePosition.z;
            float dx = b.x - a.x, dz = b.z - a.z;
            float c = px * px + pz * pz - rr * rr;
            float capY = o.top + R;

            if (c <= 0f)
            {
                // Starts over the cylinder: crossing the top, or already inside the wall (push straight out).
                if (a.y >= capY)
                {
                    if (b.y >= capY) return false;
                    t = (a.y - capY) / (a.y - b.y);
                    var outward = new Vector3(px + dx * t, 0f, pz + dz * t);
                    if (outward.sqrMagnitude < 1e-8f) outward = new Vector3(-dx, 0f, -dz);
                    normal = (Vector3.up + outward.normalized * CapTilt).normalized;
                    return true;
                }
                if (a.y < o.basePosition.y - R) return false;
                normal = new Vector3(px, 0f, pz);
                normal = normal.sqrMagnitude > 1e-10f ? normal.normalized : Vector3.forward;
                return true;
            }

            float qa = dx * dx + dz * dz;
            if (qa < 1e-12f) return false;
            float qb = 2f * (px * dx + pz * dz);
            float disc = qb * qb - 4f * qa * c;
            if (disc < 0f) return false;
            t = (-qb - Mathf.Sqrt(disc)) / (2f * qa);
            if (t < 0f || t > 1f) return false;
            float y = a.y + (b.y - a.y) * t;
            if (y > o.top || y < o.basePosition.y - R) return false;
            normal = new Vector3(px + dx * t, 0f, pz + dz * t) / rr;
            return true;
        }

        /// <summary>Rock: the upper half of an ellipsoid (radius across, height up) sitting on the ground.</summary>
        static bool Dome(in ObstacleBody o, Vector3 a, Vector3 b, out float t, out Vector3 normal)
        {
            t = 0f;
            float ax = o.radius + R, ay = o.Height + R;
            var scale = new Vector3(1f / ax, 1f / ay, 1f / ax);
            var p = Vector3.Scale(a - o.basePosition, scale);
            var d = Vector3.Scale(b - a, scale);
            float c = p.sqrMagnitude - 1f;
            if (c <= 0f)
            {
                normal = Gradient(p, ax, ay); // already inside: push out along the surface normal
                return p.y >= -R / ay;
            }
            normal = Vector3.zero;
            float qa = d.sqrMagnitude;
            if (qa < 1e-12f) return false;
            float qb = 2f * Vector3.Dot(p, d);
            float disc = qb * qb - 4f * qa * c;
            if (disc < 0f) return false;
            t = (-qb - Mathf.Sqrt(disc)) / (2f * qa);
            if (t < 0f || t > 1f) return false;
            var q = p + d * t;
            if (q.y < -R / ay) return false; // below the ground line
            normal = Gradient(q, ax, ay);
            return true;
        }

        /// <summary>Outward normal of the ellipsoid at unit-sphere point q.</summary>
        static Vector3 Gradient(Vector3 q, float ax, float ay)
        {
            var n = new Vector3(q.x / ax, q.y / ay, q.z / ax);
            return n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
        }

        /// <summary>Puts a point that is inside the solid back on its surface (along the normal).</summary>
        public static Vector3 SurfacePoint(in ObstacleBody o, Vector3 p)
        {
            var d = p - o.basePosition;
            if (o.rock)
            {
                float ax = o.radius + R, ay = o.Height + R;
                var q = new Vector3(d.x / ax, Mathf.Max(d.y, 0f) / ay, d.z / ax);
                if (q.sqrMagnitude < 1e-10f) q = Vector3.up;
                q.Normalize();
                return o.basePosition + new Vector3(q.x * ax, q.y * ay, q.z * ax);
            }
            var flat = new Vector3(d.x, 0f, d.z);
            flat = flat.sqrMagnitude > 1e-10f ? flat.normalized : Vector3.forward;
            return new Vector3(o.basePosition.x, p.y, o.basePosition.z) + flat * (o.radius + R);
        }
    }
}
