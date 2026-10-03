using GolfSim.Course;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// Paints a stylised top-down picture of a hole for the course map: flat colours per surface (blended from the
    /// terrain's own paint, so the edges are as soft as on the course), mowing stripes, a light hill shade, the ponds
    /// and every tree, shrub and rock as a canopy with its shadow. Built from the hole's data, not a camera, so it
    /// reads the same on every theme and works in a built game (nothing to render, no shaders).
    /// </summary>
    public static class HoleMapPainter
    {
        static readonly Color Outside = new Color32(24, 34, 32, 255); // off the hole's terrain
        static readonly Color Water = new Color32(46, 122, 184, 255);
        static readonly Color Canopy = new Color32(36, 84, 46, 255);
        static readonly Color Palm = new Color32(64, 112, 52, 255);
        static readonly Color ShrubColor = new Color32(58, 98, 50, 255);
        static readonly Color Stone = new Color32(128, 132, 128, 255);
        const float StripeLift = 0.07f;   // a mowing-stripe layer is this much lighter than its surface
        const float Relief = 4f;          // hill-shade height exaggeration
        const float ShadeStrength = 1.4f;
        const float SunElevation = 40f;   // degrees; the sun is at the map's top left

        /// <summary>A surface's map colour ("green", "bunker"...); unknown surfaces are rough.</summary>
        public static Color SurfaceColor(string surface) => surface switch
        {
            "green" => new Color32(126, 210, 104, 255),
            "fairway" => new Color32(84, 166, 72, 255),
            "tee" => new Color32(98, 178, 84, 255),
            "rough" => new Color32(52, 112, 52, 255),
            "native" => new Color32(96, 108, 66, 255),
            "scrub" => new Color32(104, 104, 70, 255),
            "woods" => new Color32(40, 86, 46, 255),
            "bunker" => new Color32(232, 214, 160, 255),
            "water" => Water,
            _ => new Color32(52, 112, 52, 255),
        };

        /// <summary>The hole inside `frame` as a width x height texture (null without a terrain). Destroy it when done.</summary>
        public static Texture2D Paint(HoleInfo hole, MapFrame frame, int width, int height)
        {
            var terrain = hole ? hole.GetComponentInChildren<Terrain>() : null;
            if (!terrain || !frame.IsValid) return null;
            var pixels = new Color[width * height];
            var ground = new Ground(terrain, hole, frame);
            for (int row = 0; row < height; row++)
            for (int col = 0; col < width; col++)
            {
                // Texture rows go up from the bottom; map y goes down.
                var world = frame.ToWorld(new Vector2((col + 0.5f) / width, 1f - (row + 0.5f) / height));
                pixels[row * width + col] = ground.ColorAt(world);
            }
            var canvas = new Canvas(pixels, width, height, frame);
            PaintWater(hole, canvas);
            PaintObstacles(hole, canvas);

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = $"Hole map {hole.holeRef}", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixels(pixels);
            texture.Apply(false, true); // no CPU copy kept
            return texture;
        }

        /// <summary>The hole's flat water meshes ("Water n" under the hole), filled.</summary>
        static void PaintWater(HoleInfo hole, Canvas canvas)
        {
            foreach (var filter in hole.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (!filter.name.StartsWith("Water") || !mesh || !mesh.isReadable) continue;
                var vertices = mesh.vertices;
                var points = new Vector2[vertices.Length];
                for (int i = 0; i < vertices.Length; i++) points[i] = canvas.ToPixel(filter.transform.TransformPoint(vertices[i]));
                var triangles = mesh.triangles;
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                    canvas.FillTriangle(points[triangles[t]], points[triangles[t + 1]], points[triangles[t + 2]], Water);
            }
        }

        /// <summary>Shadows first (down and right of each), then the canopies and stones lit from the top left.</summary>
        static void PaintObstacles(HoleInfo hole, Canvas canvas)
        {
            var obstacles = hole.obstacles;
            if (obstacles == null) return;
            var centers = new Vector2[obstacles.Length];
            var radii = new float[obstacles.Length];
            for (int i = 0; i < obstacles.Length; i++)
            {
                var o = obstacles[i];
                centers[i] = canvas.ToPixel(hole.transform.TransformPoint(o.position));
                float meters = o.HasCrown ? o.crownRadius : o.IsTree ? Mathf.Max(o.radius * 4f, 2.5f) : Mathf.Max(o.radius, 0.6f);
                radii[i] = meters / canvas.metersPerPixel;
            }
            var shadow = new Color(0f, 0f, 0f, 0.32f);
            for (int i = 0; i < obstacles.Length; i++)
                canvas.FillDisc(centers[i] + new Vector2(0.45f, -0.45f) * radii[i], radii[i], shadow, 0f);
            for (int i = 0; i < obstacles.Length; i++)
                canvas.FillDisc(centers[i], radii[i], ObstacleColor((ObjectKind)obstacles[i].kind), 0.35f);
        }

        static Color ObstacleColor(ObjectKind kind) => kind switch
        {
            ObjectKind.Palm or ObjectKind.Cactus => Palm,
            ObjectKind.Shrub => ShrubColor,
            ObjectKind.Boulder or ObjectKind.Rock => Stone,
            _ => Canopy,
        };

        /// <summary>The terrain under the map: surface colours per alphamap texel and hill shade per height sample, bilinear.</summary>
        class Ground
        {
            readonly Vector3 origin, size;
            readonly int res, heightRes;
            readonly Color[] colors;
            readonly float[] shade;

            public Ground(Terrain terrain, HoleInfo hole, MapFrame frame)
            {
                var data = terrain.terrainData;
                origin = terrain.transform.position;
                size = data.size;
                res = data.alphamapResolution;
                colors = SurfaceColors(data, hole);
                heightRes = data.heightmapResolution;
                shade = HillShade(data, frame);
            }

            public Color ColorAt(Vector3 world)
            {
                float u = (world.x - origin.x) / size.x, v = (world.z - origin.z) / size.z;
                if (u < 0f || v < 0f || u > 1f || v > 1f) return Outside;
                var c = Bilinear(colors, res, u * res - 0.5f, v * res - 0.5f);
                float s = Bilinear(shade, heightRes, u * (heightRes - 1), v * (heightRes - 1));
                return new Color(c.r * s, c.g * s, c.b * s, 1f);
            }

            /// <summary>Each alphamap texel's colour: the layers' surface colours by weight (stripe layers a shade lighter).</summary>
            static Color[] SurfaceColors(TerrainData data, HoleInfo hole)
            {
                int n = data.alphamapResolution, layers = data.alphamapLayers;
                var surfaces = hole.terrainLayerSurfaces != null && hole.terrainLayerSurfaces.Length == layers
                    ? hole.terrainLayerSurfaces : null;
                var palette = new Color[layers];
                for (int k = 0; k < layers; k++)
                {
                    string surface = surfaces != null ? surfaces[k] : data.terrainLayers[k] ? data.terrainLayers[k].name.ToLowerInvariant() : "";
                    palette[k] = SurfaceColor(Known(surface));
                    // A stripe layer repeats the surface it is painted over (SurfaceLayerSet.Subset): lighten it.
                    if (k > 0 && surfaces != null && surfaces[k] == surfaces[k - 1]) palette[k] += new Color(StripeLift, StripeLift, StripeLift, 0f);
                }
                var alpha = data.GetAlphamaps(0, 0, n, n);
                var colors = new Color[n * n];
                for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    var c = new Color(0f, 0f, 0f, 1f);
                    for (int k = 0; k < layers; k++)
                    {
                        float w = alpha[z, x, k];
                        if (w <= 0f) continue;
                        c.r += palette[k].r * w;
                        c.g += palette[k].g * w;
                        c.b += palette[k].b * w;
                    }
                    colors[z * n + x] = c;
                }
                return colors;
            }

            /// <summary>Holes from before surfaces were recorded: a surface name from the layer's name.</summary>
            static string Known(string name)
            {
                foreach (var s in new[] { "green", "fairway", "tee", "rough", "bunker", "water", "native", "scrub", "woods" })
                    if (name.Contains(s)) return s;
                return name;
            }

            /// <summary>Brightness per height sample: 1 on flat ground, lighter facing the sun (map top left), darker away.</summary>
            static float[] HillShade(TerrainData data, MapFrame frame)
            {
                int n = data.heightmapResolution;
                var heights = data.GetHeights(0, 0, n, n);
                float cellX = data.size.x / (n - 1), cellZ = data.size.z / (n - 1), rise = data.size.y * Relief;
                float elevation = SunElevation * Mathf.Deg2Rad;
                var toSun = ((frame.up - frame.right).normalized * Mathf.Cos(elevation) + Vector3.up * Mathf.Sin(elevation)).normalized;
                var shade = new float[n * n];
                for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int x0 = Mathf.Max(x - 1, 0), x1 = Mathf.Min(x + 1, n - 1), z0 = Mathf.Max(z - 1, 0), z1 = Mathf.Min(z + 1, n - 1);
                    float dx = (heights[z, x1] - heights[z, x0]) * rise / ((x1 - x0) * cellX);
                    float dz = (heights[z1, x] - heights[z0, x]) * rise / ((z1 - z0) * cellZ);
                    var normal = new Vector3(-dx, 1f, -dz).normalized;
                    shade[z * n + x] = Mathf.Clamp(1f + ShadeStrength * (Vector3.Dot(normal, toSun) - toSun.y), 0.72f, 1.22f);
                }
                return shade;
            }

            static Color Bilinear(Color[] grid, int n, float x, float z)
            {
                x = Mathf.Clamp(x, 0f, n - 1.001f);
                z = Mathf.Clamp(z, 0f, n - 1.001f);
                int x0 = (int)x, z0 = (int)z;
                float fx = x - x0, fz = z - z0;
                int i = z0 * n + x0;
                return Color.LerpUnclamped(Color.LerpUnclamped(grid[i], grid[i + 1], fx), Color.LerpUnclamped(grid[i + n], grid[i + n + 1], fx), fz);
            }

            static float Bilinear(float[] grid, int n, float x, float z)
            {
                x = Mathf.Clamp(x, 0f, n - 1.001f);
                z = Mathf.Clamp(z, 0f, n - 1.001f);
                int x0 = (int)x, z0 = (int)z;
                float fx = x - x0, fz = z - z0;
                int i = z0 * n + x0;
                return Mathf.LerpUnclamped(Mathf.LerpUnclamped(grid[i], grid[i + 1], fx), Mathf.LerpUnclamped(grid[i + n], grid[i + n + 1], fx), fz);
            }
        }

        /// <summary>The texture's pixels (row 0 at the bottom) with soft-edged shapes drawn in pixel space.</summary>
        class Canvas
        {
            readonly Color[] pixels;
            readonly int width, height;
            readonly MapFrame frame;
            public readonly float metersPerPixel;

            public Canvas(Color[] pixels, int width, int height, MapFrame frame)
            {
                this.pixels = pixels;
                this.width = width;
                this.height = height;
                this.frame = frame;
                metersPerPixel = frame.size.x / width;
            }

            /// <summary>A world point in texture pixels (x right, y up).</summary>
            public Vector2 ToPixel(Vector3 world)
            {
                var m = frame.ToMap(world);
                return new Vector2(m.x * width, (1f - m.y) * height);
            }

            /// <summary>A disc with a one-pixel soft edge; `highlight` lightens it towards the top left (0 = flat).</summary>
            public void FillDisc(Vector2 center, float radius, Color color, float highlight)
            {
                radius = Mathf.Max(radius, 0.6f);
                int x0 = Mathf.Max(0, (int)(center.x - radius - 1)), x1 = Mathf.Min(width - 1, (int)(center.x + radius + 1));
                int y0 = Mathf.Max(0, (int)(center.y - radius - 1)), y1 = Mathf.Min(height - 1, (int)(center.y + radius + 1));
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - center.x, dy = y + 0.5f - center.y;
                    float coverage = Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                    if (coverage <= 0f) continue;
                    var c = color;
                    if (highlight > 0f)
                    {
                        float lit = Mathf.Clamp01(0.5f + 0.5f * (dy - dx) / (radius * 1.4142f)); // 1 at the top left
                        c = Color.LerpUnclamped(c * 0.8f, c * (1f + highlight), lit);
                    }
                    Blend(x, y, c, coverage * color.a);
                }
            }

            public void FillTriangle(Vector2 a, Vector2 b, Vector2 c, Color color)
            {
                int x0 = Mathf.Max(0, (int)Mathf.Min(a.x, Mathf.Min(b.x, c.x))), x1 = Mathf.Min(width - 1, (int)Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
                int y0 = Mathf.Max(0, (int)Mathf.Min(a.y, Mathf.Min(b.y, c.y))), y1 = Mathf.Min(height - 1, (int)Mathf.Max(a.y, Mathf.Max(b.y, c.y)));
                float area = Cross(a, b, c);
                if (Mathf.Abs(area) < 1e-6f) return;
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = Cross(b, c, p) / area, w1 = Cross(c, a, p) / area, w2 = Cross(a, b, p) / area;
                    if (w0 >= 0f && w1 >= 0f && w2 >= 0f) Blend(x, y, color, color.a);
                }
            }

            static float Cross(Vector2 a, Vector2 b, Vector2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

            void Blend(int x, int y, Color color, float alpha)
            {
                int i = y * width + x;
                var under = pixels[i];
                pixels[i] = new Color(Mathf.LerpUnclamped(under.r, color.r, alpha), Mathf.LerpUnclamped(under.g, color.g, alpha),
                                      Mathf.LerpUnclamped(under.b, color.b, alpha), 1f);
            }
        }
    }
}
