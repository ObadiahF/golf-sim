using System;
using UnityEngine;

namespace GolfSim.Net
{
    /// <summary>
    /// Which build this is, for self-updates: written to Resources/BuildInfo.json by the build script
    /// (GolfSim/Editor/BuildPlayers.cs) right before the player is built, and deleted afterwards, so only players have
    /// it. Without it (the Editor, an old build) the game never updates itself.
    /// </summary>
    [Serializable]
    public class BuildInfo
    {
        public const string ResourceName = "BuildInfo";

        /// <summary>e.g. 2026.10.02-1754-2135252 (date, UTC time, git commit).</summary>
        public string version;
        /// <summary>Monotonic build number (UTC yyyyMMddHHmmss): a release is newer when this is higher.</summary>
        public long build;
        /// <summary>Update channel: windows-x64 or macos.</summary>
        public string platform;

        static BuildInfo current;
        static bool loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => loaded = false;

        /// <summary>This player's build, or null (Editor or a build without it).</summary>
        public static BuildInfo Current
        {
            get
            {
                if (loaded) return current;
                loaded = true;
                var file = Resources.Load<TextAsset>(ResourceName);
                current = file ? JsonUtility.FromJson<BuildInfo>(file.text) : null;
                if (current != null && (string.IsNullOrEmpty(current.version) || string.IsNullOrEmpty(current.platform))) current = null;
                return current;
            }
        }

        /// <summary>"v2026.10.02-1754-2135252", or "development build".</summary>
        public static string Label => Current != null ? "v" + Current.version : "development build";
    }
}
