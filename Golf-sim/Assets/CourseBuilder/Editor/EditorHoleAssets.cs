using System;
using System.IO;
using GolfSim.Course;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GolfSim.CourseEditor
{
    /// <summary>Saves what a hole build creates as assets: per-hole objects next to the package, shared ones under Generated/.</summary>
    public class EditorHoleAssets : HoleAssets
    {
        readonly string packageFolder;

        public EditorHoleAssets(string packageFolder) => this.packageFolder = packageFolder;

        public override T PerHole<T>(string file, T asset) => GeneratedAssets.SaveFresh($"{packageFolder}/{file}", asset);

        public override T Shared<T>(string key, Func<T> create) => GeneratedAssets.LoadOrCreate($"{GeneratedAssets.Root}/{key}", create);

        public override TerrainLayer PlaceholderLayer(string surface, Color color, float tileSize)
        {
            string key = $"Placeholders/{surface}_{ColorUtility.ToHtmlStringRGB(color)}";
            return Shared($"{key}.terrainlayer", () => new TerrainLayer
            {
                diffuseTexture = SaveTexture($"{GeneratedAssets.Root}/{key}.png", NoiseTexture(color, key.GetHashCode())),
                tileSize = Vector2.one * tileSize,
            });
        }

        static Texture2D SaveTexture(string assetPath, Texture2D tex)
        {
            GeneratedAssets.EnsureFolder(Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
            File.WriteAllBytes(assetPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }
    }
}
