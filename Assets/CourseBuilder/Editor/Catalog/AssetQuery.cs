using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>What kind of thing a catalog entry is. Themes always ask for assets by category + tags.</summary>
    public enum AssetCategory
    {
        Tree,
        Shrub,
        Rock,
        GroundCover,   // grass, ferns, small bushes: painted as Terrain detail meshes
        GroundLayer,   // TerrainLayer for a surface (fairway, rough, bunker ...)
        WaterMaterial,
        Prop,
    }

    /// <summary>
    /// Selects catalog entries by tags instead of by asset reference, so new assets are picked up
    /// automatically once tagged. Tags are lower-case words: biome (forest, links, desert ...),
    /// kind (conifer, grass, dry, fern, boulder ...) and, for ground layers, the surface name.
    /// </summary>
    [Serializable]
    public class AssetQuery
    {
        public AssetCategory category;
        [Tooltip("Entry must have every one of these tags.")]
        public string[] allTags = new string[0];
        [Tooltip("Entry must have at least one of these tags (ignored when empty).")]
        public string[] anyTags = new string[0];
        [Tooltip("Entry must have none of these tags.")]
        public string[] noneTags = new string[0];
        [Tooltip("Not required, but entries with these tags are preferred / weighted up.")]
        public string[] preferTags = new string[0];

        public AssetQuery() { }

        public AssetQuery(AssetCategory category, string[] all = null, string[] any = null, string[] none = null, string[] prefer = null)
        {
            this.category = category;
            allTags = all ?? new string[0];
            anyTags = any ?? new string[0];
            noneTags = none ?? new string[0];
            preferTags = prefer ?? new string[0];
        }

        public bool Matches(CatalogEntry entry) =>
            entry.asset && entry.category == category
            && allTags.All(entry.HasTag)
            && (anyTags.Length == 0 || anyTags.Any(entry.HasTag))
            && !noneTags.Any(entry.HasTag);

        /// <summary>Number of preferred tags the entry carries, counting the theme's style tags too.</summary>
        public int Score(CatalogEntry entry, IEnumerable<string> styleTags) =>
            preferTags.Concat(styleTags ?? Enumerable.Empty<string>()).Distinct().Count(entry.HasTag);

        public override string ToString()
        {
            var parts = new List<string> { category.ToString() };
            if (allTags.Length > 0) parts.Add("all:" + string.Join(",", allTags));
            if (anyTags.Length > 0) parts.Add("any:" + string.Join(",", anyTags));
            if (noneTags.Length > 0) parts.Add("not:" + string.Join(",", noneTags));
            return string.Join(" ", parts);
        }
    }
}
