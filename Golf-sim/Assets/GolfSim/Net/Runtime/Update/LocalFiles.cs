using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using UnityEngine;

namespace GolfSim.Net
{
    /// <summary>
    /// sha256 of installed files, cached by path, size and modification time in updates/hashes.json, so after the
    /// first check (a few seconds for the whole game) only changed files are hashed again. Thread-safe to call from a
    /// worker thread: it touches no Unity API.
    /// </summary>
    public static class LocalFiles
    {
        [Serializable] class Entry { public string path, sha256; public long size, ticks; }
        [Serializable] class Cache { public List<Entry> files = new List<Entry>(); }

        /// <summary>
        /// The sha256 of each wanted file that exists with the wanted size (a file of another size has changed anyway,
        /// so it isn't hashed). progress(bytes hashed, bytes to hash).
        /// </summary>
        public static Dictionary<string, string> Hashes(string root, IList<ReleaseFile> wanted, string cachePath,
                                                        Action<long, long> progress, CancellationToken cancel)
        {
            var cached = Load(cachePath);
            var result = new Dictionary<string, string>();
            var toHash = new List<(ReleaseFile file, FileInfo info)>();
            long total = 0, done = 0;
            foreach (var file in wanted)
            {
                var info = new FileInfo(InstallFolder.FullPath(root, file.path));
                if (!info.Exists || info.Length != file.size) continue;
                if (cached.TryGetValue(file.path, out var entry) && entry.size == info.Length && entry.ticks == info.LastWriteTimeUtc.Ticks)
                    result[file.path] = entry.sha256;
                else
                {
                    toHash.Add((file, info));
                    total += info.Length;
                }
            }
            foreach (var (file, info) in toHash)
            {
                cancel.ThrowIfCancellationRequested();
                string sha = Sha256(info.FullName, cancel, n => progress?.Invoke(done + n, total));
                done += info.Length;
                result[file.path] = sha;
                cached[file.path] = new Entry { path = file.path, sha256 = sha, size = info.Length, ticks = info.LastWriteTimeUtc.Ticks };
            }
            if (toHash.Count > 0) Save(cachePath, cached);
            return result;
        }

        /// <summary>
        /// Files under root that the release doesn't list, inside folders the release owns (a top-level folder with
        /// files in the manifest, e.g. GolfSim_Data/ or the bundle's Contents/). Loose files next to GolfSim.exe and
        /// other folders are never touched: the game might have been unzipped onto the desktop.
        /// </summary>
        public static List<string> Extra(string root, IList<ReleaseFile> files)
        {
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                listed.Add(file.path);
                int slash = file.path.IndexOf('/');
                if (slash > 0) owned.Add(file.path.Substring(0, slash));
            }
            var extra = new List<string>();
            foreach (var folder in owned)
            {
                string full = Path.Combine(root, folder);
                if (!Directory.Exists(full)) continue;
                foreach (var path in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                {
                    string relative = InstallFolder.Relative(root, path);
                    if (!listed.Contains(relative) && InstallFolder.IsSafe(relative)) extra.Add(relative);
                }
            }
            return extra;
        }

        /// <summary>The file's sha256 as lowercase hex; progress(bytes read so far).</summary>
        public static string Sha256(string path, CancellationToken cancel, Action<long> progress = null)
        {
            using var sha = SHA256.Create();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20);
            var buffer = new byte[1 << 20];
            long read = 0;
            for (int n; (n = stream.Read(buffer, 0, buffer.Length)) > 0;)
            {
                cancel.ThrowIfCancellationRequested();
                sha.TransformBlock(buffer, 0, n, null, 0);
                read += n;
                progress?.Invoke(read);
            }
            sha.TransformFinalBlock(buffer, 0, 0);
            return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
        }

        static Dictionary<string, Entry> Load(string cachePath)
        {
            var map = new Dictionary<string, Entry>();
            try
            {
                if (File.Exists(cachePath))
                    foreach (var e in JsonUtility.FromJson<Cache>(File.ReadAllText(cachePath)).files)
                        map[e.path] = e;
            }
            catch (Exception e) { Debug.LogWarning($"[Updates] Ignoring the hash cache: {e.Message}"); }
            return map;
        }

        static void Save(string cachePath, Dictionary<string, Entry> map)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
            File.WriteAllText(cachePath, JsonUtility.ToJson(new Cache { files = new List<Entry>(map.Values) }));
        }
    }
}
