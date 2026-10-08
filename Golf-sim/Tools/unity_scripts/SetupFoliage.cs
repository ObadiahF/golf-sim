// Dev helper (Edit mode), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/SetupFoliage.cs --entry SetupFoliage.Run
// Puts the trees that aren't SpeedTrees on GolfSim/Foliage (URP Lit plus the wind sway, FoliageSway.hlsl): the
// broadleaves' leaves and trunk and the palm. Their Lit settings carry over (same properties and keywords). The
// SpeedTrees sway through FoliageWind's WindZone instead.
using System.Text;
using UnityEditor;
using UnityEngine;

public static class SetupFoliage
{
    static readonly string[] Materials =
    {
        "Assets/CourseBuilder/Trees/Broadleaf_Leaves.mat",
        "Assets/CourseBuilder/Trees/BroadleafGold_Leaves.mat",
        "Assets/CourseBuilder/Trees/BroadleafOrange_Leaves.mat",
        "Assets/CourseBuilder/Trees/BroadleafRed_Leaves.mat",
        "Assets/VegetationSpawner/_Demo/Materials/DemoTree_Trunk.mat",
        "Assets/PinwheelStudio/Vista/Personal/Samples/Materials/VistaSample_Palm.mat",
    };

    public static string Run()
    {
        var shader = Shader.Find("GolfSim/Foliage");
        if (!shader) return "GolfSim/Foliage not found (compile errors?)";
        var log = new StringBuilder();
        foreach (var path in Materials)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { log.AppendLine($"MISSING {path}"); continue; }
            int queue = m.renderQueue;
            m.shader = shader;
            m.renderQueue = queue;
            EditorUtility.SetDirty(m);
            log.AppendLine($"{path}: {m.shader.name}, alpha clip {m.IsKeywordEnabled("_ALPHATEST_ON")}");
        }
        AssetDatabase.SaveAssets();
        return log.ToString();
    }
}
