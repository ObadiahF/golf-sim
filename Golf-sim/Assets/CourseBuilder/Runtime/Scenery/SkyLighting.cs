using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GolfSim.Course
{
    /// <summary>
    /// Relights the hole scene for a time of day (SkyPreset) in a theme's air (ThemeScenery): turns the scene's sun
    /// to its place relative to the line of play, swaps the sky, sets ambient light and fog, and raises the bloom
    /// after dark so glowing things glow. Everything it changes is per scene (RenderSettings, the Sun), so the next
    /// hole's scene load starts from the day it was built with.
    /// </summary>
    public static class SkyLighting
    {
        const int VolumePriority = 10; // over the scene's global post-processing volume (priority 0)
        const string Ours = " (Scenery)"; // name suffix of the skies made here

        /// <summary>The hole scene's own sky (Sky.mat): what every procedural sky is copied from.</summary>
        static Material sceneSky;

        public static void Apply(SceneryRig rig, SkyPreset preset, ThemeScenery theme, Vector3 lineOfPlay)
        {
            var forward = Vector3.ProjectOnPlane(lineOfPlay, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();
            var toSun = Direction(forward, preset.azimuth, preset.elevation);

            var sun = FindSun();
            if (sun)
            {
                sun.transform.rotation = Quaternion.LookRotation(-toSun);
                sun.color = preset.lightColor;
                sun.intensity = preset.intensity;
                sun.shadowStrength = preset.shadowStrength;
                RenderSettings.sun = sun;
            }

            RenderSettings.skybox = Sky(rig, preset, forward, toSun);
            if (preset.skyAmbient) RenderSettings.ambientMode = AmbientMode.Skybox;
            else
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = preset.ambientSky;
                RenderSettings.ambientEquatorColor = preset.ambientEquator;
                RenderSettings.ambientGroundColor = preset.ambientGround;
            }
            RenderSettings.reflectionIntensity = preset.reflections;
            RenderSettings.fog = true;
            RenderSettings.fogColor = preset.fog * theme.fogTint;
            RenderSettings.fogDensity = preset.fogDensity * theme.haze;

            if (preset.time != TimeOfDay.Day) Post(rig, preset);
        }

        /// <summary>A flat heading `azimuth` degrees clockwise from `forward`, tilted up by `elevation`.</summary>
        public static Vector3 Direction(Vector3 forward, float azimuth, float elevation) =>
            Quaternion.AngleAxis(azimuth, Vector3.up) * Quaternion.AngleAxis(-elevation, Vector3.Cross(Vector3.up, forward)) * forward;

        /// <summary>The scene's directional light: RenderSettings.sun, else the first directional light.</summary>
        public static Light FindSun()
        {
            if (RenderSettings.sun) return RenderSettings.sun;
            foreach (var light in Object.FindObjectsByType<Light>())
                if (light.type == LightType.Directional) return light;
            return null;
        }

        static Material Sky(SceneryRig rig, SkyPreset preset, Vector3 forward, Vector3 toSun)
        {
            var current = RenderSettings.skybox;
            if (current && !current.name.EndsWith(Ours)) sceneSky = current; // a fresh scene's own sky, not one of ours
            if (preset.time == TimeOfDay.Day || !preset.proceduralSky && !RuntimeMaterials.Template(m => m.nightSky))
                return sceneSky; // day as built (or no night sky in this build: better a dim day sky than none)

            if (preset.proceduralSky)
            {
                if (!sceneSky) return null;
                var sky = rig.Own(new Material(sceneSky) { name = sceneSky.name + Ours });
                sky.SetFloat("_Exposure", preset.skyExposure);
                sky.SetFloat("_AtmosphereThickness", preset.atmosphere);
                sky.SetFloat("_SunSize", preset.sunSize);
                sky.SetColor("_SkyTint", preset.skyTint);
                return sky;
            }

            var night = rig.Own(RuntimeMaterials.Create(m => m.nightSky, "Night Sky" + Ours));
            night.SetColor("_ZenithColor", preset.zenith);
            night.SetColor("_HorizonColor", preset.horizon);
            night.SetColor("_GroundColor", preset.horizon * 0.35f);
            night.SetColor("_GlowColor", preset.afterglow);
            night.SetVector("_GlowDirection", toSun);
            night.SetFloat("_StarBrightness", preset.stars);
            night.SetColor("_MoonColor", preset.moon);
            night.SetVector("_MoonDirection", Direction(forward, preset.moonAzimuth, preset.moonElevation));
            return night;
        }

        /// <summary>A global volume over the scene's: stronger bloom and a little more exposure as it gets dark.</summary>
        static void Post(SceneryRig rig, SkyPreset preset)
        {
            var profile = rig.Own(ScriptableObject.CreateInstance<VolumeProfile>());
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(preset.bloom);
            bloom.threshold.Override(preset.bloomThreshold);
            profile.Add<ColorAdjustments>().postExposure.Override(preset.exposure);

            var volume = rig.Child("Post Processing").gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = VolumePriority;
            volume.sharedProfile = profile;
        }
    }
}
