using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GolfSim.Course
{
    /// <summary>
    /// Which models represent each object kind in a hole's objects.bin (trees, shrubs, rocks), plus ground
    /// cover rules. Where objects go comes from the package, never from here (Docs/hole-format).
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
            [Tooltip("Ground cover only: random size range. Objects are sized by the package's heights.")]
            public Vector2 scale = new Vector2(0.8f, 1.2f);
        }

        [Serializable]
        public class Rule
        {
            public string name;
            public ObjectKind kind;
            public List<Prototype> prototypes = new List<Prototype>();
        }

        [Tooltip("Models per object kind. A kind without models borrows another's (see Fallback).")]
        public List<Rule> rules = Enum.GetValues(typeof(ObjectKind)).Cast<ObjectKind>()
            .Select(k => new Rule { name = k.ToString(), kind = k }).ToList();

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
            [Min(0), Tooltip("Meters over which it thins out toward keep-clear surfaces (the fairway); 0 = stops at the margin.")]
            public float edgeFadeMeters;
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

        [Tooltip("Surfaces no ground cover grows on.")]
        public string[] keepClear = { "fairway", "tee", "green", "bunker", "water" };

        /// <summary>Models for a kind, borrowing from related kinds when it has none (e.g. no palms: deciduous).</summary>
        public List<Prototype> PrototypesFor(ObjectKind kind) => RuleFor(kind)?.prototypes;

        /// <summary>The rule whose models draw this kind (its own, or the one it borrows from), or null.</summary>
        public Rule RuleFor(ObjectKind kind)
        {
            var k = kind;
            while (true)
            {
                var rule = rules.FirstOrDefault(r => r.kind == k && r.prototypes.Any(p => p.prefab));
                if (rule != null) return rule;
                var next = Fallback(k);
                if (next == null || next == kind) return null; // went round the whole cycle
                k = next.Value;
            }
        }

        /// <summary>Next kind to borrow models from. Trees, small plants and rocks each form a cycle.</summary>
        static ObjectKind? Fallback(ObjectKind kind) => kind switch
        {
            ObjectKind.Palm => ObjectKind.Deciduous,
            ObjectKind.Deciduous => ObjectKind.Conifer,
            ObjectKind.Conifer => ObjectKind.Palm,
            ObjectKind.Cactus => ObjectKind.Shrub,
            ObjectKind.Shrub => ObjectKind.Cactus,
            ObjectKind.Boulder => ObjectKind.Rock,
            ObjectKind.Rock => ObjectKind.Boulder,
            _ => null,
        };

        /// <summary>Deterministic weighted pick: the same roll (0..1) always gives the same model.</summary>
        public static Prototype Pick(List<Prototype> options, float roll01)
        {
            float total = 0;
            foreach (var p in options) if (p.prefab) total += p.weight;
            float roll = roll01 * total;
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
