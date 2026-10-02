using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>Procedural textures for the menus (USS has no gradients).</summary>
    public static class MenuArt
    {
        static Texture2D shade;

        /// <summary>
        /// Darkens the left side and the bottom of the backdrop so the title, text and cards stay readable
        /// over any mode art: alpha = max(left ramp, bottom ramp) on the menu background color.
        /// </summary>
        public static Texture2D Shade(Color color)
        {
            if (shade) return shade;
            const int size = 128;
            shade = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (size - 1f), v = y / (size - 1f); // v = 0 at the bottom
                float left = Mathf.SmoothStep(0.85f, 0.15f, u);
                float bottom = Mathf.SmoothStep(0.98f, 0.0f, v * 1.8f);
                float top = Mathf.SmoothStep(0f, 0.45f, (v - 0.8f) * 5f);
                pixels[y * size + x] = new Color(color.r, color.g, color.b, Mathf.Clamp01(Mathf.Max(left, bottom, top) + 0.15f));
            }
            shade.SetPixels(pixels);
            shade.Apply();
            return shade;
        }
    }
}
