using System.Linq;
using GolfSim.Course;
using UnityEditor;
using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>
    /// Golf > Catalog > Bake Runtime Themes: resolves every catalog theme into concrete layers, models and water
    /// and saves them in one RuntimeThemeLibrary asset, so the game can build downloaded holes at runtime.
    /// Re-bake after changing the catalog or themes.
    /// </summary>
    public static class RuntimeThemeBaker
    {
        public const string LibraryPath = "Assets/CourseBuilder/Settings/RuntimeThemes.asset";

        [MenuItem("Golf/Catalog/Bake Runtime Themes")]
        public static RuntimeThemeLibrary Bake()
        {
            var catalog = AssetCatalog.Load();
            if (!catalog) throw new System.InvalidOperationException("No asset catalog yet: run Golf > Catalog > Build Catalog From Current Assets");

            var library = GeneratedAssets.SaveFresh(LibraryPath, ScriptableObject.CreateInstance<RuntimeThemeLibrary>());
            library.defaultTheme = catalog.defaultTheme ? catalog.defaultTheme.name : DefaultThemes.DefaultName;
            foreach (var theme in catalog.themes.Where(t => t))
            {
                var resolved = ThemeResolver.Resolve(theme, catalog);
                resolved.layers.AddMissingDefaults();
                library.themes.Add(new RuntimeThemeLibrary.Theme
                {
                    name = theme.name,
                    layers = Embed(library, resolved.layers, $"{theme.name} layers"),
                    scatter = Embed(library, resolved.scatter, $"{theme.name} scatter"),
                    // A theme's own water provider is a shared asset; a resolved fallback one is embedded.
                    water = AssetDatabase.Contains(resolved.water) ? resolved.water : Embed(library, resolved.water, $"{theme.name} water"),
                });
            }
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Debug.Log($"[CourseBuilder] Baked {library.themes.Count} runtime themes into {LibraryPath}");
            return library;
        }

        static T Embed<T>(Object library, T obj, string name) where T : Object
        {
            obj.name = name;
            AssetDatabase.AddObjectToAsset(obj, library);
            return obj;
        }
    }
}
