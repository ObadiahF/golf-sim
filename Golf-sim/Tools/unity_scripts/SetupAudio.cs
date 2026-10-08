// Dev helper (Edit mode), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/SetupAudio.cs --entry SetupAudio.Run
// Creates or refreshes Assets/GolfSim/Game/Resources/AudioCatalog.asset from the clips in Assets/GolfSim/Game/Audio:
// CC0/ (downloaded recordings, see CC0/SOURCES.txt) and Synth/ (Tools/audio/synth_sounds.py). For each sound the
// first pattern with any matching clips wins (e.g. a recording of a cheer, else the applause). Also sets the clips'
// import settings (one-shots decompressed in memory, long crowd and ambience clips compressed / streamed).
// Volumes and pitch ranges already tuned in the asset are kept; only the clip lists are rebuilt.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GolfSim.Game;
using UnityEditor;
using UnityEngine;

public static class SetupAudio
{
    const string AudioRoot = "Assets/GolfSim/Game/Audio";
    const string CatalogPath = "Assets/GolfSim/Game/Resources/AudioCatalog.asset";

    struct Spec
    {
        public SoundId id;
        public SoundBus bus;
        public bool spatial;
        public float minDistance, volume;
        public Vector2 pitch;
        public string[] patterns; // regexes on "Folder/name", in order of preference
    }

    static Spec S(SoundId id, SoundBus bus, bool spatial, float volume, float pitchSpread, float minDistance, params string[] patterns) =>
        new Spec { id = id, bus = bus, spatial = spatial, volume = volume, pitch = new Vector2(1f - pitchSpread, 1f + pitchSpread),
                   minDistance = minDistance, patterns = patterns };

    static readonly Spec[] Specs =
    {
        // Strikes: per club family, every strike quality (strike_iron_pure_1, strike_iron_thin_2...: StrikeSound picks one).
        S(SoundId.StrikeDriver, SoundBus.Sfx, true, 1f, 0.03f, 18f, @"^CC0/strike_driver_", @"^Synth/strike_driver_"),
        S(SoundId.StrikeWood, SoundBus.Sfx, true, 1f, 0.03f, 18f, @"^CC0/strike_wood_", @"^Synth/strike_wood_"),
        S(SoundId.StrikeIron, SoundBus.Sfx, true, 0.95f, 0.04f, 16f, @"^CC0/strike_iron_", @"^Synth/strike_iron_"),
        S(SoundId.StrikeWedge, SoundBus.Sfx, true, 0.95f, 0.04f, 14f, @"^CC0/strike_wedge_", @"^Synth/strike_wedge_"),
        S(SoundId.StrikePutter, SoundBus.Sfx, true, 0.8f, 0.04f, 8f, @"^CC0/strike_putter_", @"^Synth/strike_putter_"),
        // Landings carry from far down the fairway; the splash and the cup are heard wherever the camera is (like a
        // broadcast's course mics: the moment matters more than the distance).
        S(SoundId.LandGrass, SoundBus.Sfx, true, 0.75f, 0.08f, 30f, @"^CC0/land_grass_\d", @"^Synth/land_grass_\d"),
        S(SoundId.LandSand, SoundBus.Sfx, true, 0.8f, 0.08f, 30f, @"^CC0/land_sand_\d", @"^Synth/land_sand_\d"),
        S(SoundId.LandGreen, SoundBus.Sfx, true, 0.75f, 0.06f, 30f, @"^CC0/land_green_\d", @"^Synth/land_green_\d"),
        S(SoundId.TreeTrunk, SoundBus.Sfx, true, 1f, 0.08f, 12f, @"^CC0/wood_knock_\d"),
        S(SoundId.TreeLeaves, SoundBus.Sfx, true, 0.9f, 0.1f, 12f, @"^CC0/leaves_\d"),
        S(SoundId.Rock, SoundBus.Sfx, true, 0.9f, 0.08f, 12f, @"^CC0/rock_clack_\d"),
        S(SoundId.Water, SoundBus.Sfx, false, 1f, 0.06f, 14f, @"^CC0/splash_\d"),
        S(SoundId.Cup, SoundBus.Sfx, false, 1f, 0.05f, 10f, @"^CC0/cup_rattle_\d"),
        S(SoundId.CrowdApplause, SoundBus.Crowd, false, 0.85f, 0.03f, 10f, @"^CC0/crowd_applause_\d"),
        S(SoundId.CrowdCheer, SoundBus.Crowd, false, 0.95f, 0.03f, 10f, @"^CC0/crowd_cheer_\d", @"^CC0/crowd_applause_\d"),
        S(SoundId.CrowdRoar, SoundBus.Crowd, false, 0.8f, 0.02f, 10f, @"^CC0/crowd_roar_\d", @"^CC0/crowd_cheer_\d"),
        S(SoundId.CrowdOoh, SoundBus.Crowd, false, 0.85f, 0.04f, 10f, @"^CC0/crowd_ooh_\d"),
        S(SoundId.CrowdGroan, SoundBus.Crowd, false, 0.85f, 0.04f, 10f, @"^CC0/crowd_groan_\d", @"^CC0/crowd_ooh_\d"),
        S(SoundId.UiSwoosh, SoundBus.Ui, false, 0.7f, 0.05f, 10f, @"^CC0/ui_swoosh_\d"),
        S(SoundId.UiMove, SoundBus.Ui, false, 0.5f, 0.03f, 10f, @"^CC0/ui_tick_\d"),
        S(SoundId.UiSelect, SoundBus.Ui, false, 0.6f, 0.02f, 10f, @"^CC0/ui_select_\d"),
        S(SoundId.UiBack, SoundBus.Ui, false, 0.55f, 0.02f, 10f, @"^CC0/ui_back_\d"),
        S(SoundId.ReplaySting, SoundBus.Ui, false, 0.7f, 0f, 10f, @"^CC0/replay_sting_\d", @"^Synth/replay_sting_\d"),
        S(SoundId.AmbienceBirds, SoundBus.Ambience, false, 0.8f, 0f, 10f, @"^CC0/amb_birds_\d"),
        S(SoundId.AmbienceWind, SoundBus.Ambience, false, 0.6f, 0f, 10f, @"^CC0/amb_wind_\d"),
        // Celebrations (HoleCelebration): jingles everywhere, fireworks over the green.
        S(SoundId.JingleBirdie, SoundBus.Sfx, false, 0.8f, 0f, 10f, @"^Synth/jingle_birdie_\d"),
        S(SoundId.JingleEagle, SoundBus.Sfx, false, 0.85f, 0f, 10f, @"^Synth/jingle_eagle_\d"),
        S(SoundId.JingleAce, SoundBus.Sfx, false, 0.9f, 0f, 10f, @"^Synth/jingle_ace_\d"),
        S(SoundId.FireworkLaunch, SoundBus.Sfx, false, 0.55f, 0.08f, 10f, @"^Synth/firework_launch_\d"),
        S(SoundId.FireworkBurst, SoundBus.Sfx, false, 0.8f, 0.1f, 10f, @"^Synth/firework_burst_\d"),
    };

    public static string Run()
    {
        AssetDatabase.Refresh();
        var clips = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .ToDictionary(p => p.Substring(AudioRoot.Length + 1).Replace(".wav", "").Replace(".ogg", ""), p => p);
        foreach (var path in clips.Values) Import(path);

        var catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
        if (!catalog)
        {
            catalog = ScriptableObject.CreateInstance<AudioCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        var log = new StringBuilder();
        var sounds = new List<AudioCatalog.Sound>();
        foreach (var spec in Specs)
        {
            var existing = catalog.Find(spec.id);
            var sound = existing ?? new AudioCatalog.Sound { id = spec.id, volume = new Vector2(spec.volume * 0.9f, spec.volume), pitch = spec.pitch };
            sound.bus = spec.bus;
            sound.spatial = spec.spatial;
            sound.minDistance = spec.minDistance;
            sound.clips = new AudioClip[0];
            foreach (var pattern in spec.patterns)
            {
                var found = clips.Keys.Where(k => Regex.IsMatch(k, pattern)).OrderBy(k => k).ToArray();
                if (found.Length == 0) continue;
                sound.clips = found.Select(k => AssetDatabase.LoadAssetAtPath<AudioClip>(clips[k])).ToArray();
                break;
            }
            sounds.Add(sound);
            log.AppendLine($"{spec.id}: {(sound.clips.Length == 0 ? "NONE" : string.Join(", ", sound.clips.Select(c => c.name)))}");
        }
        catalog.sounds = sounds.ToArray();
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    static void Import(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (!importer) return;
        string name = Path.GetFileName(path);
        bool longClip = name.StartsWith("amb_") || name.StartsWith("crowd_") || name.StartsWith("jingle_");
        var before = importer.defaultSampleSettings;
        var settings = before;
        settings.loadType = name.StartsWith("amb_") ? AudioClipLoadType.Streaming
                          : longClip ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = longClip ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
        settings.quality = 0.7f;
        settings.preloadAudioData = !name.StartsWith("amb_");
        bool mono = !name.StartsWith("amb_");
        if (before.loadType == settings.loadType && before.compressionFormat == settings.compressionFormat &&
            before.preloadAudioData == settings.preloadAudioData && importer.forceToMono == mono) return; // already set (keeps reruns fast)
        importer.defaultSampleSettings = settings;
        importer.forceToMono = mono;
        importer.loadInBackground = longClip;
        importer.SaveAndReimport();
    }
}
