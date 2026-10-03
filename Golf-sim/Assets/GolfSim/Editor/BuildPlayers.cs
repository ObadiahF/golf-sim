// The one build recipe for the players (Windows x64 and macOS). Used by the menu (Golf > Build) and by the publish
// tool through the Unity CLI (Tools/publish/publish.sh):
//   unity command run_script --file Assets/GolfSim/Editor/BuildPlayers.cs --entry BuildPlayers.Build \
//       --args '["windows","2026.10.02-1754-2135252","20261002175400"]' --detach
// Each build embeds its version (Resources/BuildInfo.json, written just before the build and deleted after it) so the
// player can update itself from the game server (GolfSim/Net/Runtime/Update, Game-server/docs/UPDATES.md), and leaves
// the same facts in Builds/<Windows|macOS>.build.json for the publish tool. Afterwards the Editor goes back to macOS.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GolfSim.Net;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class BuildPlayers
{
    const string BuildInfoAsset = "Assets/GolfSim/Net/Resources/" + BuildInfo.ResourceName + ".json";

    /// <summary>What the build tool needs per platform: target, update channel and where the player goes.</summary>
    struct Target
    {
        public BuildTarget target;
        public string channel, folder, player;
    }

    static Target For(string platform) => platform switch
    {
        "windows" => new Target { target = BuildTarget.StandaloneWindows64, channel = "windows-x64", folder = "Builds/Windows", player = "GolfSim.exe" },
        "macos" => new Target { target = BuildTarget.StandaloneOSX, channel = "macos", folder = "Builds/macOS", player = "GolfSim.app" },
        _ => throw new ArgumentException($"Unknown platform '{platform}' (windows or macos)"),
    };

    [MenuItem("Golf/Build/Windows (x64)")]
    static void BuildWindows() => Debug.Log(Build("windows", null, null));

    [MenuItem("Golf/Build/macOS")]
    static void BuildMac() => Debug.Log(Build("macos", null, null));

    /// <summary>
    /// Builds the player for "windows" or "macos". version and build (the monotonic build number) come from the
    /// publish tool; left empty, a local version is made from the time (yyyy.MM.dd-HHmm-local, yyyyMMddHHmmss).
    /// </summary>
    public static string Build(string platform, string version, string build)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before building");
        var t = For(platform);
        var now = DateTime.UtcNow;
        var info = new BuildInfo
        {
            version = string.IsNullOrWhiteSpace(version) ? now.ToString("yyyy.MM.dd-HHmm") + "-local" : version.Trim(),
            build = string.IsNullOrWhiteSpace(build) ? long.Parse(now.ToString("yyyyMMddHHmmss")) : long.Parse(build),
            platform = t.channel,
        };
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0) throw new InvalidOperationException("No scenes are enabled in the build settings");

        var clock = Stopwatch.StartNew();
        File.WriteAllText(BuildInfoAsset, JsonUtility.ToJson(info, true));
        AssetDatabase.ImportAsset(BuildInfoAsset);
        try
        {
            if (t.target == BuildTarget.StandaloneWindows64) UseX64();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(t.folder, t.player),
                target = t.target,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"The {platform} build {report.summary.result}: {report.summary.totalErrors} errors (see the Editor log)");
            File.WriteAllText(t.folder + ".build.json", JsonUtility.ToJson(info, true));
            return $"{info.platform} {info.version} (build {info.build}): {t.folder}/{t.player}, " +
                   $"{report.summary.totalSize / 1048576.0:0} MB in {clock.Elapsed.TotalMinutes:0.0} min";
        }
        finally
        {
            AssetDatabase.DeleteAsset(BuildInfoAsset);
            EditorApplication.delayCall += () => BackToMac();
        }
    }

    /// <summary>
    /// Puts the Editor back on macOS; returns the active target afterwards. Right after a build for another target
    /// Unity still has a deferred platform switch to finish and refuses, so the publish tool calls this again until it
    /// says StandaloneOSX (the build itself also tries on the next Editor tick).
    /// </summary>
    public static string BackToMac()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneOSX && !BuildPipeline.isBuildingPlayer)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX);
        return EditorUserBuildSettings.activeBuildTarget.ToString();
    }

    /// <summary>Unity on this Mac defaults Windows builds to ARM64; the laptop needs x64 (the setting is internal).</summary>
    static void UseX64()
    {
        var type = Type.GetType("UnityEditor.WindowsStandalone.UserBuildSettings, UnityEditor.WindowsStandalone.Extensions");
        var property = type?.GetProperty("architecture", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (property == null) throw new InvalidOperationException("Can't find the Windows architecture setting (is Windows Build Support installed?)");
        var x64 = Enum.Parse(property.PropertyType, "x64");
        property.SetValue(null, x64);
        Debug.Log($"[BuildPlayers] Windows architecture: {property.GetValue(null)}");
    }
}
