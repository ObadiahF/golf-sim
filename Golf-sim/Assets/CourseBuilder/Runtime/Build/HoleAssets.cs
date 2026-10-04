using System;
using System.Collections.Generic;
using UnityEngine;
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
                var mat = RuntimeMaterials.Create(m => m.lit, name);
                mat.color = color; // .color targets the shader's [MainColor]
                if (doubleSided) DoubleSided(mat);
                return mat;
            });

        /// <summary>Lit and glowing: `glow` is the HDR emission (bloom picks up anything above ~1).</summary>
        public Material GlowMaterial(string name, Color color, Color glow, bool doubleSided = false) =>
            Shared($"Materials/{name}.mat", () =>
            {
                var mat = Glow(name, color, glow);
                if (doubleSided) DoubleSided(mat);
                return mat;
            });

        /// <summary>URP Lit: render both faces (a cloth flag seen from either side).</summary>
        public static void DoubleSided(Material mat)
        {
            mat.SetFloat("_Cull", 0f);
            mat.doubleSidedGI = true;
        }

        public static bool IsDoubleSided(Material mat) => mat && mat.HasProperty("_Cull") && mat.GetFloat("_Cull") == 0f;

        /// <summary>A new glowing material (not shared): the emissive template with this base colour and HDR emission.</summary>
        public static Material Glow(string name, Color color, Color glow)
        {
            var mat = RuntimeMaterials.Create(m => m.litEmissive, name);
            mat.color = color;
            mat.EnableKeyword("_EMISSION"); // on the template already; kept so an editor fallback still glows
            mat.SetColor("_EmissionColor", glow);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return mat;
        }

        /// <summary>A material from one of the RuntimeMaterials templates (e.g. the cup's shaders).</summary>
        public Material TemplateMaterial(string name, Func<RuntimeMaterials, Material> template) =>
            Shared($"Materials/{name}.mat", () => RuntimeMaterials.Create(template, name));

        /// <summary>Transparent glossy URP Lit water, used when a theme has no water material.</summary>
        public Material TransparentMaterial(string name, Color color) =>
            Shared($"Materials/{name}.mat", () =>
            {
                var mat = RuntimeMaterials.Create(m => m.litTransparent, name);
                mat.color = color;
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
