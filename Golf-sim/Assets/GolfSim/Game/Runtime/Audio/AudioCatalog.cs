using System;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>Every sound the game makes. GameAudio plays them by id; AudioCatalog says which clips and how.</summary>
    public enum SoundId
    {
        StrikeDriver, StrikeIron, StrikePutter,
        LandGrass, LandSand, LandGreen,
        TreeTrunk, TreeLeaves, Rock, Water, Cup,
        CrowdApplause, CrowdCheer, CrowdRoar, CrowdOoh, CrowdGroan,
        UiSwoosh, UiMove, UiSelect, UiBack, ReplaySting,
        AmbienceBirds, AmbienceWind,
    }

    /// <summary>Volume groups the player can set separately (with the master volume over all of them).</summary>
    public enum SoundBus { Sfx, Crowd, Ui, Ambience }

    /// <summary>
    /// Maps each sound to its clips (one is picked at random each time), a volume and pitch range for variation, its
    /// volume group and whether it is heard from where it happens (3D) or everywhere (2D). The asset at
    /// Resources/AudioCatalog is the one the game uses; Tools/unity_scripts/SetupAudio.cs fills it from the clip folders.
    /// </summary>
    [CreateAssetMenu(menuName = "Golf/Audio Catalog", fileName = "AudioCatalog")]
    public class AudioCatalog : ScriptableObject
    {
        public const string ResourceName = "AudioCatalog";

        [Serializable]
        public class Sound
        {
            public SoundId id;
            public SoundBus bus;
            public AudioClip[] clips = new AudioClip[0];
            [Tooltip("Volume, random in this range (before the bus and master volumes).")]
            public Vector2 volume = new Vector2(0.9f, 1f);
            [Tooltip("Pitch, random in this range.")]
            public Vector2 pitch = new Vector2(0.95f, 1.05f);
            [Tooltip("Heard from where it happens (ball sounds) rather than everywhere (crowd, UI, ambience).")]
            public bool spatial;
            [Tooltip("3D: full volume within this distance, m.")]
            public float minDistance = 12f;
        }

        [Header("Default volumes (players' settings override them)")]
        [Range(0f, 1f)] public float master = 1f;
        [Range(0f, 1f)] public float sfx = 0.9f;
        [Range(0f, 1f)] public float crowd = 0.75f;
        [Range(0f, 1f)] public float ui = 0.6f;
        [Range(0f, 1f)] public float ambience = 0.35f;

        public Sound[] sounds = new Sound[0];

        public Sound Find(SoundId id) => Array.Find(sounds, s => s.id == id);

        public float DefaultVolume(SoundBus bus) => bus switch
        {
            SoundBus.Crowd => crowd,
            SoundBus.Ui => ui,
            SoundBus.Ambience => ambience,
            _ => sfx,
        };

        static AudioCatalog loaded;

        public static AudioCatalog Load()
        {
            if (!loaded) loaded = Resources.Load<AudioCatalog>(ResourceName);
            if (!loaded) loaded = CreateInstance<AudioCatalog>();
            return loaded;
        }
    }
}
