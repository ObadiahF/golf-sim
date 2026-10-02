using GolfSim.Course;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GolfSim.Game
{
    /// <summary>
    /// The game's sound: plays catalog sounds (AudioCatalog) from a pool of sources, 3D for ball sounds and 2D for the
    /// crowd and UI, with master / SFX / crowd / UI / ambience volumes saved in PlayerPrefs, and an ambient bed of birds
    /// and wind on the holes. What triggers the sounds lives in ShotSounds (the ball), CrowdReactions (the gallery) and
    /// UiSounds (menus and banners), which only listen to game events: no sound code in the gameplay.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        const int Voices = 24;
        const string PrefsKey = "GolfSim.Volume.";

        public static GameAudio Instance { get; private set; }

        public AudioCatalog catalog;

        AudioSource[] voices;
        int nextVoice;
        AudioSource birds, wind;
        AudioListener listener;

        void Awake()
        {
            Instance = this;
            if (!catalog) catalog = AudioCatalog.Load();
            voices = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++) voices[i] = NewSource($"Voice {i}");
            birds = Loop(SoundId.AmbienceBirds);
            wind = Loop(SoundId.AmbienceWind);
            listener = gameObject.AddComponent<AudioListener>();
            listener.enabled = false; // until we know the scene has none
            gameObject.AddComponent<ShotSounds>();
            gameObject.AddComponent<CrowdReactions>();
            gameObject.AddComponent<UiSounds>();
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this) Instance = null;
        }

        // ---- volumes ----

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(PrefsKey + "Master", Instance ? Instance.catalog.master : 1f);
            set => Save("Master", value);
        }

        /// <summary>A group's volume (0..1) before the master volume.</summary>
        public static float GetVolume(SoundBus bus) =>
            PlayerPrefs.GetFloat(PrefsKey + bus, Instance ? Instance.catalog.DefaultVolume(bus) : 1f);

        public static void SetVolume(SoundBus bus, float value) => Save(bus.ToString(), value);

        static void Save(string key, float value)
        {
            PlayerPrefs.SetFloat(PrefsKey + key, Mathf.Clamp01(value));
            if (Instance) Instance.UpdateAmbience();
        }

        static float Gain(SoundBus bus) => MasterVolume * GetVolume(bus);

        // ---- playing ----

        /// <summary>
        /// Plays a sound: at `position` if the catalog says it is spatial (else everywhere), louder or softer by
        /// `volume`, higher or lower by `pitch`. Missing sounds are silently skipped.
        /// </summary>
        public static void Play(SoundId id, Vector3? position = null, float volume = 1f, float pitch = 1f, bool flat = false)
        {
            if (!Instance) return;
            var sound = Instance.catalog.Find(id);
            if (sound == null || sound.clips.Length == 0 || volume <= 0f) return;
            var clip = sound.clips[Random.Range(0, sound.clips.Length)];
            if (!clip) return;
            var source = Instance.NextVoice();
            bool spatial = sound.spatial && position.HasValue && !flat;
            source.transform.position = position ?? Instance.transform.position;
            source.spatialBlend = spatial ? 1f : 0f;
            source.minDistance = sound.minDistance;
            source.pitch = Random.Range(sound.pitch.x, sound.pitch.y) * pitch;
            source.volume = Mathf.Clamp01(Random.Range(sound.volume.x, sound.volume.y) * volume * Gain(sound.bus));
            source.clip = clip;
            source.Play();
        }

        AudioSource NextVoice()
        {
            // Round robin, but prefer a free voice so a long crowd clip isn't cut off by a burst of bounces.
            for (int i = 0; i < voices.Length; i++)
            {
                var v = voices[(nextVoice + i) % voices.Length];
                if (!v.isPlaying)
                {
                    nextVoice = (nextVoice + i + 1) % voices.Length;
                    return v;
                }
            }
            var oldest = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Length;
            return oldest;
        }

        AudioSource NewSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.maxDistance = 600f;
            source.dopplerLevel = 0f;
            return source;
        }

        // ---- ambience and the listener ----

        AudioSource Loop(SoundId id)
        {
            var source = NewSource(id.ToString());
            source.loop = true;
            source.spatialBlend = 0f;
            return source;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Hear the game from the scene's camera if it has a listener; scenes without one (the menu) use ours.
            bool sceneHasOne = System.Array.Exists(FindObjectsByType<AudioListener>(),
                                                   l => l != listener && l.isActiveAndEnabled);
            listener.enabled = !sceneHasOne;
            UpdateAmbience();
        }

        void UpdateAmbience()
        {
            bool onCourse = FindAnyObjectByType<HoleInfo>();
            Ambience(birds, SoundId.AmbienceBirds, onCourse);
            Ambience(wind, SoundId.AmbienceWind, onCourse);
        }

        void Ambience(AudioSource source, SoundId id, bool play)
        {
            var sound = catalog.Find(id);
            if (!play || sound == null || sound.clips.Length == 0)
            {
                source.Stop();
                return;
            }
            if (!source.isPlaying || !System.Array.Exists(sound.clips, c => c == source.clip))
            {
                source.clip = sound.clips[Random.Range(0, sound.clips.Length)];
                source.time = Random.Range(0f, source.clip.length * 0.9f);
                source.Play();
            }
            source.volume = sound.volume.y * Gain(SoundBus.Ambience);
        }
    }
}
