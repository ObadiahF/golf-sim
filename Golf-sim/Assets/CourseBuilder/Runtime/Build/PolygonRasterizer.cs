using System.Collections.Generic;
using UnityEngine;

namespace GolfSim.Course
{
    /// <summary>Scanline polygon fill and blur for square masks indexed [z, x].</summary>
    public static class PolygonRasterizer
    {
        /// <summary>
        /// Sets mask cells whose centres fall inside the rings to 1. Rings are combined with the
        /// even-odd rule, so inner rings (holes) cut out of the outer ring.
        /// </summary>
        public static void Fill(float[,] mask, IReadOnlyList<PointList> rings, float cellSize)
        {
            int res = mask.GetLength(0);
            if (!Bounds(rings, out float minZ, out float maxZ)) return;

            int rowStart = Mathf.Max(0, Mathf.CeilToInt(minZ / cellSize - 0.5f));
            int rowEnd = Mathf.Min(res - 1, Mathf.FloorToInt(maxZ / cellSize - 0.5f));
            var crossings = new List<float>();

            for (int row = rowStart; row <= rowEnd; row++)
            {
                float z = (row + 0.5f) * cellSize;
                crossings.Clear();
                foreach (var ring in rings) AddCrossings(ring, z, crossings);
                crossings.Sort();

                for (int k = 0; k + 1 < crossings.Count; k += 2)
                {
                    int c0 = Mathf.Max(0, Mathf.CeilToInt(crossings[k] / cellSize - 0.5f));
                    int c1 = Mathf.Min(res - 1, Mathf.FloorToInt(crossings[k + 1] / cellSize - 0.5f));
                    for (int col = c0; col <= c1; col++) mask[row, col] = 1f;
                }
            }
        }

        static void AddCrossings(PointList ring, float z, List<float> crossings)
        {
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = ring[i], b = ring[(i + 1) % n];
                if ((a.y > z) != (b.y > z))
                    crossings.Add(a.x + (z - a.y) * (b.x - a.x) / (b.y - a.y));
            }
        }

        static bool Bounds(IReadOnlyList<PointList> rings, out float minZ, out float maxZ)
        {
            minZ = float.MaxValue;
            maxZ = float.MinValue;
            foreach (var ring in rings)
                for (int i = 0; i < ring.Count; i++)
                {
                    minZ = Mathf.Min(minZ, ring[i].y);
                    maxZ = Mathf.Max(maxZ, ring[i].y);
                }
            return minZ <= maxZ;
        }

        /// <summary>Separable box blur, softening hard mask edges into blend zones.</summary>
        public static void Blur(float[,] mask, int radius)
        {
            if (radius <= 0) return;
            int res = mask.GetLength(0);
            var line = new float[res];
            for (int pass = 0; pass < 2; pass++)
            {
                bool rows = pass == 0;
                for (int a = 0; a < res; a++)
                {
                    for (int b = 0; b < res; b++) line[b] = rows ? mask[a, b] : mask[b, a];
                    float sum = 0;
                    int count = 0;
                    for (int b = -radius; b < res + radius; b++)
                    {
                        int add = b + radius, drop = b - radius - 1;
                        if (add < res) { sum += line[add]; count++; }
                        if (drop >= 0) { sum -= line[drop]; count--; }
                        if (b < 0 || b >= res) continue;
                        if (rows) mask[a, b] = sum / count; else mask[b, a] = sum / count;
                    }
                }
            }
        }
    }
}
