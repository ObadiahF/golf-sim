using System;
using System.Collections.Generic;
using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>
    /// What to scatter on a hole: models for OSM-mapped trees, plus density rules per surface.
    /// Everything becomes Terrain tree instances (cheap to render, LOD and billboards included).
    /// </summary>
    [CreateAssetMenu(menuName = "Golf/Scatter Set", fileName = "ScatterSet")]
    public class ScatterSet : ScriptableObject
    {
        public const string DefaultPath = "Assets/CourseBuilder/Settings/DefaultScatter.asset";

        [Serializable]
        public class Prototype
        {
            public GameObject prefab;
            [Min(0)] public float weight = 1f;
            public Vector2 scale = new Vector2(0.8f, 1.2f);
        }

        [Serializable]
        public class Rule
        {
            public string name;
            [Tooltip("Surfaces (from the Surface Layer Set) this rule scatters on.")]
            public string[] surfaces = new string[0];
            [Min(0)] public float perHectare = 10f;
            [Tooltip("Allowed terrain slope in degrees.")]
            public Vector2 slopeDegrees = new Vector2(0f, 35f);
            [Range(0, 1), Tooltip("0 = even spread, 1 = tight groves with gaps between.")]
            public float clumping = 0.5f;
            public List<Prototype> prototypes = new List<Prototype>();
        }

        [Tooltip("Models used for trees mapped individually in OSM (natural=tree / tree_row).")]
        public List<Prototype> mappedTrees = new List<Prototype>();

        public List<Rule> rules = new List<Rule>
        {
            new Rule { name = "Woods",          surfaces = new[] { "woods" },           perHectare = 250, clumping = 0.3f },
            new Rule { name = "Native trees",   surfaces = new[] { "native", "scrub" }, perHectare = 8,   clumping = 0.7f },
            new Rule { name = "Shrubs",         surfaces = new[] { "native", "scrub" }, perHectare = 90,  clumping = 0.6f, slopeDegrees = new Vector2(0, 40) },
            new Rule { name = "Slope boulders", surfaces = new[] { "native", "scrub" }, perHectare = 60,  clumping = 0.5f, slopeDegrees = new Vector2(25, 90) },
            new Rule { name = "Loose rocks",    surfaces = new[] { "native", "scrub" }, perHectare = 25,  clumping = 0.4f },
        };

        /// <summary>Ground cover (grass, small bushes) painted as Terrain detail meshes.</summary>
        [Serializable]
        public class DetailRule
        {
            public string name;
            public string[] surfaces = new string[0];
            [Range(0, 1), Tooltip("How much of the matching ground is covered at most.")]
            public float coverage = 0.6f;
            [Range(0, 1), Tooltip("0 = uniform carpet, 1 = scattered patches.")]
            public float patchiness = 0.5f;
            public Vector2 slopeDegrees = new Vector2(0f, 45f);
            [Min(0), Tooltip("Instances per covered spot; raise for a thicker carpet (costs performance).")]
            public float density = 5f;
            [Tooltip("Each prototype becomes its own detail layer; weight sets its share of the coverage.")]
            public List<Prototype> prototypes = new List<Prototype>();
        }

        public List<DetailRule> detailRules = new List<DetailRule>
        {
            new DetailRule { name = "Rough grass",  surfaces = new[] { "rough" },           coverage = 0.8f, patchiness = 0.3f },
            new DetailRule { name = "Native grass", surfaces = new[] { "native", "scrub" }, coverage = 0.6f, patchiness = 0.6f },
            new DetailRule { name = "Native brush", surfaces = new[] { "native", "scrub" }, coverage = 0.25f, patchiness = 0.8f },
            new DetailRule { name = "Woods floor",  surfaces = new[] { "woods" },           coverage = 0.5f, patchiness = 0.5f },
        };
        [Min(0), Tooltip("Meters of bare ground kept around keep-clear surfaces for ground cover.")]
        public float detailClearMargin = 0.75f;

        [Tooltip("Surfaces nothing is scattered on (mapped trees are still allowed near them).")]
        public string[] keepClear = { "fairway", "tee", "green", "bunker", "water" };
        [Min(0), Tooltip("Meters of extra clearance around keep-clear surfaces for scattered objects.")]
        public float clearMargin = 4f;

        public static ScatterSet LoadOrCreateDefault() =>
            GeneratedAssets.LoadOrCreate(DefaultPath, CreateInstance<ScatterSet>);

        public static Prototype Pick(List<Prototype> options, System.Random rng)
        {
            float total = 0;
            foreach (var p in options) if (p.prefab) total += p.weight;
            float roll = (float)rng.NextDouble() * total;
            foreach (var p in options)
            {
                if (!p.prefab) continue;
                roll -= p.weight;
                if (roll <= 0) return p;
            }
            return null;
        }
    }
}
