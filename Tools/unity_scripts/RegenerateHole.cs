// Dev helper, run from the shell with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/RegenerateHole.cs --entry RegenerateHole.Run
// Rebuilds the hole currently in the scene from its package with the default layer and scatter sets.
using GolfSim.CourseEditor;
using UnityEngine;

public static class RegenerateHole
{
    public static string Run()
    {
        var hole = HoleNavigation.FindHole();
        if (!hole) return "no hole in scene";

        var pkg = HolePackage.Load(hole.sourcePackage);
        var root = HoleTerrainBuilder.Build(pkg, new HoleBuildOptions
        {
            layers = SurfaceLayerSet.LoadOrCreateDefault(),
            scatter = ScatterSet.LoadOrCreateDefault(),
        });
        var data = root.GetComponentInChildren<Terrain>().terrainData;
        return $"{root.name}: {data.terrainLayers.Length} terrain layers, {data.treeInstanceCount} trees/rocks";
    }
}
