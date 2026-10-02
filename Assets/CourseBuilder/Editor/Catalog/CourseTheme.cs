using GolfSim.Course;
using System;
using GolfSim.Course;
using System.Collections.Generic;
using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>
    /// A look for a hole (parkland, forest, links, desert ...): ground layers, which models stand in for each
    /// object kind, ground cover and water. Every asset slot is an AssetQuery into the AssetCatalog instead of a direct
    /// reference. ThemeResolver turns a theme into concrete sets, so one build pipeline serves both.
    /// The asset's name is the theme name that generated hole packages ask for ("theme" in hole.json).
    /// </summary>
    [CreateAssetMenu(menuName = "Golf/Course Theme", fileName = "CourseTheme")]
    public class CourseTheme : ScriptableObject
    {
        [Serializable]
        public class SurfaceSlot
        {
            [Tooltip("Surface name, paint priority and placeholder colour; leave its layer empty.")]
            public SurfaceLayerSet.Entry entry = new SurfaceLayerSet.Entry();
            public AssetQuery layer = new AssetQuery { category = AssetCategory.GroundLayer };
            [Tooltip("Mowing-stripe variant of the layer; used when the entry's stripe width is above 0.")]
            public AssetQuery stripeLayer = new AssetQuery { category = AssetCategory.GroundLayer };
        }

        [Serializable]
        public class ScatterSlot
        {
            [Tooltip("Object kind from the package's objects.bin that these models represent.")]
            public ObjectKind kind;
            public AssetQuery assets = new AssetQuery { category = AssetCategory.Tree };
        }

        [Serializable]
        public class DetailSlot
        {
            public ScatterSet.DetailRule rule = new ScatterSet.DetailRule();
            public AssetQuery assets = new AssetQuery { category = AssetCategory.GroundCover };
            public float scale = 1f;
            [Min(1), Tooltip("At most this many catalog matches become detail layers (each costs a draw call).")]
            public int maxPrototypes = 4;
        }

        [Tooltip("Preferred everywhere in this theme (e.g. desert, dry). Ranks matches; never filters them out.")]
        public string[] styleTags = new string[0];

        [Tooltip("In paint priority order: the first is the base layer that fills everything.")]
        public List<SurfaceSlot> surfaces = new List<SurfaceSlot>();
        [Tooltip("Models per object kind. Placement, size and count come from the hole package.")]
        public List<ScatterSlot> scatter = new List<ScatterSlot>();
        public List<DetailSlot> details = new List<DetailSlot>();

        [Header("Water")]
        [Tooltip("Optional: custom water tech. Empty = flat mesh with the material found by the query below.")]
        public WaterProvider waterProvider;
        public AssetQuery waterMaterial = new AssetQuery { category = AssetCategory.WaterMaterial };

        [Header("Clearances")]
        public string[] keepClear = { "fairway", "tee", "green", "bunker", "water" };
        [Min(0)] public float detailClearMargin = 0.75f;
    }
}
