using System;
using UnityEngine;

namespace GolfSim.Ball
{
    /// <summary>
    /// How the ball reacts to trees, shrubs and rocks (HoleInfo.obstacles): rebounds off solid trunks, bodies and
    /// rocks, and chance hits on leaves and branches inside tree canopies. Part of BallPhysicsSettings.
    /// </summary>
    [Serializable]
    public class ObstacleSettings
    {
        [Header("Trunks, shrubs and rocks")]
        [Tooltip("Normal speed kept off a trunk (random in this range).")]
        public Vector2 woodRestitution = new Vector2(0.3f, 0.5f);
        public Vector2 rockRestitution = new Vector2(0.5f, 0.6f);
        public Vector2 shrubRestitution = new Vector2(0.15f, 0.3f);
        [Tooltip("Speed along the surface kept in a rebound (friction).")]
        public float surfaceSpeedKept = 0.7f;
        [Tooltip("Random scatter of a rebound, degrees.")]
        public float scatter = 8f;
        [Tooltip("Spin kept after hitting a trunk or rock.")]
        public float spinKept = 0.2f;

        [Header("Tree canopies")]
        [Tooltip("Canopy shape per tree kind (0 conifer, 1 deciduous, 2 palm, 3 cactus), as fractions of the tree height.")]
        public Canopy[] canopies = DefaultCanopies();
        [Tooltip("Speed kept by a leaf / branch hit (random in this range).")]
        public Vector2 canopySpeedKept = new Vector2(0.2f, 0.6f);
        [Tooltip("Largest sideways deflection of a canopy hit, degrees either way.")]
        public float canopyDeflection = 45f;
        [Tooltip("Change of the flight angle by a canopy hit, degrees (negative knocks the ball down).")]
        public Vector2 canopyPitch = new Vector2(-50f, 10f);
        public float canopySpinKept = 0.5f;

        [Serializable]
        public class Canopy
        {
            public string name;
            [Tooltip("A cone narrowing to the top (conifer); otherwise an ellipsoid crown.")] public bool cone;
            [Tooltip("Crown bottom, fraction of the height.")] public float bottom = 0.3f;
            [Tooltip("Crown top, fraction of the height.")] public float top = 1f;
            [Tooltip("Crown radius (cone: at its base), fraction of the height.")] public float radius = 0.4f;
            [Tooltip("Leaf / branch hits per meter travelled inside the crown (chance rate).")] public float density = 0.3f;
        }

        public static Canopy[] DefaultCanopies() => new[]
        {
            new Canopy { name = "conifer",   cone = true, bottom = 0.30f, top = 1.00f, radius = 0.22f, density = 0.35f },
            new Canopy { name = "deciduous",              bottom = 0.30f, top = 1.00f, radius = 0.40f, density = 0.30f },
            new Canopy { name = "palm",                   bottom = 0.80f, top = 1.00f, radius = 0.30f, density = 0.30f },
            new Canopy { name = "cactus",                 bottom = 0.35f, top = 0.85f, radius = 0.20f, density = 0.20f },
        };

        /// <summary>The canopy of this obstacle kind, or null (shrubs and rocks).</summary>
        public Canopy CanopyFor(byte kind) => kind < canopies.Length ? canopies[kind] : null;

        public Vector2 RestitutionFor(byte kind) => ObstacleKinds.IsRock(kind) ? rockRestitution : kind == ObstacleKinds.Shrub ? shrubRestitution : woodRestitution;
    }

    /// <summary>The obstacle kinds in HoleInfo.obstacles (objects.bin).</summary>
    public static class ObstacleKinds
    {
        public const byte Shrub = 4;
        public const byte Boulder = 5;

        /// <summary>Boulders and rocks: a dome. Everything else has an upright cylinder (trunk / body).</summary>
        public static bool IsRock(byte kind) => kind >= Boulder;
    }
}
