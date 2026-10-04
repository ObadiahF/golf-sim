using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GolfSim.Course
{
    /// <summary>
    /// What makes a hole playable after dark: a floodlight over each green and one over the tee, glowing flags,
    /// flagsticks and tee markers, a glowing ring around each cup (it reads from the tee as a bright dot to aim at)
    /// and lanterns along the hole in the rough. Lights are few on purpose: the terrain is one renderer and URP
    /// lights it with at most four extra lights (floodlight, tee light, the glowing ball). The lanterns only glow.
    /// None of it has a collider or an obstacle entry, so the ball never notices.
    /// </summary>
    public static class NightKit
    {
        const float LanternSpacing = 42f, LanternOffset = 17f, LanternClearance = 3f;
        const float RingInner = 0.075f, RingOuter = 0.11f, RingLift = 0.004f;
        const int RingSegments = 40;
        static readonly Color FloodColor = new Color(1f, 0.93f, 0.8f);
        static readonly Color CupGlow = new Color(1.2f, 3.2f, 1.6f);
        static readonly Color LanternGlow = new Color(3.2f, 1.9f, 0.7f);
        static readonly string[] NoLanterns = { "water", "bunker", "green", "fairway", "tee" };

        public static void Build(SceneryRig rig, HoleInfo hole, float darkness)
        {
            var terrain = hole.GetComponentInChildren<Terrain>();
            var root = rig.Child("Night Kit");
            var assets = new HoleAssets();
            float lit = Mathf.Sqrt(darkness); // the lights are nearly as bright at dusk: the green must read as well

            foreach (var flag in hole.GetComponentsInChildren<FlagWave>())
            {
                var pin = flag.transform.parent;
                Light(root, "Green Floodlight", pin.position + Vector3.up * 9f, 36f, 70f * lit);
                Glow(assets, flag.GetComponent<Renderer>(), 1.8f);
                Glow(assets, pin.Find("Flagstick")?.GetComponent<Renderer>(), 1.4f);
                if (terrain) CupRing(rig, assets, root, pin.position, terrain);
            }

            var tee = hole.transform.Find("Tee");
            if (tee)
            {
                Light(root, "Tee Light", tee.position + Vector3.up * 5f, 14f, 22f * lit);
                foreach (var marker in tee.GetComponentsInChildren<Renderer>()) Glow(assets, marker, 1.6f);
            }

            if (terrain) Lanterns(root, hole, terrain, assets);
        }

        static void Light(Transform parent, string name, Vector3 position, float range, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = FloodColor;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel; // a vertex light on the terrain's big patches would barely show
        }

        /// <summary>Swaps a renderer to a glowing copy of its own colour (HDR emission = colour × strength).</summary>
        static void Glow(HoleAssets assets, Renderer renderer, float strength)
        {
            if (!renderer) return;
            var day = renderer.sharedMaterial;
            var color = day ? day.color : Color.white;
            bool twoSided = HoleAssets.IsDoubleSided(day);
            renderer.sharedMaterial = assets.GlowMaterial($"Glow_{ColorUtility.ToHtmlStringRGB(color)}_{strength:0.0}{(twoSided ? "_2s" : "")}",
                                                          color, color * strength, twoSided);
        }

        /// <summary>A thin glowing ring on the green around the cup, following the slope.</summary>
        static void CupRing(SceneryRig rig, HoleAssets assets, Transform parent, Vector3 cup, Terrain terrain)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i <= RingSegments; i++)
            {
                float a = i * Mathf.PI * 2f / RingSegments;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                foreach (float r in new[] { RingInner, RingOuter })
                {
                    var p = cup + dir * r;
                    p.y = terrain.SampleHeight(p) + terrain.transform.position.y + RingLift;
                    vertices.Add(p - cup);
                }
                if (i == RingSegments) break;
                int v = i * 2;
                triangles.AddRange(new[] { v, v + 2, v + 1, v + 1, v + 2, v + 3 });
            }
            var mesh = rig.Own(new Mesh { name = "Cup Ring" });
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Cup Ring");
            go.transform.SetParent(parent, false);
            go.transform.position = cup;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = assets.GlowMaterial("CupRingGlow", Color.white, CupGlow);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>Lanterns on both sides of the hole path, only where they stand in rough or native ground and clear of trees.</summary>
        static void Lanterns(Transform parent, HoleInfo hole, Terrain terrain, HoleAssets assets)
        {
            var path = hole.holePath;
            if (path.Length < 2) return;
            var post = assets.ColorMaterial("LanternPost", new Color(0.08f, 0.07f, 0.06f));
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
                    world.y = terrain.SampleHeight(world) + terrain.transform.position.y;
                    Lantern(root, world, post, orb);
                }
            }
        }

        static void Lantern(Transform parent, Vector3 ground, Material post, Material orb)
        {
            var lantern = new GameObject("Lantern").transform;
            lantern.SetParent(parent, false);
            lantern.position = ground;
            Primitive(PrimitiveType.Cylinder, lantern, new Vector3(0f, 0.55f, 0f), new Vector3(0.06f, 0.55f, 0.06f), post);
            Primitive(PrimitiveType.Sphere, lantern, new Vector3(0f, 1.2f, 0f), Vector3.one * 0.22f, orb);
        }

        static void Primitive(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            SceneryRig.Discard(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>On the terrain, on ground a lantern may stand on, and not inside a tree, shrub or rock.</summary>
        static bool Clear(HoleInfo hole, Terrain terrain, Vector3 world)
        {
            var data = terrain.terrainData;
            var local = world - terrain.transform.position;
            float u = local.x / data.size.x, v = local.z / data.size.z;
            if (u < 0.02f || u > 0.98f || v < 0.02f || v > 0.98f) return false;
            string surface = DominantSurface(hole, data, u, v);
            if (surface == null || System.Array.IndexOf(NoLanterns, surface) >= 0) return false;
            var holeLocal = hole.transform.InverseTransformPoint(world);
            foreach (var o in hole.obstacles)
            {
                float clearance = LanternClearance + Mathf.Max(o.radius, o.crownRadius * 0.6f);
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
