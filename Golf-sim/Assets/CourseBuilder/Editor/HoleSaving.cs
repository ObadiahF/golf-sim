using System.IO;
using GolfSim.Course;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>
    /// Keeps a hole for good: moves its package (hole.json, heightmap and the built terrain, water and cup
    /// assets) out of the generator's scratch folder, where unrated holes get pruned, into
    /// Assets/CourseData/Saved, then saves the scene. Asset GUIDs survive the move, so the scene keeps working.
    /// </summary>
    public static class HoleSaving
    {
        public const string SavedFolder = "Assets/CourseData/Saved";
        const string GeneratedFolder = "Assets/CourseData/generated";

        [MenuItem("Golf/Save Current Hole")]
        static void SaveMenu()
        {
            var hole = HoleNavigation.FindHole();
            if (!hole)
            {
                EditorUtility.DisplayDialog("Save Hole", "There is no hole in the open scene.", "OK");
                return;
            }
            string folder = Save(hole);
            Debug.Log($"[CourseBuilder] Saved hole {hole.holeRef} to {folder} and saved scene {hole.gameObject.scene.path}");
        }

        [MenuItem("Golf/Save Current Hole", true)]
        static bool CanSave() => !EditorApplication.isPlaying;

        /// <summary>Moves the hole's package to the Saved folder if it is a generated one; returns its folder.</summary>
        public static string Save(HoleInfo hole)
        {
            string folder = Path.GetDirectoryName(hole.sourcePackage)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folder) && folder.StartsWith(GeneratedFolder))
            {
                EnsureFolder(SavedFolder);
                string dest = AssetDatabase.GenerateUniqueAssetPath($"{SavedFolder}/{Path.GetFileName(folder)}");
                string error = AssetDatabase.MoveAsset(folder, dest);
                if (!string.IsNullOrEmpty(error)) throw new IOException($"Could not move {folder} to {dest}: {error}");
                hole.sourcePackage = $"{dest}/{Path.GetFileName(hole.sourcePackage)}";
                EditorUtility.SetDirty(hole);
                folder = dest;
            }
            EditorSceneManager.MarkSceneDirty(hole.gameObject.scene);
            EditorSceneManager.SaveScene(hole.gameObject.scene);
            return folder;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
