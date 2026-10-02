// Dev helper, run from the shell with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/SetupRuntimeMaterials.cs --entry SetupRuntimeMaterials.Run
// Makes the material templates the game clones in code (RuntimeMaterials) under Assets/CourseBuilder/Materials and
// the Resources/RuntimeMaterials asset that lists them, so their shaders and keyword variants are in every build.
// Safe to re-run: existing templates are updated in place (their GUIDs, and so the references, stay).
using GolfSim.Course;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class SetupRuntimeMaterials
{
    const string Folder = "Assets/CourseBuilder/Materials";
    const string LibraryPath = "Assets/CourseBuilder/Resources/" + RuntimeMaterials.ResourceName + ".asset";
    const string UrpTerrainLit = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/TerrainLit.mat";

    public static string Run()
    {
        var library = AssetDatabase.LoadAssetAtPath<RuntimeMaterials>(LibraryPath);
        if (!library)
        {
            EnsureFolder("Assets/CourseBuilder/Resources");
            library = ScriptableObject.CreateInstance<RuntimeMaterials>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }
        library.lit = Template("RuntimeLit", "Universal Render Pipeline/Lit", _ => { });
        library.litTransparent = Template("RuntimeLitTransparent", "Universal Render Pipeline/Lit", mat =>
        {
            mat.SetFloat("_Smoothness", 0.95f);
            Transparent(mat);
        });
        library.line = Template("RuntimeLine", "Universal Render Pipeline/Particles/Unlit", Transparent);
        library.cupMask = Template("RuntimeCupMask", "GolfSim/CupMask", _ => { });
        library.cupInterior = Template("RuntimeCupInterior", "GolfSim/CupInterior", _ => { });
        library.terrain = AssetDatabase.LoadAssetAtPath<Material>(UrpTerrainLit);
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        return $"lit={library.lit} transparent={library.litTransparent} line={library.line} cup={library.cupMask}/{library.cupInterior} terrain={library.terrain}";
    }

    static Material Template(string name, string shaderName, System.Action<Material> setup)
    {
        var shader = Shader.Find(shaderName) ?? throw new System.Exception($"No shader {shaderName}");
        EnsureFolder(Folder);
        string path = $"{Folder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        setup(mat);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>URP alpha-blended surface: the keyword is on the template so its variant is built.</summary>
    static void Transparent(Material mat)
    {
        mat.SetFloat("_Surface", 1f); // transparent
        mat.SetFloat("_Blend", 0f);   // alpha
        mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.renderQueue = (int)RenderQueue.Transparent;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        int slash = folder.LastIndexOf('/');
        AssetDatabase.CreateFolder(folder.Substring(0, slash), folder.Substring(slash + 1));
    }
}
