using GolfSim.Course;
using System.Collections.Generic;
using GolfSim.Course;
using System.Linq;
using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>
    /// Starting themes, matching the generator presets (Tools/course_gen/style.py) plus "coastal",
    /// which reproduces the original real-course look. They differ in ground cover and tag preferences;
    /// how densely trees and rocks are planted lives in Tools/course_prep/vegetation_themes.py.
    /// </summary>
    public static class DefaultThemes
    {
        public const string DefaultName = "coastal";

        class Spec
        {
            public string name;
            public string[] styleTags;
            public float roughGrass = 0.8f, nativeGrass = 0.6f, brush = 0.25f, woodsFloor = 0.5f;
            public string[] brushPrefer = new string[0];
            public Dictionary<string, string[]> layerPrefer = new Dictionary<string, string[]>();
        }

        static readonly Spec[] Specs =
        {
            new Spec { name = "coastal", styleTags = new[] { "coastal" } },
            new Spec { name = "parkland", styleTags = new[] { "parkland", "lush" }, nativeGrass = 0.4f, brush = 0.15f },
            new Spec { name = "forest", styleTags = new[] { "forest" }, woodsFloor = 0.7f },
            new Spec { name = "lakes", styleTags = new[] { "lakes", "lush" }, brush = 0.15f },
            new Spec { name = "links", styleTags = new[] { "links", "coastal" }, brushPrefer = new[] { "heather" },
                       nativeGrass = 0.85f, brush = 0.35f },
            new Spec { name = "desert", styleTags = new[] { "desert", "dry" }, roughGrass = 0.5f, nativeGrass = 0.3f, brush = 0.3f,
                       layerPrefer = { ["native"] = new[] { "sand", "desert" } } },
            new Spec { name = "mountain", styleTags = new[] { "mountain" },
                       layerPrefer = { ["native"] = new[] { "rock", "mountain" } } },
            // Scenic course types: their own ground layers (Tools/unity_scripts/SetupSceneryThemes.cs makes them from
            // ambientCG textures and tags them with the theme's word) plus sky and air in ThemeScenery.cs.
            new Spec { name = "autumn", styleTags = new[] { "autumn" }, brushPrefer = new[] { "autumn" }, woodsFloor = 0.3f,
                       layerPrefer = { ["native"] = new[] { "autumn" }, ["woods"] = new[] { "autumn" }, ["rough"] = new[] { "autumn" } } },
            new Spec { name = "tropical", styleTags = new[] { "tropical" }, nativeGrass = 0.05f, brush = 0.35f, woodsFloor = 0.7f,
                       layerPrefer = { ["native"] = new[] { "tropical" }, ["scrub"] = new[] { "tropical" }, ["bunker"] = new[] { "tropical" } } },
            new Spec { name = "canyon", styleTags = new[] { "canyon", "dry" }, roughGrass = 0.5f, nativeGrass = 0.2f, brush = 0.2f, woodsFloor = 0.2f,
                       layerPrefer = { ["native"] = new[] { "canyon" }, ["scrub"] = new[] { "canyon" }, ["woods"] = new[] { "canyon" },
                                       ["bunker"] = new[] { "canyon" } } },
            new Spec { name = "winter", styleTags = new[] { "winter" }, roughGrass = 0.25f, nativeGrass = 0.08f, brush = 0.05f, woodsFloor = 0.05f,
                       layerPrefer = { ["native"] = new[] { "snow" }, ["scrub"] = new[] { "snow" }, ["woods"] = new[] { "snow" },
                                       ["rough"] = new[] { "winter" } } },
            new Spec { name = "heathland", styleTags = new[] { "heathland" }, brushPrefer = new[] { "heather" }, brush = 0.5f, nativeGrass = 0.4f,
                       layerPrefer = { ["native"] = new[] { "heather" }, ["scrub"] = new[] { "heather" } } },
        };

        public const string StripeTag = "stripe";

        /// <summary>Which catalog assets represent each object kind (the same in every theme; styleTags rank them).</summary>
        public static readonly Dictionary<ObjectKind, AssetQuery> KindQueries = new Dictionary<ObjectKind, AssetQuery>
        {
            [ObjectKind.Conifer] = Query(AssetCategory.Tree, all: new[] { "conifer" }),
            [ObjectKind.Deciduous] = Query(AssetCategory.Tree, all: new[] { "deciduous" }),
            [ObjectKind.Palm] = Query(AssetCategory.Tree, all: new[] { "palm" }),
            [ObjectKind.Cactus] = Query(AssetCategory.Tree, any: new[] { "cactus" }),
            [ObjectKind.Shrub] = Query(AssetCategory.Shrub),
            [ObjectKind.Boulder] = Query(AssetCategory.Rock, none: new[] { "small" }),
            [ObjectKind.Rock] = Query(AssetCategory.Rock, none: new[] { "large" }),
        };


        public static IEnumerable<string> Names => Specs.Select(s => s.name);

        public static CourseTheme Create(string name)
        {
            var spec = Specs.FirstOrDefault(s => s.name == name) ?? Specs[0];
            var theme = ScriptableObject.CreateInstance<CourseTheme>();
            theme.name = name;
            theme.styleTags = spec.styleTags;

            theme.surfaces = SurfaceLayerSet.DefaultEntries().Select(e => new CourseTheme.SurfaceSlot
            {
                entry = new SurfaceLayerSet.Entry
                {
                    surface = e.surface,
                    placeholderColor = e.placeholderColor,
                    stripeWidth = e.stripeWidth,
                    stripeAngle = e.stripeAngle,
                },
                layer = new AssetQuery(AssetCategory.GroundLayer, all: new[] { e.surface }, none: new[] { StripeTag },
                                       prefer: spec.layerPrefer.TryGetValue(e.surface, out var p) ? p : null),
                stripeLayer = new AssetQuery(AssetCategory.GroundLayer, all: new[] { e.surface, StripeTag }),
            }).ToList();

            theme.scatter = KindQueries.Select(kv => new CourseTheme.ScatterSlot { kind = kv.Key, assets = kv.Value }).ToList();
            var native = new[] { "native", "scrub" };
            theme.details = new List<CourseTheme.DetailSlot>
            {
                Detail("Rough grass", new[] { "rough" }, spec.roughGrass, 0.3f,
                       Query(AssetCategory.GroundCover, all: new[] { "grass" }, none: new[] { "dry" })),
                Detail("Native grass", native, spec.nativeGrass, 0.6f,
                       Query(AssetCategory.GroundCover, all: new[] { "grass", "dry" })),
                Detail("Native brush", native, spec.brush, 0.8f,
                       Query(AssetCategory.GroundCover, any: new[] { "bush", "shrub", "heather" }, prefer: spec.brushPrefer.Concat(new[] { "dry" }).ToArray())),
                Detail("Woods floor", new[] { "woods" }, spec.woodsFloor, 0.5f,
                       Query(AssetCategory.GroundCover, any: new[] { "fern", "bush" }, none: new[] { "dry" })),
            };
            theme.waterMaterial = Query(AssetCategory.WaterMaterial);
            KeepToOwnSeason(theme);
            return theme;
        }

        /// <summary>
        /// Tags of seasonal models: a model carrying one (orange broadleaf trees, red bushes) dresses only themes whose
        /// styleTags include it, never the summer themes whose queries would otherwise match it too.
        /// </summary>
        public static readonly string[] SeasonTags = { "autumn" };

        /// <summary>Adds the other seasons' tags to the theme's model queries as excluded. Returns true if it changed anything.</summary>
        public static bool KeepToOwnSeason(CourseTheme theme)
        {
            var others = SeasonTags.Where(t => !theme.styleTags.Contains(t)).ToArray();
            bool changed = false;
            foreach (var slot in theme.scatter) changed |= Exclude(ref slot.assets, others);
            foreach (var slot in theme.details) changed |= Exclude(ref slot.assets, others);
            return changed;
        }

        static bool Exclude(ref AssetQuery query, string[] tags)
        {
            var q = query;
            var missing = tags.Where(t => !q.noneTags.Contains(t)).ToArray();
            if (missing.Length == 0) return false;
            // A new query: the slots of a fresh theme share KindQueries' objects.
            query = new AssetQuery(q.category, q.allTags, q.anyTags, q.noneTags.Concat(missing).ToArray(), q.preferTags);
            return true;
        }

        static AssetQuery Query(AssetCategory c, string[] all = null, string[] any = null, string[] none = null, string[] prefer = null) =>
            new AssetQuery(c, all, any, none, prefer);

        static CourseTheme.DetailSlot Detail(string name, string[] surfaces, float coverage, float patchiness, AssetQuery q) =>
            new CourseTheme.DetailSlot
            {
                rule = new ScatterSet.DetailRule { name = name, surfaces = surfaces, coverage = coverage, patchiness = patchiness },
                assets = q,
            };
    }
}
