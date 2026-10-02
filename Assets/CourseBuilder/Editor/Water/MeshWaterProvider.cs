using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>Default water: one flat mesh per pond with a material (generated fallback if empty).</summary>
    [CreateAssetMenu(menuName = "Golf/Water/Mesh Water Provider", fileName = "MeshWaterProvider")]
    public class MeshWaterProvider : WaterProvider
    {
        public Material material;

        public override void Build(Transform parent, HolePackage pkg) => WaterBuilder.Create(parent, pkg, material);
    }
}
