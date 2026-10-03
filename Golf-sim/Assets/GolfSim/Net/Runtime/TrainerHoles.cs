using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace GolfSim.Net
{
    /// <summary>
    /// Downloads a round of Course Trainer holes (its read-only Game API) into a local cache: a weighted random pick
    /// (ServerConfig.HoleSelection.Random, skipping recently played holes) or the top-rated holes. Packages never
    /// change, so a cached hole is reused forever. Each round's hole list is saved next to the cache (rounds/), so a
    /// resumed game replays the same holes. When the trainer is down a random round is drawn from the cached
    /// packages, a top-rated one replays the last top list. One fetch runs at a time; a second waits for the first.
    /// See Tools/course_trainer/README.md, "Game API".
    /// </summary>
    public static partial class TrainerHoles
    {
        /// <summary>Files a hole needs to be built (Docs/hole-format); gen.json and preview.png are optional.</summary>
        static readonly string[] Files = { "hole.json", "heightmap.raw", "objects.bin" };
        const string LastList = "last";
        const int ListTimeout = 10, FileTimeout = 60; // s
        const int MaxHoles = 18;

        [Serializable]
        public class TrainerHole
        {
            public int rank;
            public string id;
            public string preset;
            public int par;
            public int ups;
            public int downs;
            public float score;
        }

        [Serializable]
        class HoleList
        {
            public string formula;
            public TrainerHole[] holes;
        }

        /// <summary>What a fetch found: the holes (with their local folders) in order, or an error.</summary>
        public class Result
        {
            public List<(TrainerHole hole, string folder)> holes;
            public string error;
            /// <summary>The holes came from a saved list, not the trainer (offline is set when it couldn't be reached).</summary>
            public bool fromCache, offline;
            /// <summary>A random round (ServerConfig.HoleSelection.Random), not the top-rated holes.</summary>
            public bool random;
        }

        static bool busy;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => busy = false;

        public static string CacheFolder => Path.Combine(Application.persistentDataPath, "holes");
        static string RoundsFolder => Path.Combine(CacheFolder, "rounds");

        /// <summary>
        /// Gets `count` holes for a round (config.holeSelection) and makes sure each is cached, then calls done exactly
        /// once. roundKey (e.g. a server game) replays the list saved for it without the network; otherwise the
        /// trainer is asked, and when it can't be reached a random round comes from the cached packages
        /// (TryRandomCached) and a top-rated one from the last saved top list. progress(i, n) reports each hole as it
        /// is checked or downloaded; cancelled stops the fetch early (done is then not called).
        /// Run with StartCoroutine.
        /// </summary>
        public static IEnumerator Fetch(ServerConfig config, int count, string roundKey, Action<int, int> progress,
                                        Func<bool> cancelled, Action<Result> done)
        {
            while (busy)
            {
                if (cancelled()) yield break;
                yield return null; // single flight: wait for the other fetch (its holes are then cached)
            }
            busy = true;
            var result = new Result { random = config.holeSelection == ServerConfig.HoleSelection.Random };
            try
            {
                if (roundKey != null && TryCached(roundKey, out result.holes))
                    result.fromCache = true;
                else
                    yield return SafeCoroutine.Run(FetchOnline(config, count, result.random, progress, cancelled, result),
                                                   e => result.error = e.Message);
                if (cancelled()) yield break;
                if (result.holes == null && (result.random ? TryRandomCached(count, out result.holes) : TryCached(LastList, out result.holes)))
                {
                    result.fromCache = result.offline = true;
                    Debug.LogWarning($"[TrainerHoles] {result.error}; playing {(result.random ? "random cached holes" : "the last top holes from the cache")}.");
                }
                if (result.holes != null)
                {
                    if (roundKey != null) SaveList(roundKey, result.holes);
                    if (!result.fromCache && !result.random) RememberLast(result.holes);
                    RememberPlayed(result.holes);
                }
            }
            finally { busy = false; }
            done(result);
        }

        static IEnumerator FetchOnline(ServerConfig config, int count, bool random, Action<int, int> progress, Func<bool> cancelled, Result result)
        {
            string key = config.TrainerKey;
            if (string.IsNullOrEmpty(key)) { result.error = "No trainer game key (Net/Resources/TrainerKey.txt or GOLF_TRAINER_KEY)"; yield break; }
            string baseUrl = config.trainerUrl.Trim().TrimEnd('/');
            // Random: no repeats within the response; recently played holes only when the pool runs short.
            string url = random ? $"{baseUrl}/api/game/random-holes?count={count}&exclude={Uri.EscapeDataString(string.Join(",", RecentIds()))}"
                                : $"{baseUrl}/api/game/top-holes?limit={count}";

            HoleList list;
            using (var req = Get(url, key, ListTimeout))
            {
                yield return Send(req, cancelled);
                if (req.result != UnityWebRequest.Result.Success) { result.error = $"{(random ? "Random" : "Top")} holes: {req.error}"; yield break; }
                list = JsonUtility.FromJson<HoleList>(req.downloadHandler.text);
            }
            if (list?.holes == null || list.holes.Length == 0) { result.error = $"The trainer has no {(random ? "playable" : "rated")} holes yet"; yield break; }

            var holes = new List<(TrainerHole, string)>();
            for (int i = 0; i < list.holes.Length; i++)
            {
                var hole = list.holes[i];
                progress?.Invoke(i, list.holes.Length);
                string folder = FolderFor(hole.id), failure = null;
                yield return Download(baseUrl, key, hole.id, folder, cancelled, e => failure = e);
                if (cancelled()) yield break;
                if (failure != null) { result.error = $"Hole {hole.id}: {failure}"; yield break; }
                holes.Add((hole, folder));
            }
            result.holes = holes;
        }

        static IEnumerator Download(string baseUrl, string key, string id, string folder, Func<bool> cancelled, Action<string> error)
        {
            if (IsComplete(folder)) yield break; // cached: packages are immutable
            // A folder of our own, so no other download can write into it or delete it.
            string partial = $"{folder}.partial-{Guid.NewGuid():N}";
            Directory.CreateDirectory(partial);
            try
            {
                foreach (var file in Files)
                {
                    using var req = Get($"{baseUrl}/api/game/holes/{Uri.EscapeDataString(id)}/{file}", key, FileTimeout);
                    req.downloadHandler = new DownloadHandlerFile(Path.Combine(partial, file)) { removeFileOnAbort = true };
                    yield return Send(req, cancelled);
                    if (cancelled()) yield break;
                    if (req.result != UnityWebRequest.Result.Success) { error($"{file}: {req.error}"); yield break; }
                }
                if (IsComplete(folder)) yield break; // someone else finished it first
                if (Directory.Exists(folder)) Directory.Delete(folder, true); // an incomplete leftover
                Directory.Move(partial, folder); // only complete packages ever appear in the cache
            }
            finally
            {
                if (Directory.Exists(partial)) Directory.Delete(partial, true);
            }
        }

        /// <summary>Sends a request and waits for it, aborting it if the fetch is cancelled.</summary>
        static IEnumerator Send(UnityWebRequest req, Func<bool> cancelled)
        {
            var op = req.SendWebRequest(); // may throw (e.g. insecure HTTP): SafeCoroutine reports it
            while (!op.isDone)
            {
                if (cancelled()) { req.Abort(); yield break; }
                yield return null;
            }
        }

        static UnityWebRequest Get(string url, string key, int timeout)
        {
            var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("Authorization", "Bearer " + key);
            req.timeout = timeout;
            return req;
        }

        // ---- saved round lists ----

        /// <summary>The holes saved for this round key, if every one of them is still cached.</summary>
        static bool TryCached(string roundKey, out List<(TrainerHole hole, string folder)> holes)
        {
            holes = null;
            string path = ListPath(roundKey);
            if (!File.Exists(path)) return false;
            HoleList saved;
            try { saved = JsonUtility.FromJson<HoleList>(File.ReadAllText(path)); }
            catch (Exception e) { Debug.LogWarning($"[TrainerHoles] Unreadable {path}: {e.Message}"); return false; }
            if (saved?.holes == null || saved.holes.Length == 0) return false;
            var list = new List<(TrainerHole, string)>();
            foreach (var hole in saved.holes)
            {
                string folder = FolderFor(hole.id);
                if (!IsComplete(folder)) return false;
                list.Add((hole, folder));
            }
            holes = list;
            return true;
        }

        /// <summary>The newest top list first, then the older top holes it pushed out (so a short round doesn't shrink it).</summary>
        static void RememberLast(List<(TrainerHole hole, string folder)> holes)
        {
            var merged = new List<(TrainerHole hole, string folder)>(holes);
            if (TryCached(LastList, out var older))
                merged.AddRange(older.FindAll(o => !holes.Exists(h => h.hole.id == o.hole.id)));
            SaveList(LastList, merged.GetRange(0, Math.Min(merged.Count, MaxHoles)));
        }

        static void SaveList(string roundKey, List<(TrainerHole hole, string folder)> holes)
        {
            try { WriteAtomic(ListPath(roundKey), JsonUtility.ToJson(new HoleList { holes = holes.ConvertAll(h => h.hole).ToArray() })); }
            catch (Exception e) { Debug.LogWarning($"[TrainerHoles] Couldn't save the hole list '{roundKey}': {e.Message}"); }
        }

        /// <summary>Writes a file whole or not at all (via a temp file), creating its folder.</summary>
        static void WriteAtomic(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        static string ListPath(string roundKey) => Path.Combine(RoundsFolder, Safe(roundKey) + ".json");

        static string FolderFor(string id) => Path.Combine(CacheFolder, Safe(id));

        static bool IsComplete(string folder) => Array.TrueForAll(Files, f => File.Exists(Path.Combine(folder, f)));

        /// <summary>A file name from any text (ids, server addresses): invalid characters and ':' dropped.</summary>
        public static string Safe(string id) => string.Concat(id.Split(Path.GetInvalidFileNameChars())).Replace(":", "_");
    }
}
