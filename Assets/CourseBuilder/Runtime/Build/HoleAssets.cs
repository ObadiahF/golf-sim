using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace GolfSim.Course
{
    /// <summary>
    /// Where a hole build puts the meshes, materials and terrain data it creates. In the game everything
    /// stays in memory (this class); the editor subclass saves them as assets so built scenes keep them.
    /// </summary>
    public class HoleAssets
    {
        static readonly Dictionary<string, Object> SharedCache = new Dictionary<string, Object>();

        /// <summary>A per-hole object (terrain data, cup or water mesh). `file` is its name inside the package folder.</summary>
        public virtual T PerHole<T>(string file, T asset) where T : Object => asset;

        /// <summary>An object shared by every hole (marker materials, the flag mesh), created once.</summary>
        public virtual T Shared<T>(string key, Func<T> create) where T : Object
        {
            if (SharedCache.TryGetValue(key, out var existing) && existing) return (T)existing;
            var made = create();
            made.hideFlags |= HideFlags.DontSave;
            SharedCache[key] = made;
            return made;
        }

        public Material ColorMaterial(string name, Color color, bool doubleSided = false) =>
            Shared($"Materials/{name}.mat", () =>
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
                    { name = name, color = color }; // .color targets the shader's [MainColor]
                if (doubleSided)
                {
                    mat.SetFloat("_Cull", 0f); // URP Lit: render both faces
                    mat.doubleSidedGI = true;
                }
                return mat;
            });

        public Material ShaderMaterial(string name, string shaderName) =>
            Shared($"Materials/{name}.mat", () => new Material(Shader.Find(shaderName)) { name = name });

        /// <summary>Transparent glossy URP Lit water, used when a theme has no water material.</summary>
        public Material TransparentMaterial(string name, Color color) =>
            Shared($"Materials/{name}.mat", () =>
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };
                mat.SetFloat("_Surface", 1f); // transparent
                mat.SetFloat("_Blend", 0f);   // alpha
                mat.SetFloat("_Smoothness", 0.95f);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                mat.renderQueue = (int)RenderQueue.Transparent;
                return mat;
            });

        /// <summary>Flat-colour terrain layer for a surface without a real one (slight noise so slopes still read).</summary>
        public virtual TerrainLayer PlaceholderLayer(string surface, Color color, float tileSize) =>
            Shared($"Placeholders/{surface}_{ColorUtility.ToHtmlStringRGB(color)}.terrainlayer", () => new TerrainLayer
            {
                diffuseTexture = NoiseTexture(color, surface.GetHashCode()),
                tileSize = Vector2.one * tileSize,
            });

        protected static Texture2D NoiseTexture(Color color, int seed)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
            var rng = new System.Random(seed);
            var pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                float k = 0.92f + 0.16f * (float)rng.NextDouble();
                pixels[i] = new Color(color.r * k, color.g * k, color.b * k, 1f);
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
