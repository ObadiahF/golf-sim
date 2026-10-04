using UnityEngine;

namespace GolfSim.Course
{
    /// <summary>
    /// How one time of day lights a hole: the sun (or moon), the sky, ambient light, fog and the bloom. Day matches
    /// the hole scene as built (its Sun, Sky.mat and fog), so relighting a day hole changes nothing but the sun's
    /// side. Tuned for play first: at night the moon and ambient stay bright enough to read the slopes, and the
    /// night kit lights the green.
    /// </summary>
    public class SkyPreset
    {
        public TimeOfDay time;

        // Sun / moon
        public float elevation;     // degrees above the horizon
        public float azimuth;       // degrees clockwise from the line of play (tee to pin); 180 = behind the player
        public Color lightColor;
        public float intensity;
        public float shadowStrength = 1f;

        // Sky
        public bool proceduralSky = true;   // the scene's Skybox/Procedural; false = GolfSim/NightSky
        public float skyExposure = 1.25f, atmosphere = 0.9f, sunSize = 0.035f;
        public Color skyTint = new Color(0.5f, 0.5f, 0.5f);
        public Color zenith, horizon, afterglow; // NightSky only (afterglow is HDR, toward the set sun)
        public float stars;                      // NightSky star brightness
        public Color moon;                       // NightSky moon disc (HDR), black = none
        // Where the moon is drawn, like azimuth/elevation. Not where its light comes from: the disc hangs over the
        // hole where the tee camera sees it, while the light comes from behind the player so the slopes face it.
        public float moonAzimuth = 20f, moonElevation = 24f;

        // Ambient and fog
        public bool skyAmbient;     // the scene's baked sky probe (day); otherwise the three colours below
        public Color ambientSky, ambientEquator, ambientGround;
        public Color fog;
        public float fogDensity;
        public float reflections = 1f;

        // Post
        public float bloom = 0.25f;     // the scene profile's intensity
        public float bloomThreshold = 1.1f;
        public float exposure = 0.2f;   // post exposure, the scene profile's 0.2 by day

        /// <summary>0 by day, 1 at night: how much the night kit and the ball glow.</summary>
        public float darkness;

        public bool IsDark => darkness > 0f;

        public static SkyPreset For(TimeOfDay time) => time switch
        {
            TimeOfDay.GoldenHour => GoldenHour,
            TimeOfDay.Dusk => Dusk,
            TimeOfDay.Night => Night,
            _ => Day,
        };

        static readonly SkyPreset Day = new SkyPreset
        {
            time = TimeOfDay.Day,
            elevation = 48f, azimuth = 150f, lightColor = new Color(1f, 0.96f, 0.88f), intensity = 2.2f,
            skyAmbient = true,
            fog = new Color(0.7f, 0.78f, 0.86f), fogDensity = 0.0009f,
        };

        // Low warm sun from the side: long shadows across the fairway, a hazy orange horizon.
        static readonly SkyPreset GoldenHour = new SkyPreset
        {
            time = TimeOfDay.GoldenHour,
            elevation = 9f, azimuth = 95f, lightColor = new Color(1f, 0.70f, 0.42f), intensity = 2.1f, shadowStrength = 0.85f,
            skyExposure = 1.15f, atmosphere = 1.7f, sunSize = 0.05f, skyTint = new Color(0.56f, 0.5f, 0.46f),
            ambientSky = new Color(0.66f, 0.62f, 0.66f), ambientEquator = new Color(0.72f, 0.54f, 0.42f), ambientGround = new Color(0.22f, 0.18f, 0.14f),
            fog = new Color(0.86f, 0.66f, 0.5f), fogDensity = 0.0011f, reflections = 0.8f,
            bloom = 0.35f, exposure = 0.2f,
        };

        // The sun has just gone: a grazing pink light, an afterglow ahead and the first stars overhead.
        static readonly SkyPreset Dusk = new SkyPreset
        {
            time = TimeOfDay.Dusk,
            elevation = 3f, azimuth = 60f, lightColor = new Color(1f, 0.56f, 0.5f), intensity = 1.4f, shadowStrength = 0.45f,
            proceduralSky = false,
            zenith = new Color(0.06f, 0.08f, 0.22f), horizon = new Color(0.62f, 0.36f, 0.42f), afterglow = new Color(2.2f, 0.9f, 0.35f),
            stars = 0.5f,
            ambientSky = new Color(0.5f, 0.5f, 0.68f), ambientEquator = new Color(0.6f, 0.45f, 0.5f), ambientGround = new Color(0.12f, 0.1f, 0.12f),
            fog = new Color(0.36f, 0.27f, 0.36f), fogDensity = 0.0012f, reflections = 0.45f,
            bloom = 0.6f, bloomThreshold = 1f, exposure = 0.45f,
            darkness = 0.6f,
        };

        // Moonlight: cool, soft shadows, a starry sky. The ambient is lifted well above real night so the ground reads.
        static readonly SkyPreset Night = new SkyPreset
        {
            time = TimeOfDay.Night,
            elevation = 38f, azimuth = 160f, lightColor = new Color(0.62f, 0.74f, 1f), intensity = 0.55f, shadowStrength = 0.6f,
            proceduralSky = false,
            zenith = new Color(0.008f, 0.012f, 0.04f), horizon = new Color(0.05f, 0.07f, 0.14f), afterglow = Color.black,
            stars = 1.2f, moon = new Color(1.9f, 1.9f, 1.75f),
            ambientSky = new Color(0.2f, 0.25f, 0.4f), ambientEquator = new Color(0.13f, 0.16f, 0.24f), ambientGround = new Color(0.05f, 0.05f, 0.06f),
            fog = new Color(0.03f, 0.045f, 0.08f), fogDensity = 0.001f, reflections = 0.25f,
            bloom = 0.9f, bloomThreshold = 0.9f, exposure = 0.55f,
            darkness = 1f,
        };
    }
}
