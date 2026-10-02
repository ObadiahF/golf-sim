using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>
    /// Guesses category and tags for an asset from its type, name, folder and size, so a new
    /// Asset Store pack is usable after one click. Guesses are a starting point: edit the catalog
    /// entry's tags to refine (e.g. add "desert" to a cactus pack so desert themes prefer it).
    /// </summary>
    public static class AutoTagger
    {
        // keyword in asset name or path -> tags
        static readonly (string word, string[] tags)[] Keywords =
        {
            ("pine", new[] { "conifer" }), ("conifer", new[] { "conifer" }), ("cypress", new[] { "conifer" }),
            ("fir", new[] { "conifer" }), ("spruce", new[] { "conifer" }), ("cedar", new[] { "conifer" }),
            ("oak", new[] { "deciduous" }), ("birch", new[] { "deciduous" }), ("maple", new[] { "deciduous" }),
            ("beech", new[] { "deciduous" }), ("willow", new[] { "deciduous", "lakes" }), ("elm", new[] { "deciduous" }),
            ("palm", new[] { "palm", "tropical" }), ("cactus", new[] { "cactus", "desert" }), ("saguaro", new[] { "cactus", "desert" }),
            ("dry", new[] { "dry" }), ("dead", new[] { "dry" }), ("desert", new[] { "desert", "dry" }),
            ("grass", new[] { "grass" }), ("fern", new[] { "fern" }), ("heather", new[] { "heather", "links" }),
            ("flower", new[] { "flower", "lush" }), ("clover", new[] { "lush" }), ("reed", new[] { "reed", "lakes" }),
            ("bush", new[] { "bush" }), ("shrub", new[] { "shrub" }), ("moss", new[] { "mossy" }), ("overgrown", new[] { "mossy" }),
            ("rock", new[] { "rock" }), ("boulder", new[] { "rock", "large" }), ("stone", new[] { "rock" }), ("cliff", new[] { "rock", "large" }),
            ("sand", new[] { "sand" }), ("snow", new[] { "snow", "mountain" }), ("alpine", new[] { "mountain" }),
            ("water", new[] { "water" }), ("ocean", new[] { "water", "coastal" }), ("lake", new[] { "water", "lakes" }),
        };

        static readonly string[] TreeWords = { "tree", "pine", "oak", "palm", "conifer", "cypress", "birch", "maple", "fir", "spruce", "willow", "cedar", "beech", "elm" };
        static readonly string[] RockWords = { "rock", "boulder", "stone", "cliff", "pebble" };
        static readonly string[] CoverWords = { "grass", "fern", "flower", "heather", "weed", "clover", "reed", "plant" };
        static readonly string[] ShrubWords = { "bush", "shrub", "cactus", "hedge" };
        const float LargeRockMeters = 1.0f;
        const float TreeMinHeight = 3.0f;

        /// <param name="sizeTag">Add "large"/"small" for rocks from their mesh size.</param>
        public static string[] Tags(Object asset, bool sizeTag = true)
        {
            string text = (asset.name + " " + AssetDatabase.GetAssetPath(asset)).ToLowerInvariant();
            var tags = new HashSet<string>(Keywords.Where(k => text.Contains(k.word)).SelectMany(k => k.tags));
            if (sizeTag && asset is GameObject go && Category(asset) == AssetCategory.Rock)
                tags.Add(Size(go).magnitude > LargeRockMeters * 1.7f ? "large" : "small");
            return tags.ToArray();
        }

        public static AssetCategory Category(Object asset)
        {
            if (asset is TerrainLayer) return AssetCategory.GroundLayer;
            string name = asset.name.ToLowerInvariant();
            if (asset is Material) return name.Contains("water") ? AssetCategory.WaterMaterial : AssetCategory.Prop;
            if (!(asset is GameObject go)) return AssetCategory.Prop;

            string path = AssetDatabase.GetAssetPath(asset).ToLowerInvariant();
            float height = Size(go).y;
            bool speedTree = path.EndsWith(".st") || path.EndsWith(".spm");
            if (RockWords.Any(name.Contains)) return AssetCategory.Rock;
            if (CoverWords.Any(name.Contains)) return AssetCategory.GroundCover;
            if (ShrubWords.Any(name.Contains)) return height > 1.5f ? AssetCategory.Shrub : AssetCategory.GroundCover;
            if (speedTree || TreeWords.Any(name.Contains) || height > TreeMinHeight) return AssetCategory.Tree;
            return AssetCategory.Prop;
        }

        /// <summary>Default random scale range by category.</summary>
        public static Vector2 Scale(AssetCategory category) => category switch
        {
            AssetCategory.Tree => new Vector2(0.8f, 1.2f),
            AssetCategory.Rock => new Vector2(0.7f, 1.6f),
            AssetCategory.GroundCover => new Vector2(0.7f, 1.2f),
            _ => new Vector2(0.8f, 1.2f),
        };

        static Vector3 Size(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return Vector3.zero;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds.size;
        }

        /// <summary>Usable assets inside the selection (folders are searched recursively).</summary>
        public static List<Object> Collect(IEnumerable<Object> selection)
        {
            var found = new List<Object>();
            foreach (var obj in selection)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (AssetDatabase.IsValidFolder(path))
                {
                    found.AddRange(AssetDatabase.FindAssets("t:GameObject t:TerrainLayer", new[] { path })
                        .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadMainAssetAtPath));
                    found.AddRange(AssetDatabase.FindAssets("water t:Material", new[] { path })
                        .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadMainAssetAtPath));
                }
                else if (obj is GameObject || obj is TerrainLayer || obj is Material) found.Add(obj);
            }
            return found.Where(o => o).Distinct().ToList();
        }
    }
}
