// Dev helper (Edit mode), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/SetupEffects.cs --entry SetupEffects.Run
// Creates or refreshes the particle effects' materials in Assets/GolfSim/Game/Resources/Effects, all URP Particles/Unlit:
// Spark (additive, after dark) and SparkDay (alpha blended, so they show against a bright sky) on the soft dot
// Spark.png, for the fireworks (CupFireworks) and the water's droplets and spray (WaterSplash); Ripple (alpha blended)
// on the ring Ring.png for the splash's rings. In Resources so builds keep them and their shader.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class SetupEffects
{
    const string Folder = "Assets/GolfSim/Game/Resources/Effects";

    public static string Run()
    {
        AssetDatabase.Refresh();
        var spark = Texture("Spark.png");
        var ring = Texture("Ring.png");
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (!shader) return "URP Particles/Unlit shader not found";
        return Make("Spark", shader, spark, additive: true) + "\n" + Make("SparkDay", shader, spark, additive: false) + "\n" +
               Make("Ripple", shader, ring, additive: false);
    }

    static Texture2D Texture(string file)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath($"{Folder}/{file}");
        if (!importer.alphaIsTransparency || !importer.mipmapEnabled || importer.wrapMode != TextureWrapMode.Clamp)
        {
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}/{file}");
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
