using UnityEngine;

namespace GolfSim.Course
{
    /// <summary>Which photographed sky GolfSim/NightSky draws under its stars and moon (RuntimeMaterials holds them).</summary>
    public enum SkyArt { None, Starry, Twilight }

    /// <summary>
    /// How one time of day lights a hole: the sun (or moon), the sky, ambient light, fog and the colour grade. Day matches
    /// the hole scene as built (its Sun, Sky.mat and fog), so relighting a day hole changes nothing but the sun's side.
    /// Tuned for play first: at night the moonlight still models the slopes and the ambient keeps the ground readable,
    /// while the night kit lights the green.
    /// </summary>
    public class SkyPreset
    {
        public TimeOfDay time;

        // Sun / moon light
        public float elevation;     // degrees above the horizon
        public float azimuth;       // degrees clockwise from the line of play (tee to pin); 180 = behind the player
        public Color lightColor;
        public float intensity;
        public float shadowStrength = 1f;

        // Sky: the scene's Skybox/Procedural (day, golden hour) ...
        public bool proceduralSky = true;
        public float skyExposure = 1.25f, atmosphere = 0.9f, sunSize = 0.035f;
        public Color skyTint = new Color(0.5f, 0.5f, 0.5f);
        // ... or GolfSim/NightSky: a photo (or gradient) with stars, clouds and a moon.
        public SkyArt art;
        public float artExposure = 1f;
        public float artContrast = 1f;           // above 1: a darker background behind the Milky Way
        public float artTurn;                    // degrees from the line of play; a Twilight photo is turned to the set sun instead
        public Color zenith, horizon;            // horizon is also the fog colour, so sky and course meet without a seam
        public float horizonGlow = 1f;
        public Color afterglow;                  // HDR, toward the set sun (gradient skies only: a photo has its own)
        public float stars;
        public Color moon;                       // HDR moon disc, black = none
        public float moonSize = 0.025f;          // radians (the real one is 0.0045: drawn larger, like a long lens)
        // Where the moon is drawn. Not where its light comes from: the disc hangs over the hole where the tee camera
        // sees it, while the light comes in from the side behind the player so the slopes face it.
        public float moonAzimuth = 25f, moonElevation = 22f;
        public float cloudCover;
        public Color cloudColor = new Color(0.12f, 0.14f, 0.2f);

        // Ambient and fog
        public bool skyAmbient;     // the scene's baked sky probe (day); otherwise the three colours below
        public Color ambientSky, ambientEquator, ambientGround;
        public Color fog;           // procedural skies; a NightSky's fog is its horizon
        public float fogDensity;
        public float reflections = 1f;

        // Colour grade, over the scene's profile (bloom threshold above 1: only lights and glowing things bloom)
        public float bloom = 0.25f, bloomThreshold = 1.1f;
        public float exposure = 0.2f;   // post exposure, the scene profile's 0.2 by day
        public float temperature, contrast, saturation, vignette;
        public Vector4 lift = new Vector4(1f, 1f, 1f, 0f);

        /// <summary>0 by day, 1 at night: how much the night kit and the ball glow.</summary>
        public float darkness;

        public bool IsDark => darkness > 0f;
        public Color FogColor => proceduralSky ? fog : horizon;

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
            bloom = 0.35f, exposure = 0.35f, temperature = 8f, contrast = 5f, vignette = 0.15f,
        };

        // Civil twilight: the sun just under the horizon ahead and to the side, an orange glow there under a deepening
        // blue, the first stars overhead, soft pink light on the ground.
        static readonly SkyPreset Dusk = new SkyPreset
        {
            time = TimeOfDay.Dusk,
            elevation = 4f, azimuth = 60f, lightColor = new Color(1f, 0.6f, 0.52f), intensity = 1.2f, shadowStrength = 0.55f,
            proceduralSky = false, art = SkyArt.Twilight, artExposure = 1.6f,
            zenith = new Color(0.08f, 0.11f, 0.26f), horizon = new Color(0.42f, 0.36f, 0.42f), horizonGlow = 0.25f,
            afterglow = new Color(2.2f, 0.9f, 0.35f), stars = 0.35f, cloudCover = 0.12f, cloudColor = new Color(0.36f, 0.26f, 0.32f),
            ambientSky = new Color(0.36f, 0.4f, 0.58f), ambientEquator = new Color(0.5f, 0.4f, 0.44f), ambientGround = new Color(0.1f, 0.08f, 0.1f),
            fogDensity = 0.0012f, reflections = 0.7f,
            bloom = 0.5f, bloomThreshold = 1.05f, exposure = 0.45f, temperature = 4f, contrast = 8f, saturation = 4f, vignette = 0.2f,
            darkness = 0.6f,
        };

        // Moonlight: a cool blue-silver light from the side behind the player with soft shadows, a starry sky with the
        // Milky Way, the moon over the hole, a faint glow along the horizon. Ambient is low enough for the light to
        // model the ground but lifted well above a real night so the fairway and green read.
        static readonly SkyPreset Night = new SkyPreset
        {
            time = TimeOfDay.Night,
            elevation = 34f, azimuth = 130f, lightColor = new Color(0.66f, 0.77f, 1f), intensity = 1f, shadowStrength = 0.8f,
            proceduralSky = false, art = SkyArt.Starry, artExposure = 2f, artContrast = 1.5f, artTurn = 96f,
            zenith = new Color(0.006f, 0.01f, 0.03f), horizon = new Color(0.065f, 0.08f, 0.135f), horizonGlow = 1.1f,
            afterglow = Color.black, stars = 1f, moon = new Color(1f, 0.96f, 0.84f), cloudCover = 0.22f,
            cloudColor = new Color(0.07f, 0.085f, 0.12f),
            ambientSky = new Color(0.09f, 0.12f, 0.22f), ambientEquator = new Color(0.07f, 0.085f, 0.14f), ambientGround = new Color(0.025f, 0.025f, 0.035f),
            fogDensity = 0.0011f, reflections = 0.6f,
            bloom = 0.7f, bloomThreshold = 1f, exposure = 0.45f,
            temperature = -18f, contrast = 16f, saturation = -12f, vignette = 0.3f, lift = new Vector4(0.97f, 1f, 1.06f, 0.01f),
            darkness = 1f,
        };
    }
}
