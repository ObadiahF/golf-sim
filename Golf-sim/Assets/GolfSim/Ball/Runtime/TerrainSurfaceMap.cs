using System.Linq;
using GolfSim.Course;
using UnityEngine;

namespace GolfSim.Ball
{
    /// <summary>
    /// Fast "which surface is here?" lookup for a hole's terrain: the dominant painted layer per
    /// alphamap texel, cached once as bytes so the ball can query it every physics step.
    /// </summary>
    public class TerrainSurfaceMap
    {
        static readonly string[] KnownSurfaces = { "green", "fairway", "tee", "rough", "bunker", "water", "native", "scrub", "woods" };

        readonly Terrain terrain;
        readonly byte[] dominant;
        readonly string[] layerSurfaces;
        readonly int res;

        public Terrain Terrain => terrain;

        public TerrainSurfaceMap(Terrain terrain, HoleInfo hole)
        {
            this.terrain = terrain;
            var data = terrain.terrainData;
            res = data.alphamapResolution;
            layerSurfaces = hole && hole.terrainLayerSurfaces != null && hole.terrainLayerSurfaces.Length == data.alphamapLayers
                ? hole.terrainLayerSurfaces
                : data.terrainLayers.Select(GuessSurface).ToArray(); // holes generated before surfaces were recorded

            var alpha = data.GetAlphamaps(0, 0, res, res);
            int layers = alpha.GetLength(2);
            dominant = new byte[res * res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    int best = 0;
                    for (int k = 1; k < layers; k++)
                        if (alpha[z, x, k] > alpha[z, x, best]) best = k;
                    dominant[z * res + x] = (byte)best;
                }
        }

        public string SurfaceAt(Vector3 world)
        {
            var local = world - terrain.transform.position;
            var size = terrain.terrainData.size;
            int x = Mathf.Clamp((int)(local.x / size.x * res), 0, res - 1);
            int z = Mathf.Clamp((int)(local.z / size.z * res), 0, res - 1);
            return layerSurfaces[dominant[z * res + x]];
        }

        public float HeightAt(Vector3 world) => terrain.SampleHeight(world) + terrain.transform.position.y;

        public Vector3 NormalAt(Vector3 world)
        {
            var local = world - terrain.transform.position;
            var size = terrain.terrainData.size;
            return terrain.terrainData.GetInterpolatedNormal(local.x / size.x, local.z / size.z);
        }

        public bool Contains(Vector3 world)
        {
            var local = world - terrain.transform.position;
            var size = terrain.terrainData.size;
            return local.x >= 0f && local.z >= 0f && local.x <= size.x && local.z <= size.z;
        }

        static string GuessSurface(TerrainLayer layer)
        {
            string n = layer ? layer.name.ToLowerInvariant() : "";
            return KnownSurfaces.FirstOrDefault(n.Contains) ?? "rough";
        }
    }
}
