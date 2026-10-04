using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GolfSim.Course
{
    /// <summary>
    /// Relights the hole scene for a time of day (SkyPreset) in a theme's air (ThemeScenery): turns the scene's sun
    /// to its place relative to the line of play, swaps the sky, sets ambient light and fog, renders the new sky into a
    /// reflection probe (the water mirrors it), tells the water where its glint comes from (the sun, or the moon), and
    /// grades the colour. Everything it changes is per scene (RenderSettings, the Sun, the rig's objects), so the next
    /// hole's scene load starts from the day it was built with.
    /// </summary>
    public static class SkyLighting
    {
        const int VolumePriority = 10;    // over the scene's global post-processing volume (priority 0)
        const string Ours = " (Scenery)"; // name suffix of the skies made here
        const int ProbeResolution = 256;  // the moon is a few pixels across in it: enough for its path on the water
        const float ProbeSize = 6000f;    // every hole fits inside

        /// <summary>Where the water's sharp highlight comes from (xyz, w = 1 when set) and its HDR colour. Shader globals.</summary>
        public static readonly int GlintDirection = Shader.PropertyToID("_GolfGlintDirection"), GlintColor = Shader.PropertyToID("_GolfGlintColor");

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
                sun.shadows = LightShadows.Soft;
                RenderSettings.sun = sun;
            }

            RenderSettings.skybox = Sky(rig, preset, theme, forward, toSun);
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
            RenderSettings.fogColor = Horizon(preset, theme);
            RenderSettings.fogDensity = preset.fogDensity * theme.haze;

            Glint(preset, forward, toSun);
            if (preset.time == TimeOfDay.Day) return; // the scene's own sky, reflections and grade
            Reflections(rig);
            Post(rig, preset);
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

        /// <summary>The fog colour, and a night sky's horizon: the same colour, so the two meet without a seam.</summary>
        static Color Horizon(SkyPreset preset, ThemeScenery theme) => preset.FogColor * theme.fogTint;

        /// <summary>The water's glint: the sun while it is up, the afterglow on the horizon at dusk, the moon at night.</summary>
        static void Glint(SkyPreset preset, Vector3 forward, Vector3 toSun)
        {
            bool moon = preset.moon.maxColorComponent > 0f;
            var from = moon ? Direction(forward, preset.moonAzimuth, preset.moonElevation)
                     : preset.proceduralSky ? toSun : Direction(forward, preset.azimuth, 2f);
            var color = moon ? preset.moon * 0.6f
                      : preset.proceduralSky ? preset.lightColor * preset.intensity : new Color(1.6f, 0.75f, 0.35f);
            Shader.SetGlobalVector(GlintDirection, new Vector4(from.x, from.y, from.z, 1f));
            Shader.SetGlobalColor(GlintColor, color);
        }

        /// <summary>
        /// A reflection probe holding only the sky, rendered once: the scene's baked reflections are of its day sky, so
        /// without it the ponds would mirror a blue noon sky at midnight.
        /// </summary>
        static void Reflections(SceneryRig rig)
        {
            var probe = rig.Child("Sky Reflection").gameObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.cullingMask = 0; // the sky alone: cheap, and nothing on the course sits in the wrong place in it
            probe.resolution = ProbeResolution;
            probe.hdr = true;
            probe.size = Vector3.one * ProbeSize;
            probe.importance = 100;
            probe.RenderProbe();
        }

        /// <summary>Degrees clockwise from north (+z) of a direction's flat part.</summary>
        static float Heading(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        static Texture Photo(SkyArt art)
        {
            var library = RuntimeMaterials.Load();
            if (!library) return null;
            return art switch { SkyArt.Starry => library.starrySky, SkyArt.Twilight => library.twilightSky, _ => null };
        }

        static Material Sky(SceneryRig rig, SkyPreset preset, ThemeScenery theme, Vector3 forward, Vector3 toSun)
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
            var photo = Photo(preset.art);
            night.SetTexture("_SkyTex", photo);
            night.SetFloat("_SkyTexExposure", photo ? preset.artExposure : 0f);
            night.SetFloat("_SkyTexContrast", preset.artContrast);
            // A twilight photo has its glow at its centre (+z): turn that to the set sun. Others turn with the hole, so
            // the Milky Way crosses the view from the tee the same way on every hole.
            night.SetFloat("_SkyTexRotation", preset.art == SkyArt.Twilight ? Heading(toSun) : Heading(forward) + preset.artTurn);
            night.SetColor("_ZenithColor", preset.zenith);
            night.SetColor("_HorizonColor", Horizon(preset, theme));
            night.SetFloat("_HorizonGlow", preset.horizonGlow);
            night.SetColor("_GlowColor", photo ? Color.black : preset.afterglow);
            night.SetVector("_GlowDirection", toSun);
            night.SetFloat("_StarBrightness", preset.stars);
            night.SetColor("_MoonColor", preset.moon);
            night.SetFloat("_MoonSize", preset.moonSize);
            night.SetVector("_MoonDirection", Direction(forward, preset.moonAzimuth, preset.moonElevation));
            night.SetFloat("_CloudCover", preset.cloudCover);
            night.SetColor("_CloudColor", preset.cloudColor);
            return night;
        }

        /// <summary>
        /// A global volume over the scene's: the bloom (only lights and glowing things pass its threshold), exposure,
        /// white balance (cool moonlight, warm golden hour), contrast and saturation, lifted blacks and a vignette.
        /// </summary>
        static void Post(SceneryRig rig, SkyPreset preset)
        {
            var profile = rig.Own(ScriptableObject.CreateInstance<VolumeProfile>());
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(preset.bloom);
            bloom.threshold.Override(preset.bloomThreshold);
            var grade = profile.Add<ColorAdjustments>();
            grade.postExposure.Override(preset.exposure);
            grade.contrast.Override(preset.contrast);
            grade.saturation.Override(preset.saturation);
            profile.Add<WhiteBalance>().temperature.Override(preset.temperature);
            profile.Add<LiftGammaGain>().lift.Override(preset.lift);
            var vignette = profile.Add<Vignette>();
            vignette.intensity.Override(preset.vignette);
            vignette.smoothness.Override(0.45f);

            var volume = rig.Child("Post Processing").gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = VolumePriority;
            volume.sharedProfile = profile;
        }
    }
}
