using System;
using System.Collections.Generic;
using System.Linq;
using GolfSim.Course;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// Builds a practice facility's ground with the hole builder: a hole package made in code (heights from a
    /// function, surface areas as polygons, trees) dressed by a course theme like any downloaded hole, plus extra
    /// pins and yardage signs. Everything is made from RuntimeMaterials templates, so it works in a built game.
    /// </summary>
    public static class FacilityGround
    {
        /// <summary>The heights, surfaces and objects of a facility, in local metres (x = east, z = north).</summary>
        public class Plan
        {
            public string name, theme = "parkland";
            public float size;
            public int resolution;     // heightmap samples per side, 2^n + 1
            public float maxHeight;    // metres; heights run 0..maxHeight
            public Func<float, float, float> height;
            public Vector2 tee, pin;
            public readonly List<HoleArea> areas = new List<HoleArea>();
            public readonly List<PlacedObject> trees = new List<PlacedObject>();

            /// <summary>A surface inside `ring`, minus any `holes` (the surfaces under it show there).</summary>
            public void Area(string surface, IReadOnlyList<Vector2> ring, params IReadOnlyList<Vector2>[] holes) =>
                areas.Add(new HoleArea { surface = surface, sourceId = name, rings = holes.Prepend(ring).Select(Ring).ToArray() });
        }

        static readonly HoleAssets Assets = new HoleAssets(); // shared materials (flags, signs) are cached per colour

        /// <summary>Replaces the scene's hole with the plan's, built in memory.</summary>
        public static HoleInfo Build(Plan plan, RuntimeThemeLibrary themes)
        {
            int n = plan.resolution;
            var heights = new float[n, n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                    heights[z, x] = Mathf.Clamp01(plan.height(x * plan.size / (n - 1), z * plan.size / (n - 1)) / plan.maxHeight);
            var pkg = new HolePackage
            {
                version = 2, id = plan.name, course = plan.name, holeRef = "1", par = 3, theme = plan.theme,
                sizeMeters = plan.size,
                heightmap = new HeightmapInfo { resolution = n, minElevation = 0f, maxElevation = plan.maxHeight },
                holePath = Ring(new[] { plan.tee, plan.pin }),
                tee = plan.tee, pin = plan.pin,
                areas = plan.areas.ToArray(),
                objects = new ObjectsInfo { count = plan.trees.Count },
                heights = heights,
                placed = plan.trees.ToArray(),
            };
            var hole = RuntimeHoleBuilder.Build(pkg, themes);
            hole.name = plan.name;
            return hole;
        }

        /// <summary>A closed ring of `count` points around `centre`; radius(angle) in metres (angle 0 = +x).</summary>
        public static Vector2[] Ring(Vector2 centre, int count, Func<float, Vector2> radius)
        {
            var points = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                var r = radius(a);
                points[i] = centre + new Vector2(Mathf.Cos(a) * r.x, Mathf.Sin(a) * r.y);
            }
            return points;
        }

        public static Vector2[] Ellipse(Vector2 centre, float rx, float rz, int count = 40) => Ring(centre, count, _ => new Vector2(rx, rz));

        static PointList Ring(IReadOnlyList<Vector2> points)
        {
            var flat = new float[points.Count * 2];
            for (int i = 0; i < points.Count; i++)
            {
                flat[2 * i] = points[i].x;
                flat[2 * i + 1] = points[i].y;
            }
            return new PointList { points = flat };
        }

        /// <summary>A pin (flagstick, flag and cup) at a local spot on the hole's terrain.</summary>
        public static Transform Pin(HoleInfo hole, string name, Vector2 at, Color flag)
        {
            var terrain = hole.GetComponentInChildren<Terrain>();
            return HoleMarkers.CreatePin(hole.transform, name, OnGround(hole, at), terrain, Assets, flag);
        }

        /// <summary>The flagstick and flag of a pin shown or hidden (its cup stays).</summary>
        public static void ShowFlag(Transform pin, bool shown)
        {
            foreach (Transform child in pin)
                if (child.name is "Flagstick" or "Flag") child.gameObject.SetActive(shown);
        }

        /// <summary>Hides the tee markers (a facility's ball is placed by the facility, not teed up there).</summary>
        public static void HideTee(HoleInfo hole)
        {
            var tee = hole.transform.Find("Tee");
            if (tee) tee.gameObject.SetActive(false);
        }

        /// <summary>A local (x, z) spot on the hole's terrain, as a local position.</summary>
        public static Vector3 OnGround(HoleInfo hole, Vector2 at)
        {
            var terrain = hole.GetComponentInChildren<Terrain>();
            var world = hole.transform.TransformPoint(new Vector3(at.x, 0f, at.y));
            return new Vector3(at.x, terrain.SampleHeight(world) + terrain.transform.position.y - hole.transform.position.y, at.y);
        }

        /// <summary>
        /// A yardage board on two posts at a local spot, facing `toward`, with the number in block digits (no font:
        /// a built game has no text shader for 3D text). height: of the board, metres.
        /// </summary>
        public static Transform Sign(HoleInfo hole, Vector2 at, Vector2 toward, string number, float height, Color board)
        {
            var sign = new GameObject($"Sign {number}").transform;
            sign.SetParent(hole.transform, false);
            sign.localPosition = OnGround(hole, at);
            var facing = toward - at;
            sign.localRotation = Quaternion.LookRotation(new Vector3(-facing.x, 0f, -facing.y)); // the board's front (-z) faces `toward`

            float digit = height * 0.62f, width = number.Length * digit * 0.72f + height * 0.5f, lift = height * 0.55f;
            var post = Assets.ColorMaterial("SignPost", new Color(0.30f, 0.24f, 0.18f));
            float postLength = lift + height * 0.5f; // up into the middle of the board
            Block(sign, "Post L", new Vector3(-width * 0.38f, postLength / 2f, 0.1f), new Vector3(0.12f, postLength, 0.12f), post);
            Block(sign, "Post R", new Vector3(width * 0.38f, postLength / 2f, 0.1f), new Vector3(0.12f, postLength, 0.12f), post);
            var face = new Vector3(0f, lift + height / 2f, 0f);
            Block(sign, "Board", face, new Vector3(width, height, 0.08f), Assets.ColorMaterial($"Sign_{ColorUtility.ToHtmlStringRGB(board)}", board));
            Digits(sign, number, face + new Vector3(0f, 0f, -0.05f), digit, Assets.ColorMaterial("SignDigits", Color.white));
            return sign;
        }

        // Seven segments per digit: a top, b top right, c bottom right, d bottom, e bottom left, f top left, g middle.
        static readonly string[] Segments = { "abcdef", "bc", "abged", "abgcd", "fgbc", "afgcd", "afgedc", "abc", "abcdefg", "abcdfg" };

        /// <summary>Block digits (seven-segment) centred on `centre`, `size` tall, in the sign's plane.</summary>
        static void Digits(Transform parent, string number, Vector3 centre, float size, Material material)
        {
            float w = size * 0.55f, t = size * 0.14f, pitch = size * 0.72f;
            float x0 = centre.x - (number.Length - 1) * pitch / 2f;
            for (int i = 0; i < number.Length; i++)
            {
                if (!char.IsDigit(number[i])) continue;
                var c = new Vector3(x0 + i * pitch, centre.y, centre.z);
                foreach (char s in Segments[number[i] - '0'])
                {
                    bool across = s is 'a' or 'd' or 'g';
                    float x = s switch { 'b' or 'c' => w / 2f, 'e' or 'f' => -w / 2f, _ => 0f };
                    float y = s switch { 'a' => size / 2f, 'd' => -size / 2f, 'b' or 'f' => size / 4f, 'c' or 'e' => -size / 4f, _ => 0f };
                    var scale = across ? new Vector3(w + t, t, 0.03f) : new Vector3(t, size / 2f + t, 0.03f);
                    Block(parent, $"Digit {i} {s}", c + new Vector3(x, y, 0f), scale, material);
                }
            }
        }

        static void Block(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>()); // scenery: the ball flies by its own physics
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        /// <summary>A row of trees from `from` to `to`, about `spacing` apart, jittered (seeded, so every build matches).</summary>
        public static void TreeRow(Plan plan, Vector2 from, Vector2 to, float spacing, System.Random rng)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(from, to) / spacing));
            for (int i = 0; i <= count; i++)
            {
                var p = Vector2.Lerp(from, to, i / (float)count) + new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * spacing * 0.6f;
                bool conifer = rng.NextDouble() < 0.4;
                plan.trees.Add(new PlacedObject
                {
                    kind = conifer ? ObjectKind.Conifer : ObjectKind.Deciduous,
                    position = p,
                    height = 11f + (float)rng.NextDouble() * 7f,
                    radius = 0.35f,
                    rotation = (float)rng.NextDouble() * 360f,
                    variant = (float)rng.NextDouble(),
                });
            }
        }
    }
}
