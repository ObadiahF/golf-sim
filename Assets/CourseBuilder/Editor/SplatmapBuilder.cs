using System.Collections.Generic;
using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>Turns hole areas into terrain alphamaps using the layer set's paint priority.</summary>
    public static class SplatmapBuilder
    {
        /// <param name="unknownSurfaces">Receives surface names in the package that the layer set doesn't know.</param>
        public static float[,,] Build(HolePackage pkg, SurfaceLayerSet layers, int resolution, int blurRadius,
                                      ISet<string> unknownSurfaces)
        {
            int layerCount = layers.entries.Count;
            float cellSize = pkg.sizeMeters / resolution;
            var masks = new float[layerCount][,];

            foreach (var area in pkg.areas)
            {
                int index = layers.IndexOf(area.surface);
                if (index < 0) { unknownSurfaces.Add(area.surface); continue; }
                if (index == 0) continue; // base layer fills whatever is left anyway
                masks[index] ??= new float[resolution, resolution];
                PolygonRasterizer.Fill(masks[index], area.rings, cellSize);
            }

            foreach (var mask in masks)
                if (mask != null) PolygonRasterizer.Blur(mask, blurRadius);

            for (int k = 0; k < layerCount; k++)
            {
                var entry = layers.entries[k];
                var parent = entry.IsStripe ? masks[layers.IndexOf(SurfaceLayerSet.BaseSurface(entry.surface))] : null;
                if (parent != null) masks[k] = Stripes(parent, entry, pkg, cellSize);
            }

            return Compose(masks, resolution, layerCount);
        }

        /// <summary>
        /// Mowing stripes: the parent surface's coverage, kept only in alternating bands aligned
        /// to the line of play. Composed above the parent, so the stripe layer shows in the bands.
        /// </summary>
        static float[,] Stripes(float[,] parent, SurfaceLayerSet.Entry entry, HolePackage pkg, float cellSize)
        {
            int res = parent.GetLength(0);
            Vector2 play = (pkg.pin - pkg.tee).sqrMagnitude > 1f ? (pkg.pin - pkg.tee).normalized : Vector2.up;
            Vector2 along = Quaternion.Euler(0f, 0f, -entry.stripeAngle) * play;
            Vector2 across = new Vector2(-along.y, along.x); // bands alternate across their running direction
            var mask = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    if (parent[z, x] <= 0f) continue;
                    var pos = new Vector2((x + 0.5f) * cellSize, (z + 0.5f) * cellSize) - pkg.tee;
                    // Sharpened sine: flat bands with a soft ~20% transition, like light catching mown grass.
                    float band = Mathf.Clamp01(0.5f + 2f * Mathf.Sin(Mathf.PI * Vector2.Dot(pos, across) / entry.stripeWidth));
                    mask[z, x] = parent[z, x] * band;
                }
            return mask;
        }

        /// <summary>Highest-priority layer takes its coverage first; lower layers share what remains.</summary>
        static float[,,] Compose(float[][,] masks, int resolution, int layerCount)
        {
            var alpha = new float[resolution, resolution, layerCount];
            for (int z = 0; z < resolution; z++)
                for (int x = 0; x < resolution; x++)
                {
                    float remaining = 1f;
                    for (int k = layerCount - 1; k > 0 && remaining > 0f; k--)
                    {
                        if (masks[k] == null) continue;
                        float w = masks[k][z, x] * remaining;
                        alpha[z, x, k] = w;
                        remaining -= w;
                    }
                    alpha[z, x, 0] = remaining;
                }
            return alpha;
        }
    }
}
