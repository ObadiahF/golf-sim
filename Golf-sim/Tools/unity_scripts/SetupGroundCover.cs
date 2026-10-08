// Dev helper (Edit mode), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/SetupGroundCover.cs --entry SetupGroundCover.Run
// The ground and grass of the rough and of the native areas (native/scrub: the out-of-play ground beyond the rough),
// instead of the demo moss and the sparse, near-black dry tufts:
//   rough   ground ambientCG Grass001 (Textures/RoughGrass) on Golf_Rough and its winter and autumn copies; grass the
//           demo's Grass B/C/D with green blades matching that ground; an even, short carpet that thins out over the
//           last few meters toward the fairway
//   native  ground ambientCG Grass004 (Textures/NativeGrass), olive, on Golf_Native; grass the demo's dry tufts
//           (fescue) recoloured straw gold, on a copy of their material with sane shading (theirs halves the light: AO capped at 0.5 and a
//           heavy subsurface shadow), standing thicker with gentler patches
// The grass is registered in the catalog (tags below; Assets/CourseBuilder/Grass, SOURCE.txt) and every theme's rule
// points at it. Then re-bakes RuntimeThemes. Safe to re-run: assets are updated in place, coverage is boosted once.
using System.Linq;
using System.Text;
using GolfSim.Course;
using GolfSim.CourseEditor;
using UnityEditor;
using UnityEngine;

public static class SetupGroundCover
{
    const string TexDir = "Assets/CourseBuilder/Textures/";
    const string GrassDir = "Assets/CourseBuilder/Grass/";
    const string LayerDir = "Assets/CourseBuilder/Layers/";
    const string DemoDetails = "Assets/TerrainDemoScene_URP/Prefabs/Details/";
    const string BaseMap = "Texture2D_E1B0D043"; // the TerrainGrass shader graph's "Base Map"
    const float MaxCoverage = 0.95f;

    /// <summary>One kind of ground cover: the theme rule it fills, its models and how they stand.</summary>
    class Cover
    {
        public string rule, tag;           // the theme's detail rule; the catalog tag that marks our models ("grass" + tag)
        public float boost, patchiness, density, edgeFade;
        public Vector2 scale;
        public (string prefab, string material)[] models; // demo prefab -> our material for it
    }

    static readonly Cover Rough = new Cover
    {
        rule = "Rough grass", tag = "rough", boost = 1.7f, patchiness = 0.12f, density = 8f, edgeFade = 3f,
        scale = new Vector2(0.24f, 0.4f), // short enough to read as rough, not a meadow
        models = new[] { ("Grass_B", "RoughGrass_A"), ("Grass_C", "RoughGrass_C"), ("Grass_D", "RoughGrass_C") },
    };

    static readonly Cover Native = new Cover
    {
        rule = "Native grass", tag = "native", boost = 1.6f, patchiness = 0.25f, density = 9f, edgeFade = 0f,
        scale = new Vector2(0.55f, 0.95f), // fescue: knee-high wisps
        models = new[] { ("GrassDry_A", "NativeFescue"), ("GrassDry_B", "NativeFescue"), ("GrassDry_C", "NativeFescue"), ("GrassDry_D", "NativeFescue") },
    };

    public static string Run()
    {
        AssetDatabase.Refresh(); // textures just copied in
        var log = new StringBuilder();
        var rough = GroundTextures("RoughGrass");
        Ground("Golf_Rough", rough, 5f, new Color(1f, 1f, 1f));
        Ground("Golf_RoughFrost", rough, 5f, new Color(0.85f, 0.92f, 0.95f));
        Ground("Golf_RoughAutumn", rough, 5f, new Color(1.05f, 0.9f, 0.55f));
        Ground("Golf_Native", GroundTextures("NativeGrass"), 6f, new Color(0.9f, 0.9f, 0.78f));
        log.AppendLine("ground: Grass001 on the rough layers, Grass004 on Golf_Native");

        BladeMaterial("RoughGrass_A", "Grass_A", $"{GrassDir}RoughGrass_A_BaseColor.png");
        BladeMaterial("RoughGrass_C", "Grass_C", $"{GrassDir}RoughGrass_C_BaseColor.png");
        var fescue = BladeMaterial("NativeFescue", "Grass_Dry", $"{GrassDir}NativeFescue_BaseColor.png");
        fescue.SetVector("_AORemap", new Vector4(0.5f, 1f, 0f, 0f));        // was (0, 0.5): every blade at half light
        fescue.SetFloat("_SSS_Shadows", 0.9f);                               // was 4
        fescue.SetVector("_Thickness_Remap", new Vector4(0.1f, 1f, 0f, 0f)); // as the green grass
        fescue.SetFloat("Vector1_8651797e3e304e108dbd25f9d5a426ba", 0f);     // smoothness: dry grass isn't glossy

        var catalog = AssetCatalog.Load();
        foreach (var cover in new[] { Rough, Native }) log.Append(Apply(catalog, cover));
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        RuntimeThemeBaker.Bake();
        log.AppendLine("re-baked RuntimeThemes");
        return log.ToString();
    }

    static string Apply(AssetCatalog catalog, Cover cover)
    {
        var log = new StringBuilder();
        var tags = new[] { "grass", cover.tag };
        foreach (var (prefab, material) in cover.models)
        {
            string name = char.ToUpperInvariant(cover.tag[0]) + cover.tag.Substring(1) + prefab; // RoughGrass_B, NativeGrassDry_A
            var model = GrassPrefab(name, $"{DemoDetails}{prefab}.prefab", AssetDatabase.LoadAssetAtPath<Material>($"{GrassDir}{material}.mat"));
            catalog.Register(model, AssetCategory.GroundCover, tags).scale = cover.scale; // the scale too on re-runs
        }
        var query = new AssetQuery(AssetCategory.GroundCover, all: tags);
        foreach (var theme in catalog.themes.Where(t => t))
        {
            var slot = theme.details.FirstOrDefault(d => d.rule.name == cover.rule);
            if (slot == null) continue;
            if (!slot.assets.allTags.Contains(cover.tag)) // the first run only: re-runs keep the boosted coverage
                slot.rule.coverage = Mathf.Min(MaxCoverage, slot.rule.coverage * cover.boost);
            slot.assets = query;
            slot.rule.patchiness = cover.patchiness;
            slot.rule.density = cover.density;
            slot.rule.edgeFadeMeters = cover.edgeFade;
            DefaultThemes.KeepToOwnSeason(theme);
            EditorUtility.SetDirty(theme);
            log.Append($"{theme.name} {slot.rule.coverage:0.00}  ");
        }
        return $"{cover.rule}: {log}\n";
    }

    static (Texture2D color, Texture2D normal, Texture2D mask) GroundTextures(string name) => (
        Tex($"{TexDir}{name}/{name}_Color.jpg", TextureImporterType.Default, true),
        Tex($"{TexDir}{name}/{name}_Normal.jpg", TextureImporterType.NormalMap, false),
        Tex($"{TexDir}{name}/{name}_MaskMap.png", TextureImporterType.Default, false));

    static Texture2D Tex(string path, TextureImporterType type, bool srgb)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer.textureType != type || importer.sRGBTexture != srgb || importer.maxTextureSize != 2048)
        {
            importer.textureType = type;
            importer.sRGBTexture = srgb;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void Ground(string name, (Texture2D color, Texture2D normal, Texture2D mask) tex, float tile, Color tint)
    {
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{LayerDir}{name}.terrainlayer");
        layer.diffuseTexture = tex.color;
        layer.normalMapTexture = tex.normal;
        layer.maskMapTexture = tex.mask;
        layer.tileSize = Vector2.one * tile;
        layer.normalScale = 0.6f;
        layer.diffuseRemapMin = Vector4.zero;
        layer.diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 1f);
        layer.maskMapRemapMin = Vector4.zero;
        layer.maskMapRemapMax = new Vector4(1f, 1f, 1f, 0.1f); // grass is matte
        EditorUtility.SetDirty(layer);
    }

    /// <summary>GrassDir/name.mat: a copy of the demo grass material `source`, with `baseMap` as its blades if given.</summary>
    static Material BladeMaterial(string name, string source, string baseMap)
    {
        string path = $"{GrassDir}{name}.mat";
        var from = AssetDatabase.LoadAssetAtPath<Material>($"{DemoDetails}Materials/{source}.asset");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m)
        {
            m = new Material(from);
            AssetDatabase.CreateAsset(m, path);
        }
        else m.CopyPropertiesFromMaterial(from);
        if (baseMap != null) m.SetTexture(BaseMap, Tex(baseMap, TextureImporterType.Default, true));
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>A copy of a demo grass prefab drawn with `material` (saved at GrassDir/name.prefab).</summary>
    static GameObject GrassPrefab(string name, string sourcePath, Material material)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath));
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        instance.name = name;
        foreach (var r in instance.GetComponentsInChildren<MeshRenderer>())
            r.sharedMaterials = r.sharedMaterials.Select(_ => material).ToArray();
        var prefab = PrefabUtility.SaveAsPrefabAsset(instance, $"{GrassDir}{name}.prefab");
        Object.DestroyImmediate(instance);
        return prefab;
    }
}
