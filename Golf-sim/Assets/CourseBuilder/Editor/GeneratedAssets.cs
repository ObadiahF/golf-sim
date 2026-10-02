using GolfSim.Course;
using System;
using GolfSim.Course;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>Shared helpers for assets the course builder creates on demand.</summary>
    public static class GeneratedAssets
    {
        public const string Root = "Assets/CourseBuilder/Generated";

        public static string EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder)) return assetFolder;
            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetFolder));
            return assetFolder;
        }

        public static T LoadOrCreate<T>(string assetPath, Func<T> create) where T : UnityEngine.Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (existing != null) return existing;

            EnsureFolder(Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
            var asset = create();
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }

        /// <summary>Save a freshly built asset, discarding whatever was at that path before.</summary>
        public static T SaveFresh<T>(string assetPath, T asset) where T : UnityEngine.Object
        {
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null) AssetDatabase.DeleteAsset(assetPath);
            EnsureFolder(Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }

        public static Material ColorMaterial(string name, Color color, bool doubleSided = false) =>
            LoadOrCreate($"{Root}/Materials/{name}.mat", () =>
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var mat = new Material(shader) { color = color }; // .color targets the shader's [MainColor]
                if (doubleSided)
                {
                    mat.SetFloat("_Cull", 0f); // URP Lit: render both faces
                    mat.doubleSidedGI = true;
                }
                return mat;
            });

        public static Material ShaderMaterial(string name, string shaderName) =>
            LoadOrCreate($"{Root}/Materials/{name}.mat", () => new Material(Shader.Find(shaderName)));
    }
}
