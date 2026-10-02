using System;
using System.Collections.Generic;
using UnityEngine;

namespace GolfSim.Course
{
    /// <summary>
    /// Every course theme resolved to concrete layers, models and water, so the game can build holes without
    /// the editor-only asset catalog. Baked with Golf > Catalog > Bake Runtime Themes.
    /// </summary>
    [CreateAssetMenu(menuName = "Golf/Runtime Theme Library", fileName = "RuntimeThemes")]
    public class RuntimeThemeLibrary : ScriptableObject
    {
        [Serializable]
        public class Theme
        {
            public string name;
            public SurfaceLayerSet layers;
            public ScatterSet scatter;
            public WaterProvider water;
        }

        public List<Theme> themes = new List<Theme>();
        public string defaultTheme = "coastal";

        public Theme Find(string themeName) =>
            themes.Find(t => string.Equals(t.name, themeName, StringComparison.OrdinalIgnoreCase))
            ?? themes.Find(t => t.name == defaultTheme)
            ?? (themes.Count > 0 ? themes[0] : null);

        /// <summary>Build options for a package's theme (same seed rule as the editor generator).</summary>
        public HoleBuildOptions OptionsFor(HolePackage pkg)
        {
            var theme = Find(pkg.theme) ?? throw new InvalidOperationException($"{name} has no themes; bake them first");
            return new HoleBuildOptions { layers = theme.layers, scatter = theme.scatter, water = theme.water };
        }
    }
}
