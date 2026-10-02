using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>
    /// Builds the water bodies of a hole. Swap the implementation per theme to change water tech:
    /// the built-in MeshWaterProvider draws flat material meshes; a provider for a third-party water
    /// system (e.g. a GPU water package) only needs to subclass this and create its own objects.
    /// </summary>
    public abstract class WaterProvider : ScriptableObject
    {
        /// <param name="parent">The hole root; place water in its local space (x east, z north, meters).</param>
        /// <param name="pkg">Each pkg.water body has a surface level and a triangulated outline.</param>
        public abstract void Build(Transform parent, HolePackage pkg);
    }
}
