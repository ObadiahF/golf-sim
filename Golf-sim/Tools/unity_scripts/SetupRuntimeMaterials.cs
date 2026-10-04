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
        library.litEmissive = Template("RuntimeLitEmissive", "Universal Render Pipeline/Lit", mat =>
        {
            mat.EnableKeyword("_EMISSION"); // the emissive variant, which no other asset may use
            mat.SetColor("_EmissionColor", Color.black);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        });
        library.nightSky = Template("RuntimeNightSky", "GolfSim/NightSky", _ => { });
        library.starrySky = SkyPhoto("StarrySky_HDR.exr");
        library.twilightSky = SkyPhoto("Twilight_HDR.exr");
        library.water = Template("RuntimeWater", "GolfSim/Water", mat => mat.CopyPropertiesFromMaterial(new Material(mat.shader))); // the shader's defaults
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        return $"lit={library.lit} transparent={library.litTransparent} line={library.line} cup={library.cupMask}/{library.cupInterior} terrain={library.terrain} " +
               $"glow={library.litEmissive} nightSky={library.nightSky} skies={library.starrySky}/{library.twilightSky} water={library.water}";
    }

    /// <summary>
    /// An equirectangular HDR sky from Textures/Sky: wraps around but not over the poles, no mipmaps (they would leave
    /// a seam where the panorama's edges meet; the sky is magnified on screen anyway), BC6H in builds.
    /// </summary>
    static Texture2D SkyPhoto(string file)
    {
        string path = "Assets/CourseBuilder/Textures/Sky/" + file;
        var imp = (TextureImporter)AssetImporter.GetAtPath(path) ?? throw new System.Exception($"No sky photo at {path}");
        imp.textureShape = TextureImporterShape.Texture2D;
        imp.mipmapEnabled = false;
        imp.wrapModeU = TextureWrapMode.Repeat;
        imp.wrapModeV = TextureWrapMode.Clamp;
        imp.filterMode = FilterMode.Bilinear;
        imp.maxTextureSize = 2048;
        imp.textureCompression = TextureImporterCompression.CompressedHQ;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
