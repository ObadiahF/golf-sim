using System.Linq;
using UnityEngine;

namespace GolfSim.Course
{
    /// <summary>Answers "what surface is here?" and "is this too close to the playing area?" from painted alphamaps.</summary>
    public class SurfaceSampler
    {
        const float KeepClearThreshold = 0.05f;
        // The running-sum blur leaves float residue (~1e-7) along every row and column it passed a keep-clear cell in:
        // counted as "near", it cut bare lines through the ground cover far from any fairway.
        const float NearThreshold = 1e-3f;

        readonly float[,,] alpha;
        readonly string[] surfaces;
        readonly float[,] blocked; // > 0 within clearMargin of a keep-clear surface
        readonly float[,] near;    // share of keep-clear ground within fadeMeters (null without a fade)
        readonly int res;
        readonly float cellSize;

        public SurfaceSampler(float[,,] alpha, SurfaceLayerSet layers, float sizeMeters, string[] keepClear, float clearMargin,
                              float fadeMeters = 0f)
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
            if (fadeMeters > 0f)
            {
                // Blurred wider, the playing area becomes a ramp that falls to 0 fadeMeters out from its edge.
                near = (float[,])blocked.Clone();
                PolygonRasterizer.Blur(near, Mathf.CeilToInt(fadeMeters / cellSize));
            }
            // A box blur spreads non-zero values exactly `radius` cells: a cheap square dilation.
            PolygonRasterizer.Blur(blocked, Mathf.CeilToInt(clearMargin / cellSize));
        }

        /// <summary>
        /// 0 right at the edge of a keep-clear surface rising to 1 fadeMeters away (1 everywhere without a fade): ground
        /// cover thins out toward the fairway instead of stopping at a line.
        /// </summary>
        public float EdgeFade(Vector2 pos)
        {
            if (near == null) return 1f;
            Cell(pos, out int z, out int x);
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(1f - near[z, x] * 2f));
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
            return blocked[z, x] > NearThreshold;
        }

        void Cell(Vector2 pos, out int z, out int x)
        {
            x = Mathf.Clamp((int)(pos.x / cellSize), 0, res - 1);
            z = Mathf.Clamp((int)(pos.y / cellSize), 0, res - 1);
        }
    }
}
