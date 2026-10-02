using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// One channel on the main menu. Create one per game mode (Assets > Create > Golf > Game Mode),
    /// add its scene to the build settings and drop the asset into MainMenu.modes.
    /// </summary>
    [CreateAssetMenu(menuName = "Golf/Game Mode", fileName = "GameMode")]
    public class GameMode : ScriptableObject
    {
        public enum ModeKind
        {
            /// <summary>Loads sceneName.</summary>
            Scene,
            /// <summary>A multi-hole round (RoundDirector): resumes the server's game in progress, else a solo round of 9 or 18.</summary>
            Round,
            /// <summary>Opens the Scores screen (everyone's stats from the game server).</summary>
            Scores,
        }

        public ModeKind kind = ModeKind.Scene;
        public string title = "New Mode";
        [TextArea(2, 4)] public string description;
        [Tooltip("Scene to load (must be in the build settings).")]
        public string sceneName;
        [Tooltip("Channel art, shown on the tile and the preview screen.")]
        public Texture2D banner;
    }
}
