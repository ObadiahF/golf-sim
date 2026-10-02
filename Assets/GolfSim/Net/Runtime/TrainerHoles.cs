using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace GolfSim.Net
{
    /// <summary>
    /// Downloads the Course Trainer's top-rated holes (its read-only Game API) into a local cache. Packages never
    /// change once rated, so a cached hole is reused forever. See Tools/course_trainer/README.md, "Game API".
    /// </summary>
    public static class TrainerHoles
    {
        /// <summary>Files a hole needs to be built (Docs/hole-format); gen.json and preview.png are optional.</summary>
        static readonly string[] Files = { "hole.json", "heightmap.raw", "objects.bin" };

        [Serializable]
        public class TopHole
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
        class TopHoles
        {
            public string formula;
            public TopHole[] holes;
        }

        public static string CacheFolder => Path.Combine(Application.persistentDataPath, "holes");

        /// <summary>
        /// Fetches the top `count` holes and makes sure each is cached. Calls done with (hole, local folder) pairs
        /// in rank order, or error with a message. Run with StartCoroutine.
        /// </summary>
        public static IEnumerator FetchTop(ServerConfig config, int count, Action<List<(TopHole hole, string folder)>> done,
                                           Action<string> error)
        {
            string key = config.TrainerKey;
            if (string.IsNullOrEmpty(key)) { error("No trainer game key (Net/Resources/TrainerKey.txt or GOLF_TRAINER_KEY)"); yield break; }
            string baseUrl = config.trainerUrl.Trim().TrimEnd('/');

            TopHoles top = null;
            using (var req = Get($"{baseUrl}/api/game/top-holes?limit={count}", key))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { error($"Top holes: {req.error}"); yield break; }
                top = JsonUtility.FromJson<TopHoles>(req.downloadHandler.text);
            }
            if (top?.holes == null || top.holes.Length == 0) { error("The trainer has no rated holes yet"); yield break; }

            var result = new List<(TopHole, string)>();
            foreach (var hole in top.holes)
            {
                string folder = Path.Combine(CacheFolder, Safe(hole.id));
                string failure = null;
                yield return Download(baseUrl, key, hole.id, folder, e => failure = e);
                if (failure != null) { error($"Hole {hole.id}: {failure}"); yield break; }
                result.Add((hole, folder));
            }
            done(result);
        }

        static IEnumerator Download(string baseUrl, string key, string id, string folder, Action<string> error)
        {
            if (Array.TrueForAll(Files, f => File.Exists(Path.Combine(folder, f)))) yield break; // cached: packages are immutable
            string partial = folder + ".partial";
            if (Directory.Exists(partial)) Directory.Delete(partial, true);
            Directory.CreateDirectory(partial);
            foreach (var file in Files)
            {
                using var req = Get($"{baseUrl}/api/game/holes/{Uri.EscapeDataString(id)}/{file}", key);
                req.downloadHandler = new DownloadHandlerFile(Path.Combine(partial, file)) { removeFileOnAbort = true };
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { error($"{file}: {req.error}"); yield break; }
            }
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.Move(partial, folder); // only complete packages ever appear in the cache
        }

        static UnityWebRequest Get(string url, string key)
        {
            var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("Authorization", "Bearer " + key);
            req.timeout = 60;
            return req;
        }

        static string Safe(string id) => string.Concat(id.Split(Path.GetInvalidFileNameChars()));
    }
}
