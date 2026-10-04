using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GolfSim.Course
{
    /// <summary>
    /// What makes a hole playable after dark: two floodlight poles beside each green throwing warm pools of light on
    /// it, a lamp post behind the tee, glowing flags, flagsticks and tee markers, a glowing ring around each cup (from
    /// the tee it reads as a bright dot to aim at) and lanterns in the rough along the hole, each with its own small pool
    /// of light (the renderer is Forward+, so lights aren't capped per object). None of it has a collider or an
    /// obstacle entry, so the ball never notices.
    /// </summary>
    public static partial class NightKit
    {
        const float RingInner = 0.075f, RingOuter = 0.11f, RingLift = 0.004f;
        const int RingSegments = 40;
        const float FloodHeight = 11f, FloodIntensity = 520f, FloodRange = 60f, FloodAngle = 62f;
        static readonly float[] FloodSpots = { 24f, 30f, 18f, 36f }; // meters to the side of the pin, nearest first
        static readonly Color FloodColor = new Color(1f, 0.86f, 0.66f);  // sodium-warm against the cool moonlight
        static readonly Color LampGlow = new Color(4f, 3.3f, 2.4f);
        static readonly Color CupGlow = new Color(1.2f, 3.2f, 1.6f);
        static readonly Color PostColor = new Color(0.08f, 0.07f, 0.06f);

        public static void Build(SceneryRig rig, HoleInfo hole, float darkness)
        {
            var terrain = hole.GetComponentInChildren<Terrain>();
            var root = rig.Child("Night Kit");
            var assets = new HoleAssets();
            var post = assets.ColorMaterial("LampPost", PostColor);
            var lamp = assets.GlowMaterial("LampGlow", Color.white, LampGlow);
            float lit = Mathf.Sqrt(darkness); // the lights are nearly as bright at dusk: the green must read as well
            var forward = Vector3.ProjectOnPlane(hole.PinWorld - hole.TeeWorld, Vector3.up).normalized;
            var side = Vector3.Cross(Vector3.up, forward);

            foreach (var flag in hole.GetComponentsInChildren<FlagWave>())
            {
                var pin = flag.transform.parent;
                if (terrain)
                    foreach (float s in new[] { -1f, 1f })
                        if (FloodSpot(hole, terrain, pin.position, side * s, forward, out var foot))
                            Floodlight(root, foot, pin.position, FloodHeight, FloodIntensity * lit, post, lamp);
                Glow(assets, flag.GetComponent<Renderer>(), 1.8f);
                Glow(assets, pin.Find("Flagstick")?.GetComponent<Renderer>(), 1.4f);
                if (terrain) CupRing(rig, assets, root, pin.position, terrain);
            }

            var tee = hole.transform.Find("Tee");
            if (tee)
            {
                var foot = tee.position - forward * 9f + side * 4f;
                if (terrain) foot.y = Ground(terrain, foot);
                Floodlight(root, foot, tee.position, 6f, 60f * lit, post, lamp);
                foreach (var marker in tee.GetComponentsInChildren<Renderer>()) Glow(assets, marker, 1.1f);
            }

            if (terrain) Lanterns(root, hole, terrain, assets, lit);
        }

        /// <summary>Where a floodlight pole stands beside the green: the nearest spot off to that side a pole may stand on.</summary>
        static bool FloodSpot(HoleInfo hole, Terrain terrain, Vector3 pin, Vector3 side, Vector3 forward, out Vector3 foot)
        {
            foreach (float d in FloodSpots)
            {
                foot = pin + side * d + forward * 6f;
                if (!Clear(hole, terrain, foot)) continue;
                foot.y = Ground(terrain, foot);
                return true;
            }
            foot = default;
            return false;
        }

        /// <summary>A pole with a lamp head at `height`, its spot aimed at `target`.</summary>
        static void Floodlight(Transform parent, Vector3 foot, Vector3 target, float height, float intensity, Material post, Material lamp)
        {
            var pole = new GameObject("Floodlight").transform;
            pole.SetParent(parent, false);
            pole.position = foot;
            Primitive(PrimitiveType.Cylinder, pole, new Vector3(0f, height / 2f, 0f), new Vector3(0.14f, height / 2f, 0.14f), post);
            var head = foot + Vector3.up * height;
            var aim = Quaternion.LookRotation(target - head);
            var lampHead = Primitive(PrimitiveType.Cube, pole, Vector3.up * height, new Vector3(0.9f, 0.25f, 0.5f), lamp);
            lampHead.rotation = aim;

            var light = new GameObject("Spot").AddComponent<Light>();
            light.transform.SetParent(pole, false);
            light.transform.SetPositionAndRotation(head, aim);
            light.type = LightType.Spot;
            light.spotAngle = FloodAngle;
            light.innerSpotAngle = FloodAngle * 0.5f;
            light.range = FloodRange;
            Configure(light, FloodColor, intensity);
        }

        /// <summary>A small warm point light (lanterns), or any light of the kit: no shadows, always per pixel.</summary>
        static void Configure(Light light, Color color, float intensity)
        {
            light.color = color;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
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
                    p.y = Ground(terrain, p) + RingLift;
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

        static Transform Primitive(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            SceneryRig.Discard(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        static float Ground(Terrain terrain, Vector3 world) => terrain.SampleHeight(world) + terrain.transform.position.y;
    }
}
