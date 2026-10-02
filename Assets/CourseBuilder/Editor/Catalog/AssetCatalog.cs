using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>One usable asset (prefab, TerrainLayer or Material) with its category, tags and size hints.</summary>
    [Serializable]
    public class CatalogEntry
    {
        public string name;
        public AssetCategory category;
        public UnityEngine.Object asset;
        public List<string> tags = new List<string>();
        [Tooltip("Random scale range when placed.")]
        public Vector2 scale = new Vector2(0.8f, 1.2f);
        [Min(0)] public float weight = 1f;

        public GameObject Prefab => asset as GameObject;
        public TerrainLayer Layer => asset as TerrainLayer;
        public Material Material => asset as Material;

        public bool HasTag(string tag) => tags.Contains(tag);

        public void AddTags(IEnumerable<string> extra)
        {
            foreach (var t in extra)
                if (!string.IsNullOrWhiteSpace(t) && !tags.Contains(t.ToLowerInvariant())) tags.Add(t.ToLowerInvariant());
        }
    }

    /// <summary>
    /// Every asset the course builder may use, plus the themes that pick from it.
    /// Adding an asset = adding an entry with tags (Golf > Catalog > Add Selected Assets does it for you);
    /// every theme whose queries match those tags starts using it on the next Generate.
    /// </summary>
    [CreateAssetMenu(menuName = "Golf/Asset Catalog", fileName = "AssetCatalog")]
    public class AssetCatalog : ScriptableObject
    {
        public const string DefaultPath = "Assets/CourseBuilder/Settings/AssetCatalog.asset";

        public List<CatalogEntry> entries = new List<CatalogEntry>();
        public List<CourseTheme> themes = new List<CourseTheme>();
        [Tooltip("Used for real-course holes and any generated hole whose theme isn't found.")]
        public CourseTheme defaultTheme;

        /// <summary>The project catalog, or null if none has been built yet (no side effects).</summary>
        public static AssetCatalog Load() => AssetDatabase.LoadAssetAtPath<AssetCatalog>(DefaultPath);

        public static AssetCatalog LoadOrCreate() => GeneratedAssets.LoadOrCreate(DefaultPath, CreateInstance<AssetCatalog>);

        public CourseTheme FindTheme(string themeName) =>
            themes.FirstOrDefault(t => t && string.Equals(t.name, themeName, StringComparison.OrdinalIgnoreCase)) ?? defaultTheme;

        /// <summary>Matching entries, best (most preferred tags) first.</summary>
        public List<CatalogEntry> Find(AssetQuery query, IEnumerable<string> styleTags = null)
        {
            var style = styleTags?.ToArray() ?? new string[0];
            return entries.Where(query.Matches).OrderByDescending(e => query.Score(e, style)).ToList();
        }

        /// <summary>Existing entry for this asset in this category (one asset may serve as e.g. both Tree and Shrub).</summary>
        public CatalogEntry Get(UnityEngine.Object asset, AssetCategory category) =>
            entries.FirstOrDefault(e => e.asset == asset && e.category == category);

        /// <summary>Adds an entry, or merges tags into the existing one. Returns the entry.</summary>
        public CatalogEntry Register(UnityEngine.Object asset, AssetCategory category, IEnumerable<string> tags, Vector2? scale = null)
        {
            var entry = Get(asset, category);
            if (entry == null)
            {
                entry = new CatalogEntry { name = asset.name, asset = asset, category = category };
                if (scale.HasValue) entry.scale = scale.Value;
                entries.Add(entry);
            }
            entry.AddTags(tags);
            EditorUtility.SetDirty(this);
            return entry;
        }
    }
}
