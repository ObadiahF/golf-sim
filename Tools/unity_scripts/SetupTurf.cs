// Dev helper, run from the shell with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/SetupTurf.cs --entry SetupTurf.Run
// Builds the manicured-turf TerrainLayers (green, tee, fairway + darker mowing-stripe variants)
// from the CC0 turf textures and assigns them to the default Surface Layer Set.
using UnityEditor;
using UnityEngine;
using GolfSim.CourseEditor;

public static class SetupTurf
{
    const string TexDir = "Assets/CourseBuilder/Textures/Turf/";
    const string LayerDir = "Assets/CourseBuilder/Layers/";
    const float StripeShade = 0.78f; // stripe variant brightness relative to its surface

    public static string Run()
    {
        var color = Tex("Turf_Color.jpg", TextureImporterType.Default, true);
        var normal = Tex("Turf_Normal.jpg", TextureImporterType.NormalMap, false);
        var mask = Tex("Turf_MaskMap.png", TextureImporterType.Default, false);

        // Brightness ladder: green > tee > fairway > rough. Greens are fine-grained and slightly glossy.
        var green = new Color(1.00f, 1.08f, 0.92f);
        var tee = new Color(0.96f, 1.02f, 0.86f);
        var fairway = new Color(0.88f, 0.93f, 0.74f);

        var set = SurfaceLayerSet.LoadOrCreateDefault();
        Assign(set, "green", Layer("Green", color, normal, mask, 1.5f, green, 0.35f), Layer("GreenStripe", color, normal, mask, 1.5f, green * StripeShade, 0.35f), 1f, 0f);
        Assign(set, "tee", Layer("Tee", color, normal, mask, 2f, tee, 0.25f), null, 0f, 0f);
        Assign(set, "fairway", Layer("Fairway", color, normal, mask, 3f, fairway, 0.2f), Layer("FairwayStripe", color, normal, mask, 3f, fairway * StripeShade, 0.2f), 4f, 90f);

        // Rough keeps its clumpy texture but sits clearly below the fairway in brightness.
        var rough = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerDir + "Golf_Rough.terrainlayer");
        rough.diffuseRemapMax = new Vector4(0.66f, 0.74f, 0.52f, 1f);
        EditorUtility.SetDirty(rough);

        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        return "turf layers assigned: green (1 m stripes along play), tee, fairway (4 m stripes across), rough darkened";
    }

    static void Assign(SurfaceLayerSet set, string surface, TerrainLayer layer, TerrainLayer stripe, float width, float angle)
    {
        var e = set.entries[set.IndexOf(surface)];
        e.layer = layer;
        e.stripeLayer = stripe;
        e.stripeWidth = width;
        e.stripeAngle = angle;
    }

    static TerrainLayer Layer(string name, Texture2D color, Texture2D normal, Texture2D mask, float tile, Color tint, float smoothness) =>
        GeneratedAssets.SaveFresh(LayerDir + "Golf_" + name + ".terrainlayer", new TerrainLayer
        {
            diffuseTexture = color,
            normalMapTexture = normal,
            maskMapTexture = mask,
            tileSize = Vector2.one * tile,
            normalScale = 0.5f,
            diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 1f),
            maskMapRemapMax = new Vector4(1f, 1f, 1f, smoothness),
        });

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
