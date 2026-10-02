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
        public SurfaceResponse fallback = new SurfaceResponse { surface = "rough", restitution = 0.6f, friction = 0.6f, rolling = 0.35f, compliance = 1.4f };

        [Serializable]
        public class SurfaceResponse
        {
            public string surface;
            [Tooltip("Bounce energy kept relative to a firm green (1).")] public float restitution = 1f;
            [Tooltip("Sliding friction during a bounce.")] public float friction = 0.4f;
            [Tooltip("Rolling resistance as a fraction of g. Green 0.05 is about Stimp 11.")] public float rolling = 0.06f;
            [Tooltip("How much the turf gives under impact (crater tilt), 1 = green.")] public float compliance = 1f;
            [Tooltip("Ball stops here immediately (water).")] public bool hazard;
        }

        public static List<SurfaceResponse> DefaultSurfaces() => new List<SurfaceResponse>
        {
            new SurfaceResponse { surface = "green",   restitution = 1.00f, friction = 0.40f, rolling = 0.050f, compliance = 1.0f },
            new SurfaceResponse { surface = "fairway", restitution = 0.90f, friction = 0.45f, rolling = 0.110f, compliance = 1.1f },
            new SurfaceResponse { surface = "tee",     restitution = 0.90f, friction = 0.45f, rolling = 0.110f, compliance = 1.1f },
            new SurfaceResponse { surface = "rough",   restitution = 0.60f, friction = 0.60f, rolling = 0.350f, compliance = 1.4f },
            new SurfaceResponse { surface = "native",  restitution = 0.65f, friction = 0.60f, rolling = 0.450f, compliance = 1.3f },
            new SurfaceResponse { surface = "scrub",   restitution = 0.45f, friction = 0.70f, rolling = 0.700f, compliance = 1.5f },
            new SurfaceResponse { surface = "woods",   restitution = 0.45f, friction = 0.70f, rolling = 0.700f, compliance = 1.5f },
            new SurfaceResponse { surface = "bunker",  restitution = 0.20f, friction = 0.80f, rolling = 1.500f, compliance = 2.5f },
            new SurfaceResponse { surface = "water",   hazard = true },
        };

        public SurfaceResponse For(string surface) => surfaces.Find(s => s.surface == surface) ?? fallback;

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
