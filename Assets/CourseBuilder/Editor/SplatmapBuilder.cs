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

            return Compose(masks, resolution, layerCount);
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
