// Dev helper (edit mode), run from the shell with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/SetupSceneryThemes.cs --entry SetupSceneryThemes.Run
// The scenic course types (autumn, tropical, canyon, winter, heathland; DefaultThemes.cs): builds their ground
// TerrainLayers from the CC0 ambientCG textures in Assets/CourseBuilder/Textures (CREDITS.txt) plus tinted copies of
// the rough and bunker, makes broadleaf tree variants (a calmer green, and orange, red and gold for autumn), adds them,
// palms, heather and red bushes to the asset catalog with the tags their themes ask for, (re)writes the scenic theme
// assets from DefaultThemes, keeps autumn models out of the other themes, and re-bakes RuntimeThemes. Safe to re-run:
// assets are updated in place, so their GUIDs (and every reference to them) stay.
using System;
using System.Linq;
using GolfSim.Course;
using GolfSim.CourseEditor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class SetupSceneryThemes
{
    const string TexDir = "Assets/CourseBuilder/Textures/";
    const string LayerDir = "Assets/CourseBuilder/Layers/";
    const string TreeDir = "Assets/CourseBuilder/Trees/";
    const string ThemeDir = "Assets/CourseBuilder/Settings/Themes/";
    const string RoundPath = "Assets/GolfSim/Game/Resources/CourseRound.asset";
    const string Broadleaf = "Assets/VegetationSpawner/_Demo/Prefabs/DemoBroadleaf.prefab";
    const string BroadleafLeaves = "Assets/VegetationSpawner/_Demo/Materials/DemoBroadLeaf_Branch.mat";
    static readonly string[] Scenic = { "autumn", "tropical", "canyon", "winter", "heathland" };

    public static string Run()
    {
        var catalog = AssetCatalog.Load() ?? throw new Exception("No asset catalog: Golf > Catalog > Build Catalog From Current Assets first");

        // Ground: one textured layer per look. Tags are the surfaces it may cover plus the word its theme prefers;
        // never a word an older theme prefers (mountain, sand, lush ...), or that theme would switch to it.
        Ground(catalog, "Snow", 10f, new Color(0.96f, 0.98f, 1f), 0.4f, "native", "scrub", "woods", "snow", "winter");
        Ground(catalog, "AutumnLeaves", 4f, new Color(0.86f, 0.72f, 0.56f), 0.1f, "native", "woods", "autumn");
        Ground(catalog, "RedRock", 16f, new Color(1f, 0.86f, 0.78f), 0.15f, "native", "canyon");
        Ground(catalog, "RedDirt", 7f, Color.white, 0.1f, "scrub", "woods", "canyon");
        Ground(catalog, "WhiteSand", 6f, new Color(1f, 0.98f, 0.94f), 0.15f, "bunker", "tropical");
        Ground(catalog, "JungleMoss", 7f, new Color(0.55f, 0.8f, 0.42f), 0.1f, "native", "scrub", "tropical");

        // Tinted copies of existing layers: frosty and golden rough, red canyon sand.
        Tinted(catalog, "Golf_Rough", "Golf_RoughFrost", new Color(0.8f, 0.86f, 0.86f), "rough", "winter");
        Tinted(catalog, "Golf_Rough", "Golf_RoughAutumn", new Color(0.82f, 0.74f, 0.44f), "rough", "autumn");
        Tinted(catalog, "Golf_Bunker", "Golf_BunkerCanyon", new Color(1f, 0.66f, 0.46f), "bunker", "canyon");

        // The heath layer that ships with the terrain demo, as heathland's native ground.
        Register(catalog, "Assets/TerrainDemoScene_URP/Terrain/Layers/Heather_A.terrainlayer", AssetCategory.GroundLayer,
                 "native", "scrub", "heather", "heathland");

        // Broadleaf trees (deciduous borrowed pines until now): the demo tree's lime leaves calmed down, plus autumn
        // colours. The leaf texture is green with almost no blue, so autumn tints push red well above 1 (gamma).
        var demo = AssetDatabase.LoadAssetAtPath<GameObject>(Broadleaf);
        catalog.entries.RemoveAll(e => !e.asset || e.asset == demo); // stale entries, and the raw demo tree
        Tree(catalog, "Broadleaf", new Color(0.8f, 0.86f, 0.7f), "deciduous", "parkland", "lakes");
        Tree(catalog, "BroadleafOrange", new Color(2.3f, 0.85f, 1f), "deciduous", "autumn");
        Tree(catalog, "BroadleafRed", new Color(2.4f, 0.55f, 1f), "deciduous", "autumn");
        Tree(catalog, "BroadleafGold", new Color(2.1f, 1.05f, 1f), "deciduous", "autumn");
        Register(catalog, "Assets/PinwheelStudio/Vista/Personal/Samples/Prefabs/VistaSample_PalmTree.prefab", AssetCategory.Tree, "palm", "tropical");
        Register(catalog, "Assets/TerrainDemoScene_URP/Prefabs/Details/Heather_A.prefab", AssetCategory.GroundCover, "heather", "links", "heathland");
        Register(catalog, "Assets/TerrainDemoScene_URP/Prefabs/Details/Bush_Red.prefab", AssetCategory.GroundCover, "bush", "autumn");
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();

        CatalogMenu.BuildFromCurrentAssets(); // creates missing theme assets and lists them in the catalog
        foreach (var name in Scenic) Rewrite(name); // ours: always as DefaultThemes says now
        foreach (var theme in catalog.themes.Where(t => t))
            if (DefaultThemes.KeepToOwnSeason(theme)) EditorUtility.SetDirty(theme); // the older themes never get autumn trees
        AssetDatabase.SaveAssets();

        var library = RuntimeThemeBaker.Bake();
        var round = AssetDatabase.LoadAssetAtPath<GolfSim.Game.CourseRound>(RoundPath);
        if (round && round.themes != library) // in case the bake ever saves a new asset (GUID)
        {
            round.themes = library;
            EditorUtility.SetDirty(round);
            AssetDatabase.SaveAssets();
        }
        return $"catalog {catalog.entries.Count} entries, {catalog.themes.Count} themes; baked {library.themes.Count} runtime themes: " +
               string.Join(", ", library.themes.Select(t => t.name));
    }

    static void Ground(AssetCatalog catalog, string name, float tile, Color tint, float smoothness, params string[] tags)
    {
        var layer = Layer($"Golf_{name}", l =>
        {
            l.diffuseTexture = Tex($"{name}/{name}_Color.jpg", TextureImporterType.Default, true);
            l.normalMapTexture = Tex($"{name}/{name}_Normal.jpg", TextureImporterType.NormalMap, false);
            l.maskMapTexture = Tex($"{name}/{name}_MaskMap.png", TextureImporterType.Default, false);
            l.tileSize = Vector2.one * tile;
            l.normalScale = 0.6f;
            l.diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 1f);
            l.maskMapRemapMax = new Vector4(1f, 1f, 1f, smoothness);
        });
        catalog.Register(layer, AssetCategory.GroundLayer, tags);
    }

    static void Tinted(AssetCatalog catalog, string source, string name, Color tint, params string[] tags)
    {
        var from = AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{LayerDir}{source}.terrainlayer");
        var layer = Layer(name, l =>
        {
            EditorUtility.CopySerialized(from, l);
            l.name = name;
            l.diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 1f);
        });
        catalog.Register(layer, AssetCategory.GroundLayer, tags);
    }

    /// <summary>The layer asset at Layers/name, created if missing, set up in place (its GUID stays).</summary>
    static TerrainLayer Layer(string name, Action<TerrainLayer> setup)
    {
        string path = $"{LayerDir}{name}.terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (!layer)
        {
            layer = new TerrainLayer();
            AssetDatabase.CreateAsset(layer, path);
        }
        setup(layer);
        EditorUtility.SetDirty(layer);
        return layer;
    }

    /// <summary>A prefab variant of the demo broadleaf with its leaves tinted (the material saved next to it).</summary>
    static void Tree(AssetCatalog catalog, string name, Color leafTint, params string[] tags)
    {
        GeneratedAssets.EnsureFolder(TreeDir.TrimEnd('/'));
        var leaves = AssetDatabase.LoadAssetAtPath<Material>(BroadleafLeaves);
        string matPath = $"{TreeDir}{name}_Leaves.mat";
        var tinted = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (!tinted)
        {
            tinted = new Material(leaves);
            AssetDatabase.CreateAsset(tinted, matPath);
        }
        tinted.CopyPropertiesFromMaterial(leaves);
        tinted.SetColor("_BaseColor", leafTint);
        EditorUtility.SetDirty(tinted);

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Broadleaf));
        try
        {
            foreach (var r in instance.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = r.sharedMaterials.Select(m => m == leaves ? tinted : m).ToArray();
            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, $"{TreeDir}{name}.prefab"); // an instance saves as a variant
            catalog.Register(prefab, AssetCategory.Tree, tags, AutoTagger.Scale(AssetCategory.Tree));
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    /// <summary>Overwrites a scenic theme asset with DefaultThemes' current spec (in place: the catalog keeps pointing at it).</summary>
    static void Rewrite(string name)
    {
        var theme = AssetDatabase.LoadAssetAtPath<CourseTheme>($"{ThemeDir}{name}.asset");
        if (!theme) return;
        var fresh = DefaultThemes.Create(name);
        EditorUtility.CopySerialized(fresh, theme);
        theme.name = name;
        Object.DestroyImmediate(fresh);
        EditorUtility.SetDirty(theme);
    }

    static void Register(AssetCatalog catalog, string path, AssetCategory category, params string[] tags)
    {
        var asset = AssetDatabase.LoadMainAssetAtPath(path) ?? throw new Exception($"Missing {path}");
        catalog.Register(asset, category, tags, category == AssetCategory.GroundLayer ? (Vector2?)null : AutoTagger.Scale(category));
    }

    static Texture2D Tex(string file, TextureImporterType type, bool srgb)
    {
        string path = TexDir + file;
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        if (imp.textureType != type || imp.sRGBTexture != srgb)
        {
            imp.textureType = type;
            imp.sRGBTexture = srgb;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
