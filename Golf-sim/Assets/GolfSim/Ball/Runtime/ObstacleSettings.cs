using System;
using GolfSim.Course;
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
        [Tooltip("Canopy per tree kind (0 conifer, 1 deciduous, 2 palm, 3 cactus): the leaf density, and the shape (fractions " +
                 "of the tree height) for trees whose drawn crown wasn't measured when the hole was built.")]
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
            new Canopy { name = "conifer",   cone = true, bottom = 0.30f, top = 1.00f, radius = 0.22f, density = 0.20f }, // porous
            new Canopy { name = "deciduous",              bottom = 0.30f, top = 1.00f, radius = 0.40f, density = 0.30f },
            new Canopy { name = "palm",                   bottom = 0.80f, top = 1.00f, radius = 0.30f, density = 0.30f },
            new Canopy { name = "cactus",                 bottom = 0.35f, top = 0.85f, radius = 0.20f, density = 0.20f },
        };

        /// <summary>The canopy of this obstacle kind, or null (shrubs and rocks).</summary>
        public Canopy CanopyFor(byte kind) => kind < canopies.Length ? canopies[kind] : null;

        /// <summary>
        /// A tree's crown in its own meters: the crown its model draws (measured when the hole was built), with the leaf
        /// density of the kind of tree drawn; else its kind's default shape. None (radius 0) for shrubs and rocks.
        /// </summary>
        public Crown CrownOf(in Obstacle o)
        {
            var canopy = o.IsTree ? CanopyFor(o.kind) : null;
            if (canopy == null) return default;
            if (o.HasCrown)
                return new Crown
                {
                    cone = o.crownCone, bottom = o.crownBottom, top = Mathf.Min(o.crownTop, o.height), radius = o.crownRadius,
                    density = (CanopyFor(o.crownKind) ?? canopy).density,
                };
            return new Crown
            {
                cone = canopy.cone, bottom = canopy.bottom * o.height, top = canopy.top * o.height, radius = canopy.radius * o.height,
                density = canopy.density,
            };
        }

        public Vector2 RestitutionFor(byte kind) => ObstacleKinds.IsRock(kind) ? rockRestitution : kind == ObstacleKinds.Shrub ? shrubRestitution : woodRestitution;
    }

    /// <summary>A tree crown, heights above the tree's base: a cone narrowing to the top, or an ellipsoid.</summary>
    public struct Crown
    {
        public bool cone;
        public float bottom, top, radius; // radius: the widest (a cone's at its base)
        /// <summary>Leaf / branch hits per meter travelled inside.</summary>
        public float density;

        public bool Exists => radius > 0f && top > bottom;

        /// <summary>The crown's radius at this height above the base (0 outside it).</summary>
        public float RadiusAt(float y)
        {
            if (!Exists || y < bottom || y > top) return 0f;
            float u = (y - bottom) / (top - bottom);
            return cone ? radius * (1f - u) : radius * Mathf.Sqrt(Mathf.Max(0f, 1f - (2f * u - 1f) * (2f * u - 1f)));
        }

        /// <summary>The same crown with its sizes scaled (the hole's scale; a margin for camera views).</summary>
        public Crown Scaled(float across, float up) => new Crown
        {
            cone = cone, bottom = bottom * up, top = top * up, radius = radius * across, density = density,
        };
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
