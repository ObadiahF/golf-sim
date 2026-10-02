using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>Golf > Catalog menu: build the catalog from what's assigned today, and add new assets.</summary>
    public static class CatalogMenu
    {
        const string ThemeFolder = "Assets/CourseBuilder/Settings/Themes";

        // Object kind -> (category, extra tags) used when migrating the default ScatterSet's models.
        static readonly Dictionary<ObjectKind, (AssetCategory category, string[] tags)> RuleKinds = new Dictionary<ObjectKind, (AssetCategory, string[])>
        {
            [ObjectKind.Conifer] = (AssetCategory.Tree, new[] { "conifer" }),
            [ObjectKind.Deciduous] = (AssetCategory.Tree, new[] { "deciduous" }),
            [ObjectKind.Palm] = (AssetCategory.Tree, new[] { "palm" }),
            [ObjectKind.Cactus] = (AssetCategory.Tree, new[] { "cactus" }),
            [ObjectKind.Shrub] = (AssetCategory.Shrub, new[] { "shrub" }),
            [ObjectKind.Boulder] = (AssetCategory.Rock, new[] { "rock", "large" }),
            [ObjectKind.Rock] = (AssetCategory.Rock, new[] { "rock", "small" }),
        };

        [MenuItem("Golf/Catalog/Build Catalog From Current Assets")]
        public static void BuildFromCurrentAssets()
        {
            var catalog = AssetCatalog.LoadOrCreate();
            int before = catalog.entries.Count;
            MigrateLayers(catalog, SurfaceLayerSet.LoadOrCreateDefault());
            MigrateScatter(catalog, ScatterSet.LoadOrCreateDefault());
            int themes = EnsureThemes(catalog);
            Save(catalog);
            Selection.activeObject = catalog;
            Debug.Log($"[CourseBuilder] Catalog: {catalog.entries.Count} entries ({catalog.entries.Count - before} new), " +
                      $"{catalog.themes.Count} themes ({themes} new). Default theme: {catalog.defaultTheme?.name}");
        }

        [MenuItem("Golf/Catalog/Add Selected Assets")]
        public static void AddSelected()
        {
            var assets = AutoTagger.Collect(Selection.objects);
            if (assets.Count == 0)
            {
                EditorUtility.DisplayDialog("Course Catalog", "Select prefabs, TerrainLayers, water materials or folders in the Project window first.", "OK");
                return;
            }
            var catalog = AssetCatalog.LoadOrCreate();
            EnsureThemes(catalog);
            var added = new List<string>();
            foreach (var asset in assets)
            {
                var category = AutoTagger.Category(asset);
                if (category == AssetCategory.Prop) continue; // not used by themes yet; add by hand if wanted
                bool isNew = catalog.Get(asset, category) == null;
                catalog.Register(asset, category, AutoTagger.Tags(asset), AutoTagger.Scale(category));
                if (isNew) added.Add($"{asset.name} ({category})");
            }
            Save(catalog);
            Selection.activeObject = catalog;
            Debug.Log($"[CourseBuilder] Added {added.Count} catalog entries:\n" + string.Join("\n", added));
        }

        [MenuItem("Golf/Catalog/Add Selected Assets", true)]
        static bool CanAddSelected() => Selection.objects.Length > 0;

        static void MigrateLayers(AssetCatalog catalog, SurfaceLayerSet layers)
        {
            foreach (var entry in layers.entries.Where(e => e.layer))
                catalog.Register(entry.layer, AssetCategory.GroundLayer, AutoTagger.Tags(entry.layer).Append(entry.surface));
            foreach (var entry in layers.entries.Where(e => e.stripeLayer))
                catalog.Register(entry.stripeLayer, AssetCategory.GroundLayer,
                    AutoTagger.Tags(entry.stripeLayer).Append(entry.surface).Append(DefaultThemes.StripeTag));
            if (layers.waterMaterial)
                catalog.Register(layers.waterMaterial, AssetCategory.WaterMaterial, AutoTagger.Tags(layers.waterMaterial).Append("water"));
        }

        static void MigrateScatter(AssetCatalog catalog, ScatterSet scatter)
        {
            foreach (var rule in scatter.rules)
            {
                foreach (var proto in rule.prototypes.Where(p => p.prefab))
                {
                    var kind = RuleKinds[rule.kind];
                    bool sizeKnown = kind.Item2.Contains("large") || kind.Item2.Contains("small");
                    catalog.Register(proto.prefab, kind.Item1, AutoTagger.Tags(proto.prefab, !sizeKnown).Concat(kind.Item2), proto.scale);
                }
            }
            foreach (var rule in scatter.detailRules)
                foreach (var proto in rule.prototypes.Where(p => p.prefab))
                    catalog.Register(proto.prefab, AssetCategory.GroundCover, AutoTagger.Tags(proto.prefab), proto.scale);
        }

        /// <summary>Creates any missing default theme assets (existing ones are left untouched). Returns how many were made.</summary>
        static int EnsureThemes(AssetCatalog catalog)
        {
            int made = 0;
            foreach (var name in DefaultThemes.Names)
            {
                string path = $"{ThemeFolder}/{name}.asset";
                var theme = AssetDatabase.LoadAssetAtPath<CourseTheme>(path);
                if (!theme)
                {
                    GeneratedAssets.EnsureFolder(ThemeFolder);
                    theme = DefaultThemes.Create(name);
                    AssetDatabase.CreateAsset(theme, path);
                    made++;
                }
                if (!catalog.themes.Contains(theme)) catalog.themes.Add(theme);
                if (!catalog.defaultTheme && name == DefaultThemes.DefaultName) catalog.defaultTheme = theme;
            }
            return made;
        }

        static void Save(AssetCatalog catalog)
        {
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }
    }
}
