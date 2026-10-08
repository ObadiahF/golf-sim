// Dev helper (Edit mode), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/SetupCelebration.cs --entry SetupCelebration.Run
// Creates or refreshes the fireworks' spark materials (CupFireworks) in Assets/GolfSim/Game/Resources/Celebration:
// Spark (additive, after dark) and SparkDay (alpha blended, so the sparks show against a bright sky), both URP
// Particles/Unlit on the soft dot Spark.png. In Resources so builds keep them and their shader.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class SetupCelebration
{
    const string Folder = "Assets/GolfSim/Game/Resources/Celebration";

    public static string Run()
    {
        AssetDatabase.Refresh();
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Spark.png");
        var importer = (TextureImporter)AssetImporter.GetAtPath(Folder + "/Spark.png");
        if (importer.alphaIsTransparency == false || importer.mipmapEnabled == false)
        {
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (!shader) return "URP Particles/Unlit shader not found";
        return Make("Spark", shader, texture, additive: true) + "\n" + Make("SparkDay", shader, texture, additive: false);
    }

    static string Make(string name, Shader shader, Texture2D texture, bool additive)
    {
        string path = $"{Folder}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = shader;
        m.SetTexture("_BaseMap", texture);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Surface", 1f);                 // transparent
        m.SetFloat("_Blend", additive ? 2f : 0f);   // additive / alpha
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", (float)CullMode.Off);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssets();
        return $"{path}: {(additive ? "additive" : "alpha")}";
    }
}
