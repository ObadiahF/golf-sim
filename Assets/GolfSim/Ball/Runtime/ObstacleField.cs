using GolfSim.Course;
using UnityEngine;

namespace GolfSim.Ball
{
    /// <summary>What the ball hit in one step: the obstacle kind, whether it was leaves (canopy) or solid, where and how hard.</summary>
    public readonly struct ObstacleHit
    {
        public readonly byte kind;
        public readonly bool canopy;
        public readonly Vector3 point;
        /// <summary>Speed into the surface (solid) or ball speed (canopy), m/s, e.g. for the sound's volume.</summary>
        public readonly float impactSpeed;

        public ObstacleHit(byte kind, bool canopy, Vector3 point, float impactSpeed)
        {
            this.kind = kind;
            this.canopy = canopy;
            this.point = point;
            this.impactSpeed = impactSpeed;
        }

        public bool IsRock => ObstacleKinds.IsRock(kind);
        public string Label => IsRock ? "Hit rock" : "Hit tree";
    }

    /// <summary>
    /// The hole's trees, shrubs and rocks in a world-space grid, so the ball's 2 ms path segment is tested only against
    /// the few obstacles near it (one array lookup when it flies over the treetops). No Unity physics: solids are
    /// upright cylinders (trunks, shrubs) and domes (rocks) the ball rebounds from; tree crowns are hit by chance per
    /// meter travelled through them (leaves and branches), slowing, deflecting and usually dropping the ball. Crowns are
    /// the ones the trees' models draw, measured when the hole was built (ObstacleSettings.CrownOf).
    /// </summary>
    public class ObstacleField
    {
        const float CellSize = 8f;
        const float R = BallPhysicsSettings.Radius;
        const float PushOut = 0.002f; // m off the surface after a rebound

        public readonly HoleInfo hole;

        readonly ObstacleSettings settings;
        readonly ObstacleBody[] bodies;
        readonly int[] cellStart, cellItems; // CSR: obstacles in cell c are cellItems[cellStart[c] .. cellStart[c + 1])
        readonly float[] cellTop;            // highest obstacle top in each cell, to skip cells the ball is above
        readonly int[] stamp;                // last query that tested each obstacle (an obstacle sits in several cells)
        readonly int nx, nz;
        readonly float minX, minZ;
        int query;

        public int Count => bodies.Length;

        public ObstacleField(HoleInfo hole, ObstacleSettings settings)
        {
            this.hole = hole;
            this.settings = settings;
            var source = hole.obstacles ?? new Obstacle[0];
            bodies = new ObstacleBody[source.Length];
            stamp = new int[source.Length];
            float maxX = float.MinValue, maxZ = float.MinValue;
            minX = minZ = float.MaxValue;
            for (int i = 0; i < source.Length; i++)
            {
                bodies[i] = ToWorld(source[i]);
                float e = bodies[i].Extent + R;
                var p = bodies[i].basePosition;
                minX = Mathf.Min(minX, p.x - e); maxX = Mathf.Max(maxX, p.x + e);
                minZ = Mathf.Min(minZ, p.z - e); maxZ = Mathf.Max(maxZ, p.z + e);
            }
            if (bodies.Length == 0) minX = minZ = maxX = maxZ = 0f;
            nx = Mathf.Max(1, Mathf.CeilToInt((maxX - minX) / CellSize));
            nz = Mathf.Max(1, Mathf.CeilToInt((maxZ - minZ) / CellSize));

            // Two passes: count obstacles per cell, then fill.
            int cells = nx * nz;
            cellStart = new int[cells + 1];
            cellTop = new float[cells];
            for (int c = 0; c < cells; c++) cellTop[c] = float.MinValue;
            for (int i = 0; i < bodies.Length; i++) ForCells(bodies[i], c => cellStart[c + 1]++);
            for (int c = 0; c < cells; c++) cellStart[c + 1] += cellStart[c];
            cellItems = new int[cellStart[cells]];
            var fill = (int[])cellStart.Clone();
            for (int i = 0; i < bodies.Length; i++)
            {
                int index = i;
                ForCells(bodies[i], c =>
                {
                    cellItems[fill[c]++] = index;
                    cellTop[c] = Mathf.Max(cellTop[c], bodies[index].top);
                });
            }
        }

        ObstacleBody ToWorld(Obstacle o)
        {
            var t = hole.transform;
            float across = t.lossyScale.x, up = t.lossyScale.y;
            var body = new ObstacleBody
            {
                basePosition = t.TransformPoint(o.position),
                radius = o.radius * across,
                kind = o.kind,
                rock = ObstacleKinds.IsRock(o.kind),
            };
            body.top = body.basePosition.y + o.height * up;
            body.crown = settings.CrownOf(o).Scaled(across, up); // the drawn crown, or the kind's default
            return body;
        }

        void ForCells(in ObstacleBody o, System.Action<int> action)
        {
            float e = o.Extent + R;
            int x0 = CellX(o.basePosition.x - e), x1 = CellX(o.basePosition.x + e);
            int z0 = CellZ(o.basePosition.z - e), z1 = CellZ(o.basePosition.z + e);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++) action(z * nx + x);
        }

        int CellX(float x) => Mathf.Clamp((int)((x - minX) / CellSize), 0, nx - 1);
        int CellZ(float z) => Mathf.Clamp((int)((z - minZ) / CellSize), 0, nz - 1);

        /// <summary>
        /// Tests the step the ball just took (from → s.position). A solid hit rebounds the ball off the surface (and
        /// pushes it out); otherwise a canopy it is inside may, by chance, knock it about. Returns null for no hit.
        /// </summary>
        public ObstacleHit? Collide(ref BallState s, Vector3 from, ref ShotRandom rng, bool rolling)
        {
            if (bodies.Length == 0) return null;
            var to = s.position;
            float low = Mathf.Min(from.y, to.y) - R;
            if (Mathf.Max(from.x, to.x) + R < minX || Mathf.Min(from.x, to.x) - R > minX + nx * CellSize ||
                Mathf.Max(from.z, to.z) + R < minZ || Mathf.Min(from.z, to.z) - R > minZ + nz * CellSize) return null;
            int x0 = CellX(Mathf.Min(from.x, to.x) - R), x1 = CellX(Mathf.Max(from.x, to.x) + R);
            int z0 = CellZ(Mathf.Min(from.z, to.z) - R), z1 = CellZ(Mathf.Max(from.z, to.z) + R);

            query++;
            int solid = -1, canopy = -1;
            float bestT = float.MaxValue;
            var bestNormal = Vector3.zero;
            var mid = (from + to) * 0.5f;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int c = z * nx + x;
                    if (low > cellTop[c]) continue; // flying over everything in this cell
                    for (int k = cellStart[c]; k < cellStart[c + 1]; k++)
                    {
                        int i = cellItems[k];
                        if (stamp[i] == query) continue;
                        stamp[i] = query;
                        ref var o = ref bodies[i];
                        if (low > o.top) continue;
                        if (ObstacleShapes.Solid(o, from, to, out float t, out var n))
                        {
                            if (t < bestT) { bestT = t; bestNormal = n; solid = i; }
                        }
                        else if (!rolling && canopy < 0 && o.crown.density > 0f && ObstacleShapes.InCanopy(o, mid)) canopy = i;
                    }
                }

            if (solid >= 0) return Rebound(ref s, from + (to - from) * bestT, bestNormal, bodies[solid], ref rng);
            if (canopy >= 0)
            {
                float chance = 1f - Mathf.Exp(-bodies[canopy].crown.density * Vector3.Distance(from, to));
                if (rng.Next01() < chance) return Deflect(ref s, bodies[canopy], ref rng);
            }
            return null;
        }

        /// <summary>True if this point is inside any solid trunk, shrub or rock (tests).</summary>
        public bool InsideSolid(Vector3 p)
        {
            if (bodies.Length == 0) return false;
            int c = CellZ(p.z) * nx + CellX(p.x);
            for (int k = cellStart[c]; k < cellStart[c + 1]; k++)
                if (ObstacleShapes.Inside(bodies[cellItems[k]], p)) return true;
            return false;
        }

        /// <summary>Bounce off a trunk or rock: restitution along the normal, friction along the surface, a little scatter, most spin lost.</summary>
        ObstacleHit Rebound(ref BallState s, Vector3 contact, Vector3 n, in ObstacleBody o, ref ShotRandom rng)
        {
            if (ObstacleShapes.Inside(o, contact)) contact = ObstacleShapes.SurfacePoint(o, contact);
            var v = s.velocity;
            float vn = Vector3.Dot(v, n);
            if (vn < 0f)
            {
                float e = rng.Range(settings.RestitutionFor(o.kind));
                v = (v - vn * n) * settings.surfaceSpeedKept - e * vn * n;
                v = Quaternion.Euler(rng.Range(-settings.scatter, settings.scatter), rng.Range(-settings.scatter, settings.scatter), 0f) * v;
                if (Vector3.Dot(v, n) < 0f) v = Vector3.Reflect(v, n); // scatter never sends it back into the surface
                s.velocity = v;
                s.spin *= settings.spinKept;
            }
            s.position = contact + n * PushOut;
            return new ObstacleHit(o.kind, false, contact, Mathf.Max(0f, -vn));
        }

        /// <summary>Leaves and branches: keeps 20–60% of the speed, turns it up to ±45° and usually knocks it down.</summary>
        ObstacleHit Deflect(ref BallState s, in ObstacleBody o, ref ShotRandom rng)
        {
            float speed = s.velocity.magnitude;
            var dir = speed > 1e-4f ? s.velocity / speed : Vector3.down;
            var flat = new Vector3(dir.x, 0f, dir.z);
            float flatLength = flat.magnitude;
            float elevation = Mathf.Atan2(dir.y, flatLength) * Mathf.Rad2Deg + rng.Range(settings.canopyPitch);
            float heading = rng.Range(-settings.canopyDeflection, settings.canopyDeflection);
            if (flatLength < 1e-3f)
            {
                heading = rng.Range(0f, 360f); // dropping straight down: any way out
                flat = Vector3.forward;
            }
            else flat /= flatLength;
            flat = Quaternion.AngleAxis(heading, Vector3.up) * flat;
            elevation = Mathf.Clamp(elevation, -85f, 60f) * Mathf.Deg2Rad;
            s.velocity = (flat * Mathf.Cos(elevation) + Vector3.up * Mathf.Sin(elevation)) * speed * rng.Range(settings.canopySpeedKept);
            s.spin *= settings.canopySpinKept;
            return new ObstacleHit(o.kind, true, s.position, speed);
        }
    }
}
