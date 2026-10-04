using GolfSim.Course;
using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// Who plays next. All are hot-seat with one ball on the course at a time, and every hole starts with the honor:
    /// whoever scored best on the hole before tees off first (Round.TeeOrder).
    /// </summary>
    public enum TurnOrder
    {
        /// <summary>Wii Sports style: each player plays the whole hole, tee to holed or picked up, then the next player.</summary>
        WholeHole,
        /// <summary>Everyone tees off in order, then whoever is farthest from the pin plays next (real golf).</summary>
        FarthestFirst,
        /// <summary>The default: one shot each, round and round in tee order, skipping players who have finished the hole.</summary>
        Alternate,
    }

    /// <summary>
    /// A round: the hole scenes to play and the rules. With fewer scenes than holes the list repeats, so a
    /// 9-hole round works with one hole; add hole scenes (in the build settings) to the list to use them.
    /// The asset at Resources/CourseRound is the one rounds use.
    /// </summary>
    [CreateAssetMenu(menuName = "Golf/Course Round", fileName = "CourseRound")]
    public class CourseRound : ScriptableObject
    {
        public const string ResourceName = "CourseRound";

        [Tooltip("Themes for building the trainer's holes at runtime (Golf > Catalog > Bake Runtime Themes). " +
                 "The hole scenes then only host them. Empty = play the hole scenes as built.")]
        public RuntimeThemeLibrary themes;
        [Tooltip("Hole scenes in order (names as in the build settings). Repeated to fill the round.")]
        public string[] holeScenes = { "HoleSimulator" };
        [Tooltip("Holes in a round when the server doesn't say (it sends holesCount with the game).")]
        [Range(1, 18)] public int holes = 9;
        public TurnOrder turnOrder = TurnOrder.Alternate;
        [Tooltip("A player who hasn't holed out by par + this many strokes picks up and scores that.")]
        [Range(1, 10)] public int maxOverPar = 5;
        [Tooltip("Par used when a hole scene doesn't set one.")]
        [Range(3, 6)] public int defaultPar = 4;
        [Tooltip("Scales each hole's wind (mostly light, now and then over 15 mph); 0 plays without wind.")]
        [Range(0f, 2f)] public float windScale = 1f;
        [Tooltip("Seconds to watch the ball at rest before the next turn.")]
        public float turnDelay = 2.5f;
        [Tooltip("Name of the solo player when a round starts from the menu without a server game.")]
        public string soloPlayer = "Player 1";
        [Tooltip("Scene to return to after the round.")]
        public string menuScene = "MainMenu";

        [Header("HUD")]
        public PanelSettings panelSettings;
        public VisualTreeAsset hudLayout;

        static CourseRound loaded;

        public static CourseRound Load()
        {
            if (!loaded) loaded = Resources.Load<CourseRound>(ResourceName);
            if (!loaded) loaded = CreateInstance<CourseRound>();
            return loaded;
        }

        /// <summary>Scene for a 0-based hole index, cycling through the list.</summary>
        public string SceneFor(int holeIndex) => holeScenes.Length == 0 ? null : holeScenes[holeIndex % holeScenes.Length];

        public int ParFor(int scenePar) => scenePar >= 3 && scenePar <= 6 ? scenePar : defaultPar;
    }
}
