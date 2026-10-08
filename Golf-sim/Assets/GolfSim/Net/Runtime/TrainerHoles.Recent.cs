using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GolfSim.Net
{
    // Recently played holes (holes/recent.json, newest first): a random round sends them as `exclude`, so the trainer
    // draws other holes while it has enough, and the offline pick from the cache plays them last too.
    public static partial class TrainerHoles
    {
        const int RecentCount = 30;

        [Serializable]
        class RecentList
        {
            public List<string> ids = new List<string>();
        }

        static string RecentPath => Path.Combine(CacheFolder, "recent.json");

        /// <summary>The ids of the last ~30 holes played, newest first (empty when none or unreadable).</summary>
        public static List<string> RecentIds()
        {
            try
            {
                return File.Exists(RecentPath)
                    ? JsonUtility.FromJson<RecentList>(File.ReadAllText(RecentPath))?.ids ?? new List<string>()
                    : new List<string>();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TrainerHoles] Unreadable {RecentPath}: {e.Message}");
                return new List<string>();
            }
        }

        /// <summary>Puts a round's holes in front of the recent list (a hole already in it moves up).</summary>
        static void RememberPlayed(List<(TrainerHole hole, string folder)> holes)
        {
            var ids = holes.ConvertAll(h => h.hole.id).Distinct().ToList();
            ids.AddRange(RecentIds().Where(id => !ids.Contains(id)));
            try { WriteAtomic(RecentPath, JsonUtility.ToJson(new RecentList { ids = ids.Take(RecentCount).ToList() })); }
            catch (Exception e) { Debug.LogWarning($"[TrainerHoles] Couldn't save the recent holes: {e.Message}"); }
        }

        /// <summary>
        /// Offline random round: up to `count` different holes from the complete packages in the cache, in random order,
        /// recently played ones last: of these course types first (presets; empty: any), then of any (mixed). False
        /// when nothing is cached.
        /// </summary>
        static bool TryRandomCached(int count, string[] presets, out List<(TrainerHole hole, string folder)> holes, out bool mixed)
        {
            holes = CachedHoles(id => Matches(new TrainerHole { id = id }, presets), count, new HashSet<string>());
            mixed = holes.Count < count && presets.Length > 0;
            if (mixed) holes.AddRange(CachedHoles(_ => true, count - holes.Count, Ids(holes.Select(h => h.hole))));
            if (holes.Count == 0) return false;
            for (int i = 0; i < holes.Count; i++) holes[i].hole.rank = i + 1;
            return true;
        }
    }
}
