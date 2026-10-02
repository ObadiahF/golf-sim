// Dev helper, run from the shell with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/GenerateTest.cs --entry GenerateTest.Forest
// Same path as Golf > Course Generator > Generate Random Hole: Python layout + terrain, then the
// catalog theme build. Returns what was built.
using System.IO;
using GolfSim.CourseEditor;
using UnityEditor;
using UnityEngine;

public static class GenerateTest
{
    public static string Forest() => Generate("forest", 4);
    public static string Lakes() => Generate("lakes", 4);
    public static string Links() => Generate("links", 4);

    static string Generate(string preset, int par)
    {
        var outDir = Path.Combine(PythonRunner.ProjectRoot, "Assets/CourseData/generated");
        string output = PythonRunner.Run("Generating", "generate", "--preset", preset, "--par", par.ToString(), "--seed", "7", "--out", outDir);
        var last = PythonRunner.LastJson<GeneratedHoleResult>(output);
        string folder = PythonRunner.ToAssetPath(last.package);
        AssetDatabase.Refresh();

        var pkg = HolePackage.Load($"{folder}/{HolePackage.FileName}");
        var catalog = AssetCatalog.Load();
        var fallback = new HoleBuildOptions { layers = SurfaceLayerSet.LoadOrCreateDefault(), scatter = ScatterSet.LoadOrCreateDefault(), seed = last.seed };
        var theme = ThemeResolver.ThemeFor(pkg, catalog);
        var root = ThemeResolver.Build(pkg, theme, catalog, fallback);
        var data = root.GetComponentInChildren<Terrain>().terrainData;
        return $"{preset} par {pkg.par}: {pkg.sizeMeters:0} m tile, theme {(theme ? theme.name : "none")}, " +
               $"{data.terrainLayers.Length} layers, {data.treeInstanceCount} trees/rocks, {data.detailPrototypes.Length} grass layers, water bodies {pkg.water.Length}";
    }
}
