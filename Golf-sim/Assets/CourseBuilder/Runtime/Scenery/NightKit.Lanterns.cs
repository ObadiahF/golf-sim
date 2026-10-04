using UnityEngine;

namespace GolfSim.Course
{
    // The lanterns along the hole, and where the night kit may stand anything (NightKit.Clear).
    public static partial class NightKit
    {
        const float LanternSpacing = 42f, LanternOffset = 17f, Clearance = 3f;
        const float LanternRange = 9f, LanternIntensity = 3.2f;
        static readonly Color LanternGlow = new Color(3.2f, 1.9f, 0.7f);
        static readonly Color LanternLight = new Color(1f, 0.68f, 0.38f);
        static readonly string[] NoStanding = { "water", "bunker", "green", "fairway", "tee" };

        /// <summary>Lanterns on both sides of the hole path, only where they stand in rough or native ground and clear of trees.</summary>
        static void Lanterns(Transform parent, HoleInfo hole, Terrain terrain, HoleAssets assets, float lit)
        {
            var path = hole.holePath;
            if (path.Length < 2) return;
            var post = assets.ColorMaterial("LampPost", PostColor);
            var orb = assets.GlowMaterial("LanternGlow", new Color(1f, 0.85f, 0.6f), LanternGlow);
            var root = new GameObject("Lanterns").transform;
            root.SetParent(parent, false);

            float total = 0f;
            for (int i = 1; i < path.Length; i++) total += Vector3.Distance(path[i - 1], path[i]);
            for (float along = 25f; along < total - 30f; along += LanternSpacing)
            {
                PointAlong(path, along, out var at, out var heading);
                var side = Vector3.Cross(Vector3.up, heading).normalized;
                foreach (float s in new[] { -1f, 1f })
                {
                    var world = hole.transform.TransformPoint(at + side * (LanternOffset * s));
                    if (!Clear(hole, terrain, world)) continue;
                    world.y = Ground(terrain, world);
                    Lantern(root, world, post, orb, lit);
                }
            }
        }

        static void Lantern(Transform parent, Vector3 ground, Material post, Material orb, float lit)
        {
            var lantern = new GameObject("Lantern").transform;
            lantern.SetParent(parent, false);
            lantern.position = ground;
            Primitive(PrimitiveType.Cylinder, lantern, new Vector3(0f, 0.55f, 0f), new Vector3(0.06f, 0.55f, 0.06f), post);
            Primitive(PrimitiveType.Sphere, lantern, new Vector3(0f, 1.2f, 0f), Vector3.one * 0.22f, orb);
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(lantern, false);
            light.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            light.type = LightType.Point;
            light.range = LanternRange;
            Configure(light, LanternLight, LanternIntensity * lit);
        }

        /// <summary>On the terrain, on ground a post may stand on, and not inside a tree, shrub or rock.</summary>
        static bool Clear(HoleInfo hole, Terrain terrain, Vector3 world)
        {
            var data = terrain.terrainData;
            var local = world - terrain.transform.position;
            float u = local.x / data.size.x, v = local.z / data.size.z;
            if (u < 0.02f || u > 0.98f || v < 0.02f || v > 0.98f) return false;
            string surface = DominantSurface(hole, data, u, v);
            if (surface == null || System.Array.IndexOf(NoStanding, surface) >= 0) return false;
            var holeLocal = hole.transform.InverseTransformPoint(world);
            foreach (var o in hole.obstacles)
            {
                float clearance = Clearance + Mathf.Max(o.radius, o.crownRadius * 0.6f);
                if (new Vector2(o.position.x - holeLocal.x, o.position.z - holeLocal.z).sqrMagnitude < clearance * clearance) return false;
            }
            return true;
        }

        static string DominantSurface(HoleInfo hole, TerrainData data, float u, float v)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt(u * (data.alphamapWidth - 1)), 0, data.alphamapWidth - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt(v * (data.alphamapHeight - 1)), 0, data.alphamapHeight - 1);
            var weights = data.GetAlphamaps(x, z, 1, 1);
            int best = -1;
            float bestWeight = 0f;
            for (int i = 0; i < weights.GetLength(2); i++)
                if (weights[0, 0, i] > bestWeight) { bestWeight = weights[0, 0, i]; best = i; }
            return best >= 0 && best < hole.terrainLayerSurfaces.Length ? hole.terrainLayerSurfaces[best] : null;
        }

        /// <summary>The point `distance` meters along the path and the path's flat heading there.</summary>
        static void PointAlong(Vector3[] path, float distance, out Vector3 point, out Vector3 heading)
        {
            for (int i = 1; i < path.Length; i++)
            {
                var a = path[i - 1];
                var b = path[i];
                float length = Vector3.Distance(a, b);
                if (distance <= length || i == path.Length - 1)
                {
                    point = Vector3.Lerp(a, b, length > 0f ? Mathf.Clamp01(distance / length) : 0f);
                    heading = Vector3.ProjectOnPlane(b - a, Vector3.up).normalized;
                    return;
                }
                distance -= length;
            }
            point = path[0];
            heading = Vector3.forward;
        }
    }
}
