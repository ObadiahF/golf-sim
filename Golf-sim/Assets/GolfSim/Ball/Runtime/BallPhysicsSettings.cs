using System;
using System.Collections.Generic;
using UnityEngine;

namespace GolfSim.Ball
{
    /// <summary>
    /// Golf ball constants, aerodynamic coefficients and per-surface ground response.
    /// Aero defaults are fitted to TrackMan PGA Tour averages by Tools/ball_physics/calibrate.py;
    /// refit there and copy the numbers here if the model changes.
    /// </summary>
    [CreateAssetMenu(menuName = "Golf/Ball Physics Settings", fileName = "BallPhysicsSettings")]
    public class BallPhysicsSettings : ScriptableObject
    {
        public const float Mass = 0.04593f;    // kg, USGA maximum
        public const float Radius = 0.021335f; // m, 1.68 in diameter
        public const float Gravity = 9.81f;
        public static readonly float Area = Mathf.PI * Radius * Radius;

        [Header("Aerodynamics (fitted, see calibrate.py)")]
        public float cd0 = 0.1566f;
        public float cdSpin = 0.2552f;
        [Tooltip("Dimple drag rise as the ball slows: Cd += cdRe * 1e5 / Re")]
        public float cdRe = 0.0830f;
        public float clScale = 0.4915f;
        public float clPower = 0.4384f;
        [Tooltip("Seconds for spin to decay to 1/e.")]
        public float spinDecayTime = 8.484f;

        [Header("Air")]
        public float temperatureC = 25f;
        [Tooltip("Course elevation in meters; thinner air flies farther.")]
        public float elevation = 0f;

        [Header("Ground")]
        public List<SurfaceResponse> surfaces = DefaultSurfaces();
        [Tooltip("Used for surfaces not in the list.")]
        public SurfaceResponse fallback = new SurfaceResponse { surface = "rough", restitution = 0.45f, friction = 0.70f, rolling = 0.700f, compliance = 1.4f };

        [Header("Lie (how the surface the ball is hit from changes the launch)")]
        [Tooltip("Surfaces not listed (tee, fairway, green) are clean lies.")]
        public List<LieResponse> lies = DefaultLies();
        [Tooltip("Ball speed (m/s) from which a shot counts as full (driver to mid iron); lie speed loss is LieResponse.speed.")]
        public float fullShotSpeed = 60f;
        [Tooltip("Ball speed (m/s) up to which a shot counts as short (wedge, chip, putt); lie speed loss is LieResponse.shortSpeed.")]
        public float shortShotSpeed = 40f;

        [Header("Trees, shrubs and rocks")]
        public ObstacleSettings obstacles = new ObstacleSettings();

        [Serializable]
        public class SurfaceResponse
        {
            public string surface;
            [Tooltip("Bounce energy kept relative to a firm green (1).")] public float restitution = 1f;
            [Tooltip("Sliding friction during a bounce.")] public float friction = 0.4f;
            [Tooltip("Rolling resistance as a fraction of g. Green 0.06 is about Stimp 9.3.")] public float rolling = 0.06f;
            [Tooltip("How much the turf gives under impact (crater tilt), 1 = green.")] public float compliance = 1f;
            [Tooltip("Ball stops here immediately (water).")] public bool hazard;
        }

        /// <summary>Launch change for a lie; speed loss blends from shortSpeed to speed as the shot gets fuller.</summary>
        [Serializable]
        public class LieResponse
        {
            public string surface;
            [Tooltip("Ball speed kept on a full shot.")] public float speed = 1f;
            [Tooltip("Ball speed kept on a short shot (wedge, chip, putt).")] public float shortSpeed = 1f;
            [Tooltip("Spin kept: grass between the face and the ball gives a flyer.")] public float spin = 1f;
            [Tooltip("Degrees added to the launch angle.")] public float launch;
            [Tooltip("Clubs with less loft than this (degrees) can't get under the ball here (0 = any club is fine).")]
            public float minLoft;
            [Tooltip("Extra ball speed kept by a club 20° under minLoft (a fairway wood in a bunker); it falls from 1 at minLoft.")]
            public float lowLoftSpeed = 1f;
            [Tooltip("Degrees added to the launch angle by a club 20° under minLoft (it catches the ball thin); 0 at minLoft.")]
            public float lowLoftLaunch;
        }

        public static List<LieResponse> DefaultLies() => new List<LieResponse>
        {
            new LieResponse { surface = "rough",  speed = 0.88f, shortSpeed = 0.93f, spin = 0.70f, launch = 1.5f },
            new LieResponse { surface = "native", speed = 0.86f, shortSpeed = 0.91f, spin = 0.65f, launch = 2.0f },
            new LieResponse { surface = "scrub",  speed = 0.85f, shortSpeed = 0.90f, spin = 0.60f, launch = 2.0f },
            new LieResponse { surface = "woods",  speed = 0.85f, shortSpeed = 0.90f, spin = 0.60f, launch = 2.0f },
            // Sand: the wedges splash it out and the irons pick it clean for less, but under 30° of loft it gets ugly:
            // a 5 wood keeps under half its speed and comes off low (~35 yd, often into the face), a driver ~10 yd.
            new LieResponse { surface = "bunker", speed = 0.75f, shortSpeed = 0.90f, spin = 0.75f, launch = 2.0f,
                              minLoft = 30f, lowLoftSpeed = 0.3f, lowLoftLaunch = -6f },
        };

        /// <summary>How hitting from this surface changes this shot (its ball speed, and its club's loft: Clubs.LoftOf).</summary>
        public LieEffect LieFor(string surface, ShotData shot) => LieFor(surface, shot.ballSpeed, Clubs.LoftOf(shot));

        /// <summary>How hitting from this surface changes a shot of this ball speed (m/s) with a club of this loft (degrees).</summary>
        public LieEffect LieFor(string surface, float ballSpeed, float loft)
        {
            var lie = lies.Find(l => l.surface == surface);
            if (lie == null) return LieEffect.Clean(surface);
            float full = Mathf.InverseLerp(shortShotSpeed, fullShotSpeed, ballSpeed);
            float speed = Mathf.Lerp(lie.shortSpeed, lie.speed, full), launch = lie.launch;
            if (lie.minLoft > 0f && loft < lie.minLoft)
            {
                // 0 at minLoft, 1 at 20° under it; past that (the driver, the putter) it gets no worse.
                float under = Mathf.Clamp01((lie.minLoft - loft) / 20f);
                speed *= Mathf.Lerp(1f, lie.lowLoftSpeed, under);
                launch += lie.lowLoftLaunch * under;
            }
            return new LieEffect(surface, speed, lie.spin, launch);
        }

        public static List<SurfaceResponse> DefaultSurfaces() => new List<SurfaceResponse>
        {
            new SurfaceResponse { surface = "green",   restitution = 1.00f, friction = 0.40f, rolling = 0.060f, compliance = 1.0f },
            new SurfaceResponse { surface = "fairway", restitution = 0.90f, friction = 0.45f, rolling = 0.110f, compliance = 1.1f },
            new SurfaceResponse { surface = "tee",     restitution = 0.90f, friction = 0.45f, rolling = 0.110f, compliance = 1.1f },
            new SurfaceResponse { surface = "rough",   restitution = 0.45f, friction = 0.70f, rolling = 0.700f, compliance = 1.4f },
            new SurfaceResponse { surface = "native",  restitution = 0.50f, friction = 0.70f, rolling = 0.800f, compliance = 1.3f },
            new SurfaceResponse { surface = "scrub",   restitution = 0.40f, friction = 0.75f, rolling = 1.000f, compliance = 1.5f },
            new SurfaceResponse { surface = "woods",   restitution = 0.40f, friction = 0.75f, rolling = 1.000f, compliance = 1.5f },
            new SurfaceResponse { surface = "bunker",  restitution = 0.20f, friction = 0.80f, rolling = 1.500f, compliance = 2.5f },
            new SurfaceResponse { surface = "water",   hazard = true },
        };

        public SurfaceResponse For(string surface) => surfaces.Find(s => s.surface == surface) ?? fallback;

        /// <summary>A runtime copy made by WithOverrides (live tuning), never saved; the asset it came from is untouched.</summary>
        public bool IsRuntimeCopy => isRuntimeCopy;
        [NonSerialized] bool isRuntimeCopy;

        /// <summary>A runtime copy of these settings with the profile's overrides applied (BallPhysicsProfile).</summary>
        public BallPhysicsSettings WithOverrides(PhysicsProfile profile)
        {
            var copy = Instantiate(this);
            copy.name = name + " (live)";
            copy.hideFlags = HideFlags.DontSave;
            copy.isRuntimeCopy = true;
            foreach (var o in profile.surfaces)
            {
                var s = o == null ? null : copy.surfaces.Find(x => x.surface == o.surface);
                if (s == null) continue;
                if (!float.IsNaN(o.rolling)) s.rolling = o.rolling;
                if (!float.IsNaN(o.restitution)) s.restitution = o.restitution;
                if (!float.IsNaN(o.friction)) s.friction = o.friction;
            }
            return copy;
        }

        /// <summary>Air density from temperature and elevation (standard atmosphere, dry air).</summary>
        public float AirDensity
        {
            get
            {
                float pressure = 101325f * Mathf.Pow(1f - 2.25577e-5f * elevation, 5.25588f);
                return pressure / (287.05f * (273.15f + temperatureC));
            }
        }

        static BallPhysicsSettings defaults;

        /// <summary>In-memory defaults for when no settings asset is assigned.</summary>
        public static BallPhysicsSettings Defaults => defaults ? defaults : defaults = CreateInstance<BallPhysicsSettings>();
    }
}
