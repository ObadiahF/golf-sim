using System.Collections.Generic;
using System.Linq;
using GolfSim.Course;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GolfSim.CourseEditor
{
    public class HoleBuildOptions
    {
        public SurfaceLayerSet layers;
        public ScatterSet scatter; // null = no trees/rocks
        public int seed = 1;
        public int blurRadius = 1;
        public float pixelError = 2f;
    }

    /// <summary>Builds a hole (terrain + paint + markers) in the open scene from a hole package.</summary>
    public static class HoleTerrainBuilder
    {
        const int MaxHdrpTerrainLayers = 8;

        public static GameObject Build(HolePackage pkg, HoleBuildOptions options)
        {
            SurfaceLayerSet layers = null;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Exit Play mode before generating: anything built during Play is thrown away when it stops.");
            try
            {
                // Save the asset before painting: CreateAsset discards alphamaps set on an unsaved TerrainData.
                var data = new TerrainData { heightmapResolution = pkg.heightmapResolution }; // resolution before size
                data.size = new Vector3(pkg.sizeMeters, pkg.HeightRange, pkg.sizeMeters);
                GeneratedAssets.SaveFresh($"{pkg.Folder}/TerrainData.asset", data);

                Progress("Heightmap", 0.1f);
                data.SetHeights(0, 0, pkg.LoadHeights());

                Progress("Surface layers", 0.3f);
                options.layers.AddMissingDefaults();
                layers = options.layers.Subset(pkg.areas.Select(a => a.surface).ToHashSet());
                if (layers.entries.Count > MaxHdrpTerrainLayers)
                    Debug.LogWarning($"[CourseBuilder] {layers.entries.Count} surface layers; HDRP terrain only renders {MaxHdrpTerrainLayers}.");
                data.terrainLayers = layers.ResolveLayers();
                data.alphamapResolution = pkg.heightmapResolution - 1;

                Progress("Painting surfaces", 0.5f);
                var unknown = new HashSet<string>();
                var alpha = SplatmapBuilder.Build(pkg, layers, data.alphamapResolution, options.blurRadius, unknown);
                data.SetAlphamaps(0, 0, alpha);
                if (unknown.Count > 0)
                    Debug.LogWarning($"[CourseBuilder] Surfaces not in {options.layers.name}, left unpainted: {string.Join(", ", unknown)}");

                if (options.scatter)
                {
                    Progress("Trees and rocks", 0.7f);
                    int placed = TreeScatterer.Apply(data, pkg, layers, options.scatter, alpha, options.seed);
                    Debug.Log($"[CourseBuilder] Placed {placed} trees/rocks ({pkg.trees.Length} mapped in OSM)");
                }

                Progress("Saving terrain", 0.8f);
                EditorUtility.SetDirty(data);
                var root = CreateSceneObjects(pkg, data, options, layers, alpha);
                AssetDatabase.SaveAssets();
                return root;
            }
            finally
            {
                if (layers) Object.DestroyImmediate(layers); // in-memory subset, never saved
                EditorUtility.ClearProgressBar();
            }
        }

        static GameObject CreateSceneObjects(HolePackage pkg, TerrainData data, HoleBuildOptions options,
                                             SurfaceLayerSet layers, float[,,] alpha)
        {
            RemoveExistingHoles();

            var root = new GameObject($"Hole {pkg.holeRef} - {pkg.course}");
            var terrainGo = Terrain.CreateTerrainGameObject(data);
            terrainGo.name = "Terrain";
            terrainGo.transform.SetParent(root.transform, false);
            var terrain = terrainGo.GetComponent<Terrain>();
            terrain.heightmapPixelError = options.pixelError;
            if (options.scatter)
            {
                Progress("Grass", 0.85f);
                int detailLayers = DetailScatterer.Apply(terrain, pkg, layers, options.scatter, alpha, options.seed);
                Debug.Log($"[CourseBuilder] Painted {detailLayers} grass/ground-cover layers");
            }
            WaterBuilder.Create(root.transform, pkg, options.layers.waterMaterial);

            var tee = pkg.ToLocal(pkg.tee, terrain);
            var pin = pkg.ToLocal(pkg.pin, terrain);
            HoleMarkers.Create(root.transform, tee, pin);
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

            Undo.RegisterCreatedObjectUndo(root, $"Generate Hole {pkg.holeRef}");
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            HoleNavigation.SetUpPlayCamera(info);
            HoleNavigation.ShowInSceneView(info, HoleView.Overview);
            return root;
        }

        /// <summary>One hole per scene for now: every hole is built at the origin, so they would overlap.</summary>
        static void RemoveExistingHoles()
        {
            foreach (var hole in Object.FindObjectsByType<HoleInfo>(FindObjectsInactive.Include))
                Undo.DestroyObjectImmediate(hole.gameObject);
        }

        static void Progress(string step, float t) => EditorUtility.DisplayProgressBar("Generating hole", step, t);
    }
}
