using System;
using System.IO;
using UnityEngine;

namespace GolfSim.Net
{
    /// <summary>
    /// The installed game, as a release's manifest describes it. Windows: the folder with GolfSim.exe and GolfSim_Data;
    /// macOS: the GolfSim.app bundle (paths start with Contents/). Manifest paths are checked again here (the server
    /// already refuses unsafe ones), so nothing is ever written or deleted outside this folder.
    /// </summary>
    public static class InstallFolder
    {
        /// <summary>True on a player this updater can replace (a Windows or macOS build, not the Editor).</summary>
        public static bool Supported => !Application.isEditor &&
            (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.OSXPlayer);

        public static bool IsWindows => Application.platform == RuntimePlatform.WindowsPlayer;

        /// <summary>The game folder (Windows) or the .app bundle (macOS), without a trailing separator.</summary>
        public static string Root => Path.GetFullPath(Path.GetDirectoryName(Application.dataPath.TrimEnd('/', '\\')));

        /// <summary>What the updater starts again: GolfSim.exe (named after the GolfSim_Data folder), or the .app.</summary>
        public static string Launcher
        {
            get
            {
                if (!IsWindows) return Root;
                string data = Path.GetFileName(Application.dataPath.TrimEnd('/', '\\'));
                return Path.Combine(Root, data.Substring(0, data.Length - "_Data".Length) + ".exe");
            }
        }

        /// <summary>Where updates are staged and logged; outside the game folder.</summary>
        public static string UpdatesFolder => Path.Combine(Application.persistentDataPath, "updates");

        /// <summary>The full path of a manifest path under root; throws for anything that would leave it.</summary>
        public static string FullPath(string root, string relative)
        {
            if (!IsSafe(relative)) throw new ArgumentException($"Unsafe path in the update: {relative}");
            string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"Path leaves the game folder: {relative}");
            return full;
        }

        /// <summary>Relative, / separated, no empty / . / .. segments, no \ : or control characters (as the server checks).</summary>
        public static bool IsSafe(string relative)
        {
            if (string.IsNullOrEmpty(relative) || relative.Length > 400 || relative[0] == '/') return false;
            foreach (char c in relative)
                if (c == '\\' || c == ':' || c == '*' || c == '?' || c == '"' || c == '<' || c == '>' || c == '|' || char.IsControl(c)) return false;
            foreach (var segment in relative.Split('/'))
                if (segment.Length == 0 || segment == "." || segment == ".." || segment.EndsWith(".") || segment.EndsWith(" ")) return false;
            return true;
        }

        /// <summary>The manifest path (with /) of a file under root.</summary>
        public static string Relative(string root, string full) =>
            full.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace(Path.DirectorySeparatorChar, '/');
    }
}
