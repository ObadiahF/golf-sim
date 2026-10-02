using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GolfSim.Course
{
    public class HoleBuildOptions
    {
        public SurfaceLayerSet layers;
        public ScatterSet scatter; // null = no trees/rocks/ground cover
        public WaterProvider water; // null = flat meshes using layers.waterMaterial
        public int seed = 1;
        public int blurRadius = 1;
        public float pixelError = 2f;
    }

    /// <summary>
    /// Builds a hole (terrain, surfaces, trees and rocks, ground cover, water, tee, pin and cup) from a hole
    /// package. Works in the editor (assets saved through EditorHoleAssets) and in the game (all in memory).
    /// </summary>
    public static class HoleBuilder
    {
        const int LayersPerPass = 4; // URP terrain draws 4 layers per pass; more layers add passes

        public static GameObject Build(HolePackage pkg, HoleBuildOptions options, HoleAssets assets,
                                       Action<string, float> progress = null)
        {
            progress ??= (_, _) => { };
            SurfaceLayerSet layers = null;
            try
            {
                // Persist before painting: in the editor, CreateAsset discards alphamaps set on an unsaved TerrainData.
                var data = new TerrainData { heightmapResolution = pkg.heightmap.resolution }; // resolution before size
                data.size = new Vector3(pkg.sizeMeters, pkg.HeightRange, pkg.sizeMeters);
                data = assets.PerHole("TerrainData.asset", data);

                progress("Heightmap", 0.1f);
                data.SetHeights(0, 0, pkg.LoadHeights());

                progress("Surface layers", 0.3f);
                layers = options.layers.Subset(pkg.areas.Select(a => a.surface).ToHashSet());
                if (layers.entries.Count > LayersPerPass * 2)
                    Debug.Log($"[CourseBuilder] {layers.entries.Count} terrain layers = {(layers.entries.Count + LayersPerPass - 1) / LayersPerPass} render passes.");
                data.terrainLayers = layers.ResolveLayers(assets);
                data.alphamapResolution = pkg.heightmap.resolution - 1;

                progress("Painting surfaces", 0.5f);
                var unknown = new HashSet<string>();
                var alpha = SplatmapBuilder.Build(pkg, layers, data.alphamapResolution, options.blurRadius, unknown);
                data.SetAlphamaps(0, 0, alpha);
                if (unknown.Count > 0)
                    Debug.LogWarning($"[CourseBuilder] Surfaces not in {options.layers.name}, left unpainted: {string.Join(", ", unknown)}");

                if (options.scatter)
                {
                    progress("Trees and rocks", 0.7f);
                    int placed = TreeScatterer.Apply(data, pkg, options.scatter);
                    Debug.Log($"[CourseBuilder] Placed {placed} of {pkg.objects.count} trees, shrubs and rocks from {pkg.objects.file}");
                }
                return CreateObjects(pkg, data, options, layers, alpha, assets, progress);
            }
            finally
            {
                if (layers) Object.DestroyImmediate(layers); // in-memory subset, never saved
            }
        }

        static GameObject CreateObjects(HolePackage pkg, TerrainData data, HoleBuildOptions options, SurfaceLayerSet layers,
                                        float[,,] alpha, HoleAssets assets, Action<string, float> progress)
        {
            var root = new GameObject($"Hole {pkg.holeRef} - {pkg.course}");
            var terrainGo = Terrain.CreateTerrainGameObject(data);
            terrainGo.name = "Terrain";
            terrainGo.transform.SetParent(root.transform, false);
            var terrain = terrainGo.GetComponent<Terrain>();
            terrain.heightmapPixelError = options.pixelError;
            if (options.scatter)
            {
                progress("Grass", 0.85f);
                int detailLayers = DetailScatterer.Apply(terrain, pkg, layers, options.scatter, alpha, options.seed);
                Debug.Log($"[CourseBuilder] Painted {detailLayers} grass/ground-cover layers");
            }
            progress("Water and pin", 0.95f);
            if (options.water) options.water.Build(root.transform, pkg, assets);
            else WaterBuilder.Create(root.transform, pkg, options.layers.waterMaterial, assets);

            var tee = pkg.ToLocal(pkg.tee, terrain);
            var pin = pkg.ToLocal(pkg.pin, terrain);
            HoleMarkers.Create(root.transform, tee, pin, terrain, assets);
            HoleMarkers.AimTee(root.transform, tee, pin);

            var info = root.AddComponent<HoleInfo>();
            info.course = pkg.course;
            info.holeRef = pkg.holeRef;
            info.par = pkg.par;
            info.handicap = pkg.handicap;
            info.teePosition = tee;
            info.pinPosition = pin;
            info.holePath = Enumerable.Range(0, pkg.holePath.Count).Select(i => pkg.ToLocal(pkg.holePath[i], terrain)).ToArray();
            info.sourcePackage = pkg.assetPath;
            info.terrainLayerSurfaces = layers.SurfaceNames();
            return root;
        }
    }
}
