using GolfSim.Ball;
using GolfSim.Course;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// What's on the ground where, for presentation (which landing sound, where a camera can stand): the hole's
    /// surface map and terrain heights, cached per terrain.
    /// </summary>
    public static class CourseSurface
    {
        static TerrainSurfaceMap map;

        /// <summary>The surface at this point ("green", "bunker", "rough"...), or "" off the hole.</summary>
        public static string At(Vector3 world)
        {
            var m = Map();
            return m != null && m.Contains(world) ? m.SurfaceAt(world) : "";
        }

        /// <summary>The terrain height at this point (the point's own height off the terrain).</summary>
        public static float GroundAt(Vector3 world)
        {
            var terrain = Terrain.activeTerrain;
            return terrain ? terrain.SampleHeight(world) + terrain.transform.position.y : world.y;
        }

        /// <summary>The height of the water surface over this point (the hole's "Water" meshes), or null if none.</summary>
        public static float? WaterLevelAt(Vector3 world)
        {
            var hole = Object.FindAnyObjectByType<HoleInfo>();
            if (!hole) return null;
            foreach (var r in hole.GetComponentsInChildren<MeshRenderer>())
            {
                var b = r.bounds;
                if (r.name.StartsWith("Water") && world.x >= b.min.x && world.x <= b.max.x && world.z >= b.min.z && world.z <= b.max.z)
                    return b.max.y;
            }
            return null;
        }

        public static bool IsSand(string surface) => surface == "bunker";
        public static bool IsGreen(string surface) => surface == "green";

        static TerrainSurfaceMap Map()
        {
            var hole = Object.FindAnyObjectByType<HoleInfo>();
            var terrain = hole ? hole.GetComponentInChildren<Terrain>() : null;
            if (!terrain) return null;
            if (map == null || map.Terrain != terrain) map = new TerrainSurfaceMap(terrain, hole);
            return map;
        }
    }
}
