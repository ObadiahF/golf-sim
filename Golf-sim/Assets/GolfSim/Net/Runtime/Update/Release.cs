using System;
using System.Collections;
using UnityEngine;

namespace GolfSim.Net
{
    /// <summary>A self-update release from the game server (GET /api/updates/latest, Game-server/docs/UPDATES.md).</summary>
    [Serializable]
    public class Release
    {
        public string platform, version, note, createdAt;
        public long build, totalSize;
        public int fileCount;
        /// <summary>The whole game folder: every file with its size and sha256.</summary>
        public ReleaseFile[] files = new ReleaseFile[0];

        /// <summary>GET /api/updates/latest?platform=: done gets the release, or null when the server has none (204).</summary>
        public static IEnumerator Latest(string platform, Action<Release> done, Action<string> failed) =>
            GameApi.Get($"/api/updates/latest?platform={Uri.EscapeDataString(platform)}",
                        json => done(string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<Release>(json)), failed);

        /// <summary>Where a blob downloads from (the token goes in the Authorization header).</summary>
        public static string BlobUrl(string sha256) => $"{ServerConfig.Load().HttpUrl}/api/updates/blobs/{sha256}";
    }

    /// <summary>One file of a release, relative to the game folder (always with /).</summary>
    [Serializable]
    public class ReleaseFile
    {
        public string path, sha256;
        public long size;
        /// <summary>macOS: the updater sets the file's x bit.</summary>
        public bool executable;
    }
}
