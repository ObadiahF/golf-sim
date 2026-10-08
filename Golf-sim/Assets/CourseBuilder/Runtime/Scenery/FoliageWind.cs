using UnityEngine;
using UnityEngine.SceneManagement;

namespace GolfSim.Course
{
    /// <summary>
    /// Makes the trees move with the hole's wind: the SpeedTrees (pines, conifers, cypresses) through a directional
    /// WindZone in the hole's scene, the broadleaves and palms (GolfSim/Foliage) through the _GolfWind global their
    /// shader reads (FoliageSway.hlsl). Calm air still stirs them a little. The round sets it with each hole's wind.
    /// </summary>
    public static class FoliageWind
    {
        const string ZoneName = "Foliage Wind";
        const float StrongMph = 22f; // the top of the wind bands: full strength
        static readonly int GolfWind = Shader.PropertyToID("_GolfWind");

        /// <summary>The wind blowing toward `heading` (degrees clockwise from +z) at `mph`.</summary>
        public static void Apply(float heading, float mph)
        {
            float strength = Mathf.Clamp01(mph / StrongMph);
            var toward = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
            Shader.SetGlobalVector(GolfWind, new Vector4(toward.x, toward.z, strength, 0f));

            var zone = Zone();
            zone.transform.rotation = Quaternion.Euler(0f, heading, 0f);
            zone.windMain = Mathf.Lerp(0.15f, 1.1f, strength);
            zone.windTurbulence = Mathf.Lerp(0.15f, 0.8f, strength);
            zone.windPulseMagnitude = Mathf.Lerp(0.3f, 0.9f, strength);
            zone.windPulseFrequency = Mathf.Lerp(0.08f, 0.25f, strength);
        }

        /// <summary>The active scene's wind zone, made the first time (it goes with the scene).</summary>
        static WindZone Zone()
        {
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == ZoneName && root.TryGetComponent<WindZone>(out var found)) return found;
            var go = new GameObject(ZoneName);
            SceneManager.MoveGameObjectToScene(go, scene);
            var zone = go.AddComponent<WindZone>();
            zone.mode = WindZoneMode.Directional;
            return zone;
        }
    }
}
