using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace GolfSim.Net
{
    // Course types (course_gen presets, Tools/course_gen/style.py): a random round of the types the player chose asks
    // the trainer for them (random-holes?preset=a,b; GET /api/game/presets says how many holes each has). A trainer
    // that has too few holes of them, or an older one that ignores `preset`, is topped up: first with cached holes of
    // those types, then with holes of any type (Result.mixed, so the game can say so). A hole's type is the trainer's
    // `preset` field, else its id's prefix (generated ids are <preset>_<seed>_<digest>).
    public static partial class TrainerHoles
    {
        const int PresetsTimeout = 5; // s: the course screen asks while the player looks at it

        /// <summary>A course type on the trainer and how many holes a round can draw from it.</summary>
        [Serializable]
        public class PresetInfo
        {
            public string id;
            public string name;
            public string theme;
            public int playable;
        }

        [Serializable]
        class PresetList
        {
            public int stock;
            public PresetInfo[] presets;
        }

        /// <summary>The trainer's course types from the last FetchPresets, or null (not asked yet, unreachable, or an older trainer).</summary>
        public static PresetInfo[] KnownPresets { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetPresets() => KnownPresets = null;

        /// <summary>
        /// GET /api/game/presets into KnownPresets, then done(it) (null when the trainer can't say: down, no key, or an
        /// older trainer without the endpoint). Run with StartCoroutine.
        /// </summary>
        public static IEnumerator FetchPresets(ServerConfig config, Action<PresetInfo[]> done)
        {
            string key = config.TrainerKey;
            if (string.IsNullOrEmpty(key) || !config.useTrainerHoles) { done(null); yield break; }
            using var req = Get($"{config.trainerUrl.Trim().TrimEnd('/')}/api/game/presets", key, PresetsTimeout);
            yield return req.SendWebRequest();
            PresetInfo[] presets = null;
            if (req.result == UnityWebRequest.Result.Success)
            {
                try { presets = JsonUtility.FromJson<PresetList>(req.downloadHandler.text)?.presets; }
                catch (Exception e) { Debug.LogWarning($"[TrainerHoles] Unreadable course types: {e.Message}"); }
            }
            if (presets != null) KnownPresets = presets;
            done(presets);
        }

        /// <summary>Holes the trainer can draw from these course types (all of them for none), or -1 when it hasn't said.</summary>
        public static int Playable(string[] presets)
        {
            if (KnownPresets == null) return -1;
            return KnownPresets.Where(p => presets.Length == 0 || presets.Contains(p.id)).Sum(p => p.playable);
        }

        /// <summary>A hole's course type: as the trainer listed it, else its id's prefix.</summary>
        public static string PresetOf(TrainerHole hole) => !string.IsNullOrEmpty(hole.preset) ? hole.preset : PresetOfId(hole.id);

        static string PresetOfId(string id)
        {
            int cut = id.IndexOf('_');
            return cut > 0 ? id.Substring(0, cut) : "";
        }

        static bool Matches(TrainerHole hole, string[] presets) => presets.Length == 0 || presets.Contains(PresetOf(hole));

        /// <summary>"&amp;preset=a,b" for the types the trainer knows (all of them while it hasn't said), "" for any type.</summary>
        static string PresetQuery(string[] presets)
        {
            var known = KnownPresets == null ? presets : presets.Where(p => KnownPresets.Any(k => k.id == p)).ToArray();
            return known.Length == 0 ? "" : "&preset=" + Uri.EscapeDataString(string.Join(",", known));
        }

        static string RandomUrl(string baseUrl, int count, IEnumerable<string> exclude, string presetQuery) =>
            $"{baseUrl}/api/game/random-holes?count={count}&exclude={Uri.EscapeDataString(string.Join(",", exclude))}{presetQuery}";

        /// <summary>
        /// A random round of these course types: the trainer's draw of them, topped up with cached holes of them, then
        /// with other types (what an older trainer drew anyway, else a second draw of any type, else the cache).
        /// </summary>
        static IEnumerator RandomRound(string baseUrl, string key, int count, string[] presets, Func<bool> cancelled, Result result,
                                       Action<List<TrainerHole>> done)
        {
            var recent = RecentIds();
            string query = PresetQuery(presets);
            HoleList list = null;
            long code = 0;
            yield return GetList(RandomUrl(baseUrl, count, recent, query), key, cancelled, (l, c, e) => (list, code, result.error) = (l, c, e));
            if (list == null && code == 400 && query != "")
            {
                // The trainer doesn't know one of the types: draw from all of them and keep what matches.
                query = "";
                yield return GetList(RandomUrl(baseUrl, count, recent, query), key, cancelled, (l, c, e) => (list, code, result.error) = (l, c, e));
            }
            if (list == null) yield break;
            result.error = null;
            var drawn = list.holes ?? new TrainerHole[0];
            var picked = drawn.Where(h => Matches(h, presets)).Take(count).ToList();
            if (picked.Count < count) picked.AddRange(CachedHoles(id => Matches(new TrainerHole { id = id }, presets), count - picked.Count, Ids(picked)).Select(h => h.hole));
            if (picked.Count < count && presets.Length > 0)
            {
                result.mixed = true;
                var others = drawn.Where(h => !Matches(h, presets)).ToList(); // an older trainer that ignored `preset`
                if (others.Count == 0 && query != "")
                {
                    HoleList any = null;
                    yield return GetList(RandomUrl(baseUrl, count - picked.Count, recent.Concat(Ids(picked)), ""), key, cancelled, (l, _, _) => any = l);
                    others = any?.holes?.ToList() ?? others;
                }
                picked.AddRange(others.Where(h => !Ids(picked).Contains(h.id)).Take(count - picked.Count));
                if (picked.Count < count) picked.AddRange(CachedHoles(_ => true, count - picked.Count, Ids(picked)).Select(h => h.hole));
            }
            if (picked.Count == 0) result.error = "The trainer has no playable holes yet";
            else done(picked);
        }

        /// <summary>GETs a hole list: done(list or null, HTTP status, error or null).</summary>
        static IEnumerator GetList(string url, string key, Func<bool> cancelled, Action<HoleList, long, string> done)
        {
            using var req = Get(url, key, ListTimeout);
            yield return Send(req, cancelled);
            if (cancelled()) yield break;
            if (req.result != UnityWebRequest.Result.Success) { done(null, req.responseCode, $"Random holes: {req.error}"); yield break; }
            done(JsonUtility.FromJson<HoleList>(req.downloadHandler.text), req.responseCode, null);
        }

        static HashSet<string> Ids(IEnumerable<TrainerHole> holes) => new HashSet<string>(holes.Select(h => h.id));

        /// <summary>
        /// Up to `count` complete cached packages whose id `wanted` takes and `skip` doesn't hold, in random order with
        /// recently played ones last.
        /// </summary>
        static List<(TrainerHole hole, string folder)> CachedHoles(Func<string, bool> wanted, int count, ICollection<string> skip)
        {
            if (!Directory.Exists(CacheFolder) || count <= 0) return new List<(TrainerHole, string)>();
            var recent = RecentIds();
            var rng = new System.Random();
            return Directory.GetDirectories(CacheFolder)
                .Select(f => (folder: f, id: Path.GetFileName(f)))
                .Where(f => !f.id.Contains(".partial-") && !skip.Contains(f.id) && wanted(f.id) && IsComplete(f.folder))
                .OrderBy(f => recent.Contains(f.id))
                .ThenBy(_ => rng.Next())
                .Take(count)
                .Select(f => (new TrainerHole { id = f.id, preset = PresetOfId(f.id) }, f.folder))
                .ToList();
        }
    }
}
