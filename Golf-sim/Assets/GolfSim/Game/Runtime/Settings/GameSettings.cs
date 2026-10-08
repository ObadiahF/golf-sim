using System;
using GolfSim.Ball;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>How hard each hole's wind blows (a scale on CourseRound.windScale).</summary>
    public enum WindStrength { Off, Light, Normal, Strong }

    /// <summary>High: the High Fidelity quality level. Performance: the Performant one (GolfSim_URP_Performant) for weaker PCs.</summary>
    public enum GraphicsQuality { High, Performance }

    /// <summary>
    /// The player's settings (the Settings screen), saved in PlayerPrefs and applied as soon as they change: wind
    /// strength, putting assist (PuttPreview.Assist, which keeps its own key), the default round length, the graphics
    /// quality, fullscreen and whether the developer modes (Hole Simulator) show under Practice. Volumes are GameAudio's.
    /// Quality (and fullscreen, once chosen) are applied at startup too.
    /// </summary>
    public static class GameSettings
    {
        const string Prefix = "GolfSim.Settings.";
        const string WindKey = Prefix + "Wind", HolesKey = Prefix + "RoundLength", QualityKey = Prefix + "Quality",
                     FullscreenKey = Prefix + "Fullscreen", DeveloperKey = Prefix + "DeveloperModes";

        public static readonly int[] RoundLengths = { 9, 18 };
        /// <summary>WindStrength → multiplier of each hole's wind (Off plays calm).</summary>
        static readonly float[] WindScales = { 0f, 0.5f, 1f, 1.5f };
        /// <summary>GraphicsQuality → QualitySettings level (ProjectSettings: High Fidelity, Balanced, Performant).</summary>
        static readonly string[] QualityLevels = { "High Fidelity", "Performant" };

        /// <summary>A setting changed (after it was saved and applied).</summary>
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Changed = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplyAtStartup()
        {
            ApplyQuality();
            if (PlayerPrefs.HasKey(FullscreenKey)) ApplyFullscreen(); // until it is chosen, the window is the player's own
        }

        public static WindStrength Wind
        {
            get => (WindStrength)GetEnum(WindKey, (int)WindStrength.Normal, WindScales.Length);
            set => Save(WindKey, (int)value);
        }

        /// <summary>The wind setting as a multiplier (0, 0.5, 1, 1.5).</summary>
        public static float WindScale => WindScales[(int)Wind];

        public static PuttingAssist PuttingAssist
        {
            get => PuttPreview.Assist;
            set
            {
                if (value == PuttPreview.Assist) return;
                PuttPreview.Assist = value; // saves itself and redraws the green
                Changed?.Invoke();
            }
        }

        /// <summary>Holes in a round started on the TV: 9 or 18 (the course screen's Holes choice is this setting).</summary>
        public static int RoundLength
        {
            get => PlayerPrefs.GetInt(HolesKey, RoundLengths[0]) == RoundLengths[1] ? RoundLengths[1] : RoundLengths[0];
            set => Save(HolesKey, value == RoundLengths[1] ? RoundLengths[1] : RoundLengths[0]);
        }

        public static GraphicsQuality Quality
        {
            get => (GraphicsQuality)GetEnum(QualityKey, (int)GraphicsQuality.High, QualityLevels.Length);
            set => Save(QualityKey, (int)value, ApplyQuality);
        }

        public static bool Fullscreen
        {
            get => PlayerPrefs.HasKey(FullscreenKey) ? PlayerPrefs.GetInt(FullscreenKey) != 0 : Screen.fullScreenMode != FullScreenMode.Windowed;
            set => Save(FullscreenKey, value ? 1 : 0, ApplyFullscreen);
        }

        /// <summary>Shows the developer modes (Hole Simulator, for the AI course work) under Practice.</summary>
        public static bool DeveloperModes
        {
            get => PlayerPrefs.GetInt(DeveloperKey, 0) != 0;
            set => Save(DeveloperKey, value ? 1 : 0);
        }

        /// <summary>The quality level index the Quality setting stands for (-1 if the project has no such level).</summary>
        public static int QualityLevel(GraphicsQuality quality) => Array.IndexOf(QualitySettings.names, QualityLevels[(int)quality]);

        static void ApplyQuality()
        {
            int level = QualityLevel(Quality);
            if (level >= 0 && level != QualitySettings.GetQualityLevel()) QualitySettings.SetQualityLevel(level, true);
        }

        static void ApplyFullscreen()
        {
            if (Application.isEditor) return; // the Game view has no fullscreen
            Screen.fullScreenMode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        }

        static int GetEnum(string key, int fallback, int count) => Mathf.Clamp(PlayerPrefs.GetInt(key, fallback), 0, count - 1);

        static void Save(string key, int value, Action apply = null)
        {
            if (PlayerPrefs.HasKey(key) && PlayerPrefs.GetInt(key) == value) return;
            PlayerPrefs.SetInt(key, value);
            PlayerPrefs.Save();
            apply?.Invoke();
            Changed?.Invoke();
        }
    }
}
