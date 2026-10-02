using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>
    /// Maps surface names from hole.json to TerrainLayers. Entry order is paint priority:
    /// the first entry is the base that fills everything, later entries paint over earlier ones.
    /// Leave a layer empty to get a flat-colour placeholder; drop real TerrainLayers in when you have them.
    /// </summary>
    [CreateAssetMenu(menuName = "Golf/Surface Layer Set", fileName = "SurfaceLayerSet")]
    public class SurfaceLayerSet : ScriptableObject
    {
        public const string DefaultPath = "Assets/CourseBuilder/Settings/DefaultSurfaceLayers.asset";
        const float PlaceholderTileSize = 4f;

        [Serializable]
        public class Entry
        {
            public string surface;
            public TerrainLayer layer;
            public Color placeholderColor = Color.magenta;
        }

        public List<Entry> entries = DefaultEntries();

        [Tooltip("Material for pond surfaces. Empty = a generated murky-green transparent material.")]
        public Material waterMaterial;

        public static List<Entry> DefaultEntries() => new List<Entry>
        {
            new Entry { surface = "native",  placeholderColor = new Color(0.55f, 0.52f, 0.36f) },
            new Entry { surface = "rough",   placeholderColor = new Color(0.24f, 0.42f, 0.16f) },
            new Entry { surface = "scrub",   placeholderColor = new Color(0.45f, 0.44f, 0.28f) },
            new Entry { surface = "woods",   placeholderColor = new Color(0.13f, 0.25f, 0.10f) },
            new Entry { surface = "fairway", placeholderColor = new Color(0.36f, 0.62f, 0.22f) },
            new Entry { surface = "tee",     placeholderColor = new Color(0.40f, 0.66f, 0.26f) },
            new Entry { surface = "green",   placeholderColor = new Color(0.30f, 0.72f, 0.28f) },
            new Entry { surface = "bunker",  placeholderColor = new Color(0.90f, 0.82f, 0.60f) },
            new Entry { surface = "water",   placeholderColor = new Color(0.16f, 0.32f, 0.48f) },
        };

        public static SurfaceLayerSet LoadOrCreateDefault() =>
            GeneratedAssets.LoadOrCreate(DefaultPath, CreateInstance<SurfaceLayerSet>);

        /// <summary>Adds surfaces introduced since this set was created, each after its default predecessor.</summary>
        public void AddMissingDefaults()
        {
            var defaults = DefaultEntries();
            bool changed = false;
            for (int i = 0; i < defaults.Count; i++)
            {
                if (IndexOf(defaults[i].surface) >= 0) continue;
                int after = i == 0 ? -1 : IndexOf(defaults[i - 1].surface);
                entries.Insert(after + 1, defaults[i]);
                changed = true;
            }
            if (changed) EditorUtility.SetDirty(this);
        }

        public int IndexOf(string surface) => entries.FindIndex(e => e.surface == surface);

        /// <summary>
        /// In-memory copy holding the base entry plus only the given surfaces, in priority order.
        /// HDRP terrain renders at most 8 layers, so each hole gets just the layers it uses.
        /// </summary>
        public SurfaceLayerSet Subset(ICollection<string> surfaces)
        {
            var subset = CreateInstance<SurfaceLayerSet>();
            subset.name = name;
            subset.entries = entries.Where((e, i) => i == 0 || surfaces.Contains(e.surface)).ToList();
            return subset;
        }

        public string[] SurfaceNames() => entries.ConvertAll(e => e.surface).ToArray();

        /// <summary>The configured layer per entry, creating placeholder layers where none is assigned.</summary>
        public TerrainLayer[] ResolveLayers() => entries.ConvertAll(e => e.layer ? e.layer : Placeholder(e)).ToArray();

        static TerrainLayer Placeholder(Entry entry)
        {
            string folder = $"{GeneratedAssets.Root}/Placeholders";
            string colorKey = ColorUtility.ToHtmlStringRGB(entry.placeholderColor);
            string texPath = $"{folder}/{entry.surface}_{colorKey}.png";

            return GeneratedAssets.LoadOrCreate($"{folder}/{entry.surface}_{colorKey}.terrainlayer", () => new TerrainLayer
            {
                diffuseTexture = NoiseTexture(texPath, entry.placeholderColor),
                tileSize = Vector2.one * PlaceholderTileSize,
            });
        }

        /// <summary>Solid colour with slight per-pixel brightness noise so slopes still read in the viewport.</summary>
        static Texture2D NoiseTexture(string assetPath, Color color)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var rng = new System.Random(assetPath.GetHashCode());
            var pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                float k = 0.92f + 0.16f * (float)rng.NextDouble();
                pixels[i] = new Color(color.r * k, color.g * k, color.b * k, 1f);
            }
            tex.SetPixels(pixels);

            GeneratedAssets.EnsureFolder(Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
            File.WriteAllBytes(assetPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath);

            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }
    }
}
