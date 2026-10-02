using System.Collections.Generic;
using GolfSim.Ball;
using GolfSim.Course;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// Where a replay camera may stand: above the terrain, outside every tree crown, trunk and rock (with a margin,
    /// since the drawn trees are bushier than their collision shapes), and with a clear view of what it films.
    /// Obstacles are bucketed in a coarse grid, built once per hole.
    /// </summary>
    public class CameraSpots
    {
        const float Cell = 12f;
        const float Margin = 2.5f;       // m kept from any crown or trunk
        const float CrownScale = 1.25f;  // drawn crowns are wider than the collision crowns
        const float MinClearance = 1.2f; // m above the terrain

        struct Volume
        {
            public Vector3 basePosition;
            public float radius, bottom, top;     // the camera keeps out of this (with the margin)
            public float trunk, crown, crownBottom; // what blocks a view: the trunk, and the crown above crownBottom
        }

        readonly List<Volume> volumes = new List<Volume>();
        readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
        public readonly HoleInfo hole;

        public CameraSpots(HoleInfo hole, ObstacleSettings settings)
        {
            this.hole = hole;
            var t = hole.transform;
            foreach (var o in hole.obstacles ?? new Obstacle[0])
            {
                var p = t.TransformPoint(o.position);
                float height = o.height * t.lossyScale.y;
                var canopy = o.IsTree ? settings.CanopyFor(o.kind) : null;
                float trunk = o.radius * t.lossyScale.x;
                float crown = canopy != null ? canopy.radius * height * CrownScale : trunk;
                Add(new Volume
                {
                    basePosition = p, radius = Mathf.Max(trunk, crown) + Margin, bottom = p.y - 1f, top = p.y + height + Margin,
                    trunk = trunk + 0.3f, crown = crown, crownBottom = p.y + (canopy != null ? canopy.bottom * height : 0f),
                });
            }
        }

        void Add(Volume v)
        {
            int index = volumes.Count;
            volumes.Add(v);
            int x0 = CellOf(v.basePosition.x - v.radius), x1 = CellOf(v.basePosition.x + v.radius);
            int z0 = CellOf(v.basePosition.z - v.radius), z1 = CellOf(v.basePosition.z + v.radius);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    long key = Key(x, z);
                    if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                    list.Add(index);
                }
        }

        static int CellOf(float v) => Mathf.FloorToInt(v / Cell);
        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        /// <summary>True if this point is inside (or within the margin of) a tree, shrub or rock.</summary>
        public bool InObstacle(Vector3 p) => Inside(p, false);

        /// <summary>True if a trunk, rock or crown would hide this point (no margin: a view may pass close by).</summary>
        public bool Blocks(Vector3 p) => Inside(p, true);

        bool Inside(Vector3 p, bool view)
        {
            if (!grid.TryGetValue(Key(CellOf(p.x), CellOf(p.z)), out var list)) return false;
            foreach (int i in list)
            {
                var v = volumes[i];
                if (p.y < v.bottom || p.y > v.top) continue;
                float dx = p.x - v.basePosition.x, dz = p.z - v.basePosition.z, d2 = dx * dx + dz * dz;
                if (!view) { if (d2 < v.radius * v.radius) return true; }
                else if (d2 < v.trunk * v.trunk || (p.y > v.crownBottom && d2 < v.crown * v.crown)) return true;
            }
            return false;
        }

        /// <summary>Lifts the point to at least `clearance` above the terrain.</summary>
        public static Vector3 AboveGround(Vector3 p, float clearance = MinClearance)
        {
            p.y = Mathf.Max(p.y, CourseSurface.GroundAt(p) + clearance);
            return p;
        }

        /// <summary>
        /// True if the line from the camera to the subject is clear of terrain and obstacles (the last `ignoreNear`
        /// meters before the subject don't count: the ball may be in the tree it hit, or in the cup).
        /// </summary>
        public bool CanSee(Vector3 from, Vector3 to, float ignoreNear = 3f)
        {
            float length = Vector3.Distance(from, to);
            for (float d = 1f; d < length - ignoreNear; d += 1.5f)
            {
                var p = Vector3.Lerp(from, to, d / length);
                if (p.y < CourseSurface.GroundAt(p) + 0.15f || Blocks(p)) return false;
            }
            return true;
        }

        /// <summary>How many of these subject points the camera can see.</summary>
        public int Visible(Vector3 from, IReadOnlyList<Vector3> subjects, float ignoreNear)
        {
            int n = 0;
            foreach (var s in subjects)
                if (CanSee(from, s, ignoreNear)) n++;
            return n;
        }

        /// <summary>
        /// The best camera spot near `wanted` for filming the subjects: the wanted spot if it is clear and sees them,
        /// else the first clear spot swinging around the first subject and rising, else the best seen.
        /// clearance: minimum height above the terrain here (cup cameras sit low on the green).
        /// </summary>
        public Vector3 Place(Vector3 wanted, IReadOnlyList<Vector3> subjects, float clearance = MinClearance, float ignoreNear = 3f)
        {
            var pivot = subjects[0];
            var offset = wanted - pivot;
            Vector3 best = AboveGround(wanted, clearance);
            int bestSeen = -1;
            foreach (float lift in Lifts)
                foreach (float turn in Turns)
                {
                    var candidate = pivot + Quaternion.AngleAxis(turn, Vector3.up) * offset + Vector3.up * lift;
                    candidate = AboveGround(candidate, clearance);
                    if (InObstacle(candidate)) continue;
                    int seen = Visible(candidate, subjects, ignoreNear);
                    if (seen == subjects.Count) return candidate;
                    if (seen > bestSeen) { bestSeen = seen; best = candidate; }
                }
            return best;
        }

        static readonly float[] Turns = { 0f, 12f, -12f, 25f, -25f, 40f, -40f, 60f, -60f, 90f, -90f };
        static readonly float[] Lifts = { 0f, 3f, 8f, 16f };

        /// <summary>
        /// Searches a ring of spots around `pivot` (every 15°, at these distances, `height` above the ground) for the best
        /// camera: it must stand clear; it scores for seeing every subject, for its direction from the pivot
        /// (`preference`: 1 = ideal, 0 = poor), for standing on open ground rather than in the woods, for staying low, and
        /// against looking steeply up at the first subject.
        /// </summary>
        public Vector3 Search(Vector3 pivot, IReadOnlyList<Vector3> subjects, float[] distances, float height,
                              System.Func<Vector3, float> preference, float ignoreNear = 3f)
        {
            var best = AboveGround(pivot + Vector3.back * distances[0], height);
            float bestScore = float.MinValue;
            foreach (float lift in new[] { 0f, 4f, 10f })
                for (int a = 0; a < 360; a += 15)
                    foreach (float d in distances)
                    {
                        var dir = Quaternion.AngleAxis(a, Vector3.up) * Vector3.forward;
                        var p = pivot + dir * d;
                        p.y = CourseSurface.GroundAt(p) + height + lift;
                        if (InObstacle(p)) continue;
                        float seen = (float)Visible(p, subjects, ignoreNear) / subjects.Count;
                        string ground = CourseSurface.At(p);
                        bool open = ground is "fairway" or "rough" or "green" or "tee" or "bunker";
                        // Looking steeply up at the subject frames nothing but sky.
                        var toSubject = subjects[0] - p;
                        float lookUp = Mathf.Atan2(toSubject.y, new Vector2(toSubject.x, toSubject.z).magnitude) * Mathf.Rad2Deg;
                        float score = seen * 10f + preference(dir) * 4f + (open ? 1.5f : 0f) - lift * 0.15f - Mathf.Abs(d - distances[0]) * 0.01f
                                      - Mathf.Max(0f, lookUp - 10f) * 0.12f;
                        if (score > bestScore) { bestScore = score; best = p; }
                    }
            return best;
        }

        /// <summary>A preference for directions near this one (1 along it, 0 opposite).</summary>
        public static System.Func<Vector3, float> Toward(Vector3 wanted) =>
            dir => 0.5f + 0.5f * Vector3.Dot(dir, Vector3.ProjectOnPlane(wanted, Vector3.up).normalized);

        static CameraSpots cached;

        /// <summary>The spots for this hole (built on first use, rebuilt for a new hole).</summary>
        public static CameraSpots For(HoleInfo hole, GolfBall ball)
        {
            if (cached == null || cached.hole != hole) cached = new CameraSpots(hole, ball.Settings.obstacles);
            return cached;
        }
    }
}
