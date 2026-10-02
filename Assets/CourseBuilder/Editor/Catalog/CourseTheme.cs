using System;
using System.Collections.Generic;
using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>
    /// A look for a hole (parkland, forest, links, desert ...). Holds the same rules as SurfaceLayerSet
    /// and ScatterSet, but every asset slot is an AssetQuery into the AssetCatalog instead of a direct
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
        }

        [Serializable]
        public class ScatterSlot
        {
            [Tooltip("Placement settings; its prototype list is filled from the query.")]
            public ScatterSet.Rule rule = new ScatterSet.Rule();
            public AssetQuery assets = new AssetQuery { category = AssetCategory.Tree };
            [Tooltip("Multiplies the catalog entries' scale ranges.")]
            public float scale = 1f;
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
        public List<ScatterSlot> scatter = new List<ScatterSlot>();
        public List<DetailSlot> details = new List<DetailSlot>();
        public AssetQuery mappedTrees = new AssetQuery { category = AssetCategory.Tree };

        [Header("Water")]
        [Tooltip("Optional: custom water tech. Empty = flat mesh with the material found by the query below.")]
        public WaterProvider waterProvider;
        public AssetQuery waterMaterial = new AssetQuery { category = AssetCategory.WaterMaterial };

        [Header("Clearances")]
        public string[] keepClear = { "fairway", "tee", "green", "bunker", "water" };
        [Min(0)] public float clearMargin = 4f;
        [Min(0)] public float detailClearMargin = 0.75f;
    }
}
