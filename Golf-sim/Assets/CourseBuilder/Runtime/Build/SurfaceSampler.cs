using System.Linq;
using UnityEngine;

namespace GolfSim.Course
{
    /// <summary>Answers "what surface is here?" and "is this too close to the playing area?" from painted alphamaps.</summary>
    public class SurfaceSampler
    {
        const float KeepClearThreshold = 0.05f;

        readonly float[,,] alpha;
        readonly string[] surfaces;
        readonly float[,] blocked; // > 0 within clearMargin of a keep-clear surface
        readonly int res;
        readonly float cellSize;

        public SurfaceSampler(float[,,] alpha, SurfaceLayerSet layers, float sizeMeters, string[] keepClear, float clearMargin)
        {
            this.alpha = alpha;
            surfaces = layers.SurfaceNames();
            res = alpha.GetLength(0);
            cellSize = sizeMeters / res;

            // By name, so stripe layers count as part of their surface.
            var clearLayers = Enumerable.Range(0, surfaces.Length).Where(i => keepClear.Contains(surfaces[i])).ToArray();
            blocked = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    foreach (int k in clearLayers)
                        if (alpha[z, x, k] > KeepClearThreshold) { blocked[z, x] = 1f; break; }
            // A box blur spreads non-zero values exactly `radius` cells: a cheap square dilation.
            PolygonRasterizer.Blur(blocked, Mathf.CeilToInt(clearMargin / cellSize));
        }

        public string DominantSurface(Vector2 pos)
        {
            Cell(pos, out int z, out int x);
            int best = 0;
            for (int k = 1; k < surfaces.Length; k++)
                if (alpha[z, x, k] > alpha[z, x, best]) best = k;
            return surfaces[best];
        }

        public bool NearKeepClear(Vector2 pos)
        {
            Cell(pos, out int z, out int x);
            return blocked[z, x] > 0f;
        }

        void Cell(Vector2 pos, out int z, out int x)
        {
            x = Mathf.Clamp((int)(pos.x / cellSize), 0, res - 1);
            z = Mathf.Clamp((int)(pos.y / cellSize), 0, res - 1);
        }
    }
}
