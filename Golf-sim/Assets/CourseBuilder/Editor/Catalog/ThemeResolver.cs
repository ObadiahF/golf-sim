using GolfSim.Course;
using System.Collections.Generic;
using GolfSim.Course;
using System.Linq;
using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>
    /// Turns a CourseTheme + AssetCatalog into the in-memory SurfaceLayerSet / ScatterSet the hole
    /// builder already understands, and builds holes with them. Missing assets degrade gracefully:
    /// an unmatched ground layer becomes a placeholder colour, an unmatched rule simply scatters nothing.
    /// </summary>
    public static class ThemeResolver
    {
        public class Resolved : System.IDisposable
        {
            public SurfaceLayerSet layers;
            public ScatterSet scatter;
            public WaterProvider water;
            readonly bool ownsWater;

            public Resolved(SurfaceLayerSet layers, ScatterSet scatter, WaterProvider water, bool ownsWater)
            {
                this.layers = layers;
                this.scatter = scatter;
                this.water = water;
                this.ownsWater = ownsWater;
            }

            public void Dispose()
            {
                if (layers) Object.DestroyImmediate(layers);
                if (scatter) Object.DestroyImmediate(scatter);
                if (ownsWater && water) Object.DestroyImmediate(water);
            }
        }

        public static Resolved Resolve(CourseTheme theme, AssetCatalog catalog)
        {
            var tags = theme.styleTags;
            var layers = ScriptableObject.CreateInstance<SurfaceLayerSet>();
            layers.name = theme.name;
            layers.entries = theme.surfaces.Select(slot => new SurfaceLayerSet.Entry
            {
                surface = slot.entry.surface,
                placeholderColor = slot.entry.placeholderColor,
                layer = catalog.Find(slot.layer, tags).FirstOrDefault()?.Layer,
                stripeLayer = slot.entry.stripeWidth > 0f ? catalog.Find(slot.stripeLayer, tags).FirstOrDefault()?.Layer : null,
                stripeWidth = slot.entry.stripeWidth,
                stripeAngle = slot.entry.stripeAngle,
            }).ToList();

            var scatter = ScriptableObject.CreateInstance<ScatterSet>();
            scatter.name = theme.name;
            scatter.rules = theme.scatter.Select(slot => new ScatterSet.Rule
            {
                name = slot.kind.ToString(), kind = slot.kind, prototypes = Prototypes(catalog, slot.assets, tags, 1f),
            }).ToList();
            scatter.detailRules = theme.details.Select(slot => WithPrototypes(slot.rule,
                Prototypes(catalog, slot.assets, tags, slot.scale).Take(slot.maxPrototypes).ToList())).ToList();
            scatter.keepClear = theme.keepClear;
            scatter.detailClearMargin = theme.detailClearMargin;

            var water = theme.waterProvider;
            bool ownsWater = false;
            if (!water)
            {
                var mesh = ScriptableObject.CreateInstance<MeshWaterProvider>();
                mesh.material = catalog.Find(theme.waterMaterial, tags).FirstOrDefault()?.Material;
                water = mesh;
                ownsWater = true;
            }
            return new Resolved(layers, scatter, water, ownsWater);
        }

        /// <summary>
        /// Builds a hole dressed by `theme` when given, else with the fallback options' sets.
        /// Seed, blur and the scatter on/off choice always come from the fallback options.
        /// </summary>
        public static GameObject Build(HolePackage pkg, CourseTheme theme, AssetCatalog catalog, HoleBuildOptions fallback)
        {
            if (!theme || !catalog)
                return HoleTerrainBuilder.Build(pkg, fallback);

            using (var resolved = Resolve(theme, catalog))
            {
                var options = new HoleBuildOptions
                {
                    layers = resolved.layers,
                    scatter = fallback.scatter ? resolved.scatter : null,
                    water = resolved.water,
                    seed = fallback.seed,
                    blurRadius = fallback.blurRadius,
                    pixelError = fallback.pixelError,
                };
                var root = HoleTerrainBuilder.Build(pkg, options);
                Debug.Log($"[CourseBuilder] Dressed with theme '{theme.name}'");
                return root;
            }
        }

        /// <summary>The theme a package asks for, or the catalog default (null when no catalog exists yet).</summary>
        public static CourseTheme ThemeFor(HolePackage pkg, AssetCatalog catalog) =>
            catalog ? catalog.FindTheme(pkg.theme) : null;

        static List<ScatterSet.Prototype> Prototypes(AssetCatalog catalog, AssetQuery query, string[] tags, float scale)
        {
            // Entries with more preferred tags get proportionally more weight, so a theme leans toward
            // fitting assets but still uses everything that qualifies.
            return catalog.Find(query, tags)
                .Where(e => e.Prefab)
                .Select(e => new ScatterSet.Prototype
                {
                    prefab = e.Prefab,
                    weight = e.weight * (1 + query.Score(e, tags)),
                    scale = e.scale * scale,
                }).ToList();
        }

        static ScatterSet.DetailRule WithPrototypes(ScatterSet.DetailRule rule, List<ScatterSet.Prototype> prototypes)
        {
            var copy = JsonUtility.FromJson<ScatterSet.DetailRule>(JsonUtility.ToJson(rule));
            copy.prototypes = prototypes;
            return copy;
        }
    }
}
