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
        /// Offline random round: up to `count` different holes from every complete package in the cache, in random
        /// order, recently played ones last. False when nothing is cached.
        /// </summary>
        static bool TryRandomCached(int count, out List<(TrainerHole hole, string folder)> holes)
        {
            holes = null;
            if (!Directory.Exists(CacheFolder)) return false;
            var recent = RecentIds();
            var rng = new System.Random();
            var folders = Directory.GetDirectories(CacheFolder)
                .Where(f => !Path.GetFileName(f).Contains(".partial-") && IsComplete(f))
                .OrderBy(f => recent.Contains(Path.GetFileName(f)))
                .ThenBy(_ => rng.Next())
                .Take(count)
                .ToList();
            if (folders.Count == 0) return false;
            holes = folders.Select((f, i) => (new TrainerHole { rank = i + 1, id = Path.GetFileName(f) }, f)).ToList();
            return true;
        }
    }
}
