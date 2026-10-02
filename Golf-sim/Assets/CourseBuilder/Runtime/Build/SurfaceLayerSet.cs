using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GolfSim.Course
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
        const string StripeSuffix = "#stripe";

        [Serializable]
        public class Entry
        {
            public string surface;
            public TerrainLayer layer;
            public Color placeholderColor = Color.magenta;

            [Header("Mowing stripes (optional)")]
            [Tooltip("Variant of the layer (usually slightly darker) painted in alternating bands.")]
            public TerrainLayer stripeLayer;
            [Min(0), Tooltip("Band width in meters. 0 = no stripes.")]
            public float stripeWidth;
            [Tooltip("Band direction in degrees from the line of play: 0 = bands run tee to green, 90 = across.")]
            public float stripeAngle;

            public bool HasStripes => stripeLayer && stripeWidth > 0f;
            public bool IsStripe => surface.EndsWith(StripeSuffix);
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
            new Entry { surface = "fairway", placeholderColor = new Color(0.36f, 0.62f, 0.22f), stripeWidth = 4f, stripeAngle = 90f },
            new Entry { surface = "tee",     placeholderColor = new Color(0.40f, 0.66f, 0.26f) },
            new Entry { surface = "green",   placeholderColor = new Color(0.30f, 0.72f, 0.28f), stripeWidth = 1f },
            new Entry { surface = "bunker",  placeholderColor = new Color(0.90f, 0.82f, 0.60f) },
            new Entry { surface = "water",   placeholderColor = new Color(0.16f, 0.32f, 0.48f) },
        };

        /// <summary>Adds surfaces introduced since this set was created, each after its default predecessor. Returns true if any were added.</summary>
        public bool AddMissingDefaults()
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
            return changed;
        }

        public int IndexOf(string surface) => entries.FindIndex(e => e.surface == surface);

        /// <summary>
        /// In-memory copy holding the base entry plus only the given surfaces, in priority order.
        /// Each terrain layer costs rendering time, so a hole gets just the layers it uses.
        /// Striped surfaces expand into the surface plus a stripe entry painted right above it.
        /// </summary>
        public SurfaceLayerSet Subset(ICollection<string> surfaces)
        {
            var subset = CreateInstance<SurfaceLayerSet>();
            subset.name = name;
            subset.waterMaterial = waterMaterial;
            subset.entries = new List<Entry>();
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (i != 0 && !surfaces.Contains(e.surface)) continue;
                subset.entries.Add(e);
                if (e.HasStripes)
                    subset.entries.Add(new Entry
                    {
                        surface = e.surface + StripeSuffix,
                        layer = e.stripeLayer,
                        placeholderColor = e.placeholderColor * 0.85f,
                        stripeWidth = e.stripeWidth,
                        stripeAngle = e.stripeAngle,
                    });
            }
            return subset;
        }

        /// <summary>Surface name per entry; stripe entries report their parent surface.</summary>
        public string[] SurfaceNames() => entries.ConvertAll(e => BaseSurface(e.surface)).ToArray();

        public static string BaseSurface(string surface) =>
            surface.EndsWith(StripeSuffix) ? surface.Substring(0, surface.Length - StripeSuffix.Length) : surface;

        /// <summary>The configured layer per entry, creating placeholder layers where none is assigned.</summary>
        public TerrainLayer[] ResolveLayers(HoleAssets assets) =>
            entries.ConvertAll(e => e.layer ? e.layer : assets.PlaceholderLayer(e.surface, e.placeholderColor, PlaceholderTileSize)).ToArray();
    }
}
