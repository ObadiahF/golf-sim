using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>Places OSM-mapped trees and rule-based scatter (trees, shrubs, rocks) as Terrain tree instances.</summary>
    public class TreeScatterer
    {
        const float ClumpNoiseScale = 0.025f; // 1 / ~40 m grove size

        readonly TerrainData data;
        readonly float size;
        readonly System.Random rng;
        readonly List<GameObject> prototypes = new List<GameObject>();
        readonly List<TreeInstance> instances = new List<TreeInstance>();

        TreeScatterer(TerrainData data, float size, int seed)
        {
            this.data = data;
            this.size = size;
            rng = new System.Random(seed);
        }

        /// <returns>Number of instances placed.</returns>
        public static int Apply(TerrainData data, HolePackage pkg, SurfaceLayerSet layers, ScatterSet scatter,
                                float[,,] alpha, int seed)
        {
            var s = new TreeScatterer(data, pkg.sizeMeters, seed);
            var surfaces = new SurfaceSampler(alpha, layers, pkg.sizeMeters, scatter.keepClear, scatter.clearMargin);

            s.PlaceMapped(pkg.trees, scatter, surfaces);
            foreach (var rule in scatter.rules) s.PlaceRule(rule, scatter, surfaces);

            data.treePrototypes = s.prototypes.Select(p => new TreePrototype { prefab = p }).ToArray();
            data.SetTreeInstances(s.instances.ToArray(), true);
            return s.instances.Count;
        }

        void PlaceMapped(Vector2[] points, ScatterSet scatter, SurfaceSampler surfaces)
        {
            if (!scatter.mappedTrees.Any(p => p.prefab)) return;
            foreach (var pos in points)
                // Trust OSM, except for trees that would sit on a green, bunker etc. (mapping error).
                if (!scatter.keepClear.Contains(surfaces.DominantSurface(pos)))
                    Add(ScatterSet.Pick(scatter.mappedTrees, rng), pos);
        }

        void PlaceRule(ScatterSet.Rule rule, ScatterSet scatter, SurfaceSampler surfaces)
        {
            if (rule.perHectare <= 0 || !rule.prototypes.Any(p => p.prefab)) return;

            // Jittered grid: one candidate per cell gives even coverage without overlaps.
            float cell = Mathf.Sqrt(10000f / rule.perHectare);
            int count = Mathf.CeilToInt(size / cell);
            Vector2 noiseOffset = new Vector2(rng.Next(10000), rng.Next(10000));

            for (int gz = 0; gz < count; gz++)
                for (int gx = 0; gx < count; gx++)
                {
                    var pos = new Vector2((gx + (float)rng.NextDouble()) * cell, (gz + (float)rng.NextDouble()) * cell);
                    if (pos.x >= size || pos.y >= size) continue;
                    if (!PassesClumping(pos, rule.clumping, noiseOffset)) continue;
                    if (!rule.surfaces.Contains(surfaces.DominantSurface(pos)) || surfaces.NearKeepClear(pos)) continue;

                    float slope = data.GetSteepness(pos.x / size, pos.y / size);
                    if (slope < rule.slopeDegrees.x || slope > rule.slopeDegrees.y) continue;

                    Add(ScatterSet.Pick(rule.prototypes, rng), pos);
                }
        }

        bool PassesClumping(Vector2 pos, float clumping, Vector2 offset)
        {
            if (clumping <= 0f) return true;
            float noise = Mathf.PerlinNoise(offset.x + pos.x * ClumpNoiseScale, offset.y + pos.y * ClumpNoiseScale);
            // Raise the noise to a power so high clumping leaves wide empty gaps between groves.
            float keep = Mathf.Pow(noise, 1f + clumping * 4f) * (1f + clumping * 3f);
            return rng.NextDouble() < keep;
        }

        void Add(ScatterSet.Prototype proto, Vector2 pos)
        {
            if (proto == null) return;
            int index = prototypes.IndexOf(proto.prefab);
            if (index < 0)
            {
                index = prototypes.Count;
                prototypes.Add(proto.prefab);
            }

            float scale = Mathf.Lerp(proto.scale.x, proto.scale.y, (float)rng.NextDouble());
            instances.Add(new TreeInstance
            {
                prototypeIndex = index,
                position = new Vector3(pos.x / size, 0f, pos.y / size), // y is snapped to the terrain
                widthScale = scale,
                heightScale = scale,
                rotation = (float)(rng.NextDouble() * Mathf.PI * 2),
                color = Color.white,
                lightmapColor = Color.white,
            });
        }
    }
}
