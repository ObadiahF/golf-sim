// Dev helper, run from the shell with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/SetupEnvironmentLighting.cs --entry SetupEnvironmentLighting.Run
// Gives the open scene a LightingSettings asset with no lightmaps (everything is realtime) and
// starts a bake, which only generates the sky ambient probe and reflection cubemap. Without that
// bake the ambient light is zero and every shadowed surface renders black.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SetupEnvironmentLighting
{
    const string SettingsPath = "Assets/Settings/URP/EnvironmentOnlyLighting.lighting";

    public static string Run()
    {
        var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(SettingsPath);
        if (!settings)
        {
            settings = new LightingSettings { name = "EnvironmentOnlyLighting", bakedGI = false, realtimeGI = false };
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }
        Lightmapping.lightingSettings = settings;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = 1f;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        bool started = Lightmapping.BakeAsync();
        return started ? "environment bake started" : "bake failed to start";
    }

    public static string Status()
    {
        var p = RenderSettings.ambientProbe;
        return $"running={Lightmapping.isRunning} ambientL0=({p[0, 0]:0.000}, {p[1, 0]:0.000}, {p[2, 0]:0.000})";
    }
}
