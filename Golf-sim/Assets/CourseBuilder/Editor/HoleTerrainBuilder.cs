using GolfSim.Course;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>Builds a hole into the open scene, saving its terrain, cup and water meshes next to the package.</summary>
    public static class HoleTerrainBuilder
    {
        public static GameObject Build(HolePackage pkg, HoleBuildOptions options)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Exit Play mode before generating: anything built during Play is thrown away when it stops.");
            if (options.layers.AddMissingDefaults()) EditorUtility.SetDirty(options.layers);
            try
            {
                RemoveExistingHoles();
                var root = HoleBuilder.Build(pkg, options, new EditorHoleAssets(pkg.Folder),
                    (step, t) => EditorUtility.DisplayProgressBar("Generating hole", step, t));
                AssetDatabase.SaveAssets();

                Undo.RegisterCreatedObjectUndo(root, $"Generate Hole {pkg.holeRef}");
                EditorSceneManager.MarkSceneDirty(root.scene);
                Selection.activeGameObject = root;
                var info = root.GetComponent<HoleInfo>();
                HoleNavigation.SetUpPlayCamera(info);
                HoleNavigation.ShowInSceneView(info, HoleView.Overview);
                return root;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>One hole per scene for now: every hole is built at the origin, so they would overlap.</summary>
        static void RemoveExistingHoles()
        {
            foreach (var hole in Object.FindObjectsByType<HoleInfo>(FindObjectsInactive.Include))
                Undo.DestroyObjectImmediate(hole.gameObject);
        }
    }
}
