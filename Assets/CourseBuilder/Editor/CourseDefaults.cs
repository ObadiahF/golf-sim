using GolfSim.Course;

namespace GolfSim.CourseEditor
{
    /// <summary>The project's default surface layers and scatter set (created on first use).</summary>
    public static class CourseDefaults
    {
        public static SurfaceLayerSet Layers() =>
            GeneratedAssets.LoadOrCreate(SurfaceLayerSet.DefaultPath, UnityEngine.ScriptableObject.CreateInstance<SurfaceLayerSet>);

        public static ScatterSet Scatter() =>
            GeneratedAssets.LoadOrCreate(ScatterSet.DefaultPath, UnityEngine.ScriptableObject.CreateInstance<ScatterSet>);
    }
}
