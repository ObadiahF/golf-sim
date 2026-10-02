using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>
    /// Starting themes, matching the generator presets (Tools/course_gen/style.py) plus "coastal",
    /// which reproduces the original real-course look. They only differ in densities and tag
    /// preferences, so every theme works with whatever the catalog holds; tune the assets afterwards.
    /// </summary>
    public static class DefaultThemes
    {
        public const string DefaultName = "coastal";

        class Spec
        {
            public string name;
            public string[] styleTags;
            public float woods = 250, nativeTrees = 8, shrubs = 90, boulders = 60, looseRocks = 25;
            public float roughGrass = 0.8f, nativeGrass = 0.6f, brush = 0.25f, woodsFloor = 0.5f;
            public string[] treePrefer = new string[0];
            public string[] brushPrefer = new string[0];
            public Dictionary<string, string[]> layerPrefer = new Dictionary<string, string[]>();
        }

        static readonly Spec[] Specs =
        {
            new Spec { name = "coastal", styleTags = new[] { "coastal" }, treePrefer = new[] { "conifer" } },
            new Spec { name = "parkland", styleTags = new[] { "parkland", "lush" }, treePrefer = new[] { "deciduous" },
                       nativeTrees = 14, shrubs = 50, boulders = 15, looseRocks = 8, nativeGrass = 0.4f, brush = 0.15f },
            new Spec { name = "forest", styleTags = new[] { "forest" }, treePrefer = new[] { "conifer" },
                       woods = 340, nativeTrees = 30, shrubs = 70, boulders = 25, looseRocks = 15, woodsFloor = 0.7f },
            new Spec { name = "lakes", styleTags = new[] { "lakes", "lush" }, treePrefer = new[] { "deciduous", "willow" },
                       woods = 220, nativeTrees = 10, shrubs = 40, boulders = 10, looseRocks = 10, brush = 0.15f },
            new Spec { name = "links", styleTags = new[] { "links", "coastal" }, brushPrefer = new[] { "heather" },
                       woods = 120, nativeTrees = 1, shrubs = 40, boulders = 5, looseRocks = 5, nativeGrass = 0.85f, brush = 0.35f },
            new Spec { name = "desert", styleTags = new[] { "desert", "dry" }, treePrefer = new[] { "cactus", "palm" },
                       woods = 60, nativeTrees = 5, shrubs = 120, boulders = 90, looseRocks = 60, roughGrass = 0.5f,
                       nativeGrass = 0.3f, brush = 0.3f, layerPrefer = { ["native"] = new[] { "sand", "desert" } } },
            new Spec { name = "mountain", styleTags = new[] { "mountain" }, treePrefer = new[] { "conifer" },
                       woods = 300, nativeTrees = 20, shrubs = 60, boulders = 120, looseRocks = 50,
                       layerPrefer = { ["native"] = new[] { "rock", "mountain" } } },
        };

        public const string StripeTag = "stripe";


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

            var native = new[] { "native", "scrub" };
            theme.scatter = new List<CourseTheme.ScatterSlot>
            {
                Scatter("Woods", new[] { "woods" }, spec.woods, 0.3f, Query(AssetCategory.Tree, prefer: spec.treePrefer)),
                Scatter("Native trees", native, spec.nativeTrees, 0.7f, Query(AssetCategory.Tree, prefer: spec.treePrefer)),
                Scatter("Shrubs", native, spec.shrubs, 0.6f, Query(AssetCategory.Shrub), maxSlope: 40),
                Scatter("Slope boulders", native, spec.boulders, 0.5f, Query(AssetCategory.Rock, none: new[] { "small" }), minSlope: 25),
                Scatter("Loose rocks", native, spec.looseRocks, 0.4f, Query(AssetCategory.Rock, none: new[] { "large" })),
            };
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
            theme.mappedTrees = Query(AssetCategory.Tree, prefer: spec.treePrefer);
            theme.waterMaterial = Query(AssetCategory.WaterMaterial);
            return theme;
        }

        static AssetQuery Query(AssetCategory c, string[] all = null, string[] any = null, string[] none = null, string[] prefer = null) =>
            new AssetQuery(c, all, any, none, prefer);

        static CourseTheme.ScatterSlot Scatter(string name, string[] surfaces, float perHectare, float clumping, AssetQuery q,
                                               float minSlope = 0, float maxSlope = 35) =>
            new CourseTheme.ScatterSlot
            {
                rule = new ScatterSet.Rule
                {
                    name = name, surfaces = surfaces, perHectare = perHectare, clumping = clumping,
                    slopeDegrees = new Vector2(minSlope, minSlope > 0 ? 90 : maxSlope),
                },
                assets = q,
            };

        static CourseTheme.DetailSlot Detail(string name, string[] surfaces, float coverage, float patchiness, AssetQuery q) =>
            new CourseTheme.DetailSlot
            {
                rule = new ScatterSet.DetailRule { name = name, surfaces = surfaces, coverage = coverage, patchiness = patchiness },
                assets = q,
            };
    }
}
