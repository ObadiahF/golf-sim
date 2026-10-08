using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GolfSim.Course
{
    /// <summary>Paints grass and ground cover as Terrain detail meshes (the only detail type HDRP renders).</summary>
    public static class DetailScatterer
    {
        const float TargetCellMeters = 0.5f;
        const int ResolutionPerPatch = 32;
        const float PatchNoiseScale = 0.08f; // 1 / ~12 m patches
        const float DrawDistance = 100f;      // m

        /// <returns>Number of detail layers painted.</returns>
        public static int Apply(Terrain terrain, HolePackage pkg, SurfaceLayerSet layers, ScatterSet scatter,
                                float[,,] alpha, int seed)
        {
            var data = terrain.terrainData;
            int res = DetailResolution(pkg.sizeMeters);
            data.SetDetailResolution(res, ResolutionPerPatch);
            data.SetDetailScatterMode(DetailScatterMode.CoverageMode);

            float fade = scatter.detailRules.Count > 0 ? scatter.detailRules.Max(r => r.edgeFadeMeters) : 0f;
            var surfaces = new SurfaceSampler(alpha, layers, pkg.sizeMeters, scatter.keepClear, scatter.detailClearMargin, fade);
            var rng = new System.Random(seed);
            var prototypes = new List<DetailPrototype>();
            var maps = new List<int[,]>();

            foreach (var rule in scatter.detailRules)
            {
                var entries = rule.prototypes.Where(p => p.prefab && p.weight > 0).ToList();
                if (entries.Count == 0 || rule.coverage <= 0) continue;
                float totalWeight = entries.Sum(p => p.weight);
                var eligible = EligibleCells(data, res, pkg.sizeMeters, rule, surfaces);

                foreach (var entry in entries)
                {
                    prototypes.Add(Prototype(entry, rule.density));
                    maps.Add(Paint(eligible, res, pkg.sizeMeters, rule, entry.weight / totalWeight, surfaces, rng));
                }
            }

            data.detailPrototypes = prototypes.ToArray();
            for (int i = 0; i < maps.Count; i++) data.SetDetailLayer(0, 0, i, maps[i]);
            // Past 100 m the ground's own texture carries the look; the quality level can thin the carpet
            // (ProjectSettings: Performant draws half of it).
            terrain.detailObjectDistance = DrawDistance;
            terrain.detailObjectDensity = QualitySettings.terrainDetailDensityScale;
            return maps.Count;
        }

        static int DetailResolution(float sizeMeters)
        {
            int cells = Mathf.ClosestPowerOfTwo(Mathf.RoundToInt(sizeMeters / TargetCellMeters));
            return Mathf.Clamp(cells, 256, 2048);
        }

        /// <summary>Cells whose surface and slope match the rule (computed once, shared by its prototypes).</summary>
        static bool[,] EligibleCells(TerrainData data, int res, float size, ScatterSet.DetailRule rule, SurfaceSampler surfaces)
        {
            var eligible = new bool[res, res];
            float cell = size / res;
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    var pos = new Vector2((x + 0.5f) * cell, (z + 0.5f) * cell);
                    if (!rule.surfaces.Contains(surfaces.DominantSurface(pos)) || surfaces.NearKeepClear(pos)) continue;
                    float slope = data.GetSteepness(pos.x / size, pos.y / size);
                    eligible[z, x] = slope >= rule.slopeDegrees.x && slope <= rule.slopeDegrees.y;
                }
            return eligible;
        }

        static int[,] Paint(bool[,] eligible, int res, float size, ScatterSet.DetailRule rule, float share, SurfaceSampler surfaces,
                            System.Random rng)
        {
            var map = new int[res, res];
            float cell = size / res;
            bool fades = rule.edgeFadeMeters > 0f;
            var offset = new Vector2(rng.Next(10000), rng.Next(10000)); // per-prototype noise so species mix
            float full = rule.coverage * share * 255f;
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    if (!eligible[z, x]) continue;
                    float noise = Mathf.PerlinNoise(offset.x + x * PatchNoiseScale, offset.y + z * PatchNoiseScale);
                    // Patchiness pushes the noise through a steeper curve: more bare gaps, denser clumps.
                    float density = Mathf.Clamp01((noise - rule.patchiness * 0.5f) / (1f - rule.patchiness * 0.5f));
                    if (fades) density *= surfaces.EdgeFade(new Vector2((x + 0.5f) * cell, (z + 0.5f) * cell));
                    map[z, x] = Mathf.RoundToInt(full * density);
                }
            return map;
        }

        static DetailPrototype Prototype(ScatterSet.Prototype entry, float density) => new DetailPrototype
        {
            usePrototypeMesh = true,
            prototype = entry.prefab,
            renderMode = DetailRenderMode.VertexLit,
            useInstancing = true, // required by HDRP
            minWidth = entry.scale.x,
            maxWidth = entry.scale.y,
            minHeight = entry.scale.x,
            maxHeight = entry.scale.y,
            noiseSpread = 0.3f,
            alignToGround = 0.4f,
            positionJitter = 1f,
            density = density,
        };
    }
}
