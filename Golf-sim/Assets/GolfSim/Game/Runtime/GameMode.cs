using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// One card on the main menu (MainMenu.modes) or on its Practice screen (MainMenu.practice, developerModes).
    /// Create one per game mode (Assets > Create > Golf > Game Mode), add its scene to the build settings and drop the
    /// asset into one of those lists.
    /// </summary>
    [CreateAssetMenu(menuName = "Golf/Game Mode", fileName = "GameMode")]
    public class GameMode : ScriptableObject
    {
        public enum ModeKind
        {
            /// <summary>Loads sceneName.</summary>
            Scene,
            /// <summary>Play: resumes the server's game in progress, else opens "Choose a course" for a solo round (CourseCatalog).</summary>
            Round,
            /// <summary>Opens the Scores screen (everyone's stats from the game server).</summary>
            Scores,
            /// <summary>Opens the Settings screen (sound, gameplay, display...).</summary>
            Settings,
            /// <summary>Installs a newer version from the game server (UpdateFlow; the menu adds this card itself).</summary>
            Update,
            /// <summary>Opens the Practice screen (the practice cards: driving range, putting green...).</summary>
            Practice,
        }

        public ModeKind kind = ModeKind.Scene;
        public string title = "New Mode";
        [TextArea(2, 4)] public string description;
        [Tooltip("Scene to load (must be in the build settings).")]
        public string sceneName;
        [Tooltip("Scene modes: a practice facility built into the practice scene in place of its hole (Hole: play the scene's own).")]
        public PracticeMode practice;
        [Tooltip("Channel art, shown on the tile and the preview screen.")]
        public Texture2D banner;
    }
}
