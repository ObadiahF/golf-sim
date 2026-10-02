// Dev helper, run from the shell with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/RegenerateHole.cs --entry RegenerateHole.Run
//   unity command run_script --file Tools/unity_scripts/RegenerateHole.cs --entry RegenerateHole.HoleSimulator
// Rebuilds the hole in the open scene (or in the Hole Simulator scene, then saves it) from its package,
// dressed by the package's theme, exactly like Golf > Course Generator does.
using GolfSim.Course;
using GolfSim.CourseEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RegenerateHole
{
    public static string Run()
    {
        var hole = HoleNavigation.FindHole();
        if (!hole) return "no hole in scene";
        var root = Rebuild(hole.sourcePackage);
        var data = root.GetComponentInChildren<Terrain>().terrainData;
        return $"{root.name}: {data.terrainLayers.Length} terrain layers, {data.treeInstanceCount} trees/rocks";
    }

    public static string HoleSimulator()
    {
        string previous = EditorSceneManager.GetActiveScene().path;
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/HoleSimulator.unity", OpenSceneMode.Single);
        string result = Run();
        EditorSceneManager.SaveScene(scene);
        if (!string.IsNullOrEmpty(previous) && previous != scene.path) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
        return result;
    }

    static GameObject Rebuild(string packagePath)
    {
        var pkg = HolePackage.Load(packagePath);
        var catalog = AssetCatalog.Load();
        var fallback = new HoleBuildOptions { layers = CourseDefaults.Layers(), scatter = CourseDefaults.Scatter() };
        return ThemeResolver.Build(pkg, ThemeResolver.ThemeFor(pkg, catalog), catalog, fallback);
    }
}
