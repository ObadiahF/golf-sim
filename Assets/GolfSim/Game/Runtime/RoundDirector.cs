using System;
using System.Linq;
using GolfSim.Ball;
using GolfSim.Course;
using GolfSim.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// The game session, alive across scenes: owns the game-server connection, turns remote messages into
    /// input (nav keys, shots, club, aim), runs multi-hole rounds (gameStarted from the phone, or "Play a
    /// Round" on the menu) and keeps the phones told what the sim shows ("state"). Hole play itself (turns,
    /// shots, penalties, scores) is in RoundDirector.Play.cs. Creates itself when Play starts.
    /// </summary>
    public partial class RoundDirector : MonoBehaviour
    {
        enum Phase { Menu, Loading, Playing, BetweenShots, HoleSummary, Finished }

        public static RoundDirector Instance { get; private set; }

        /// <summary>A player's turn began (sent to the phones as "turn"; the HUD announces it from this too).</summary>
        public event Action<TurnMessage> TurnStarted;

        public CourseRound course;

        /// <summary>The round being played, or null (menu or practice).</summary>
        public Round Round => round;
        public SimConnection Connection => connection;
        /// <summary>The server's game in progress (from hello / gameStarted / scorecard), if any.</summary>
        public GameView ServerGame => serverGame;
        public string ScreenName => phase switch
        {
            Phase.Menu => StateMessage.Menu,
            Phase.Loading => StateMessage.Loading,
            Phase.HoleSummary => StateMessage.HoleComplete,
            Phase.Finished => StateMessage.Results,
            _ => HomeMenu.IsOpen ? StateMessage.Paused : StateMessage.Game,
        };

        SimConnection connection;
        RoundHud hud;
        Round round;
        GameView serverGame;
        Phase phase = Phase.Menu;
        int loadingHole = -1;
        Action pending;
        float pendingAt;
        string lastState;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance) return;
            var go = new GameObject("Golf Game");
            DontDestroyOnLoad(go);
            go.AddComponent<RoundDirector>();
        }

        // ---- menu API ----

        /// <summary>"Play a Round" on the main menu: resumes the server's game in progress, else a solo round of this many holes.</summary>
        public static void PlayFromMenu(int holes)
        {
            if (Instance) Instance.PlayRound(holes);
        }

        /// <summary>True when the server has a game in progress that "Play a Round" would resume.</summary>
        public static bool CanResume => Instance && Instance.serverGame != null && Instance.serverGame.IsInProgress;

        /// <summary>The round card's description: what pressing Play will do.</summary>
        public static string MenuDescription(GameMode mode)
        {
            if (!CanResume) return mode.description;
            var game = Instance.serverGame;
            var resume = Round.FromGame(game, Instance.course.turnOrder, Instance.course.maxOverPar).FirstUnfinishedHole();
            return $"Resume game {game.id}: {string.Join(", ", game.players.Select(p => p.name))}. " +
                   $"Hole {Mathf.Min(resume + 1, game.holesCount)} of {game.holesCount}.";
        }

        /// <summary>Resumes the server's game in progress, else starts a solo round (holes: 0 = CourseRound.holes).</summary>
        public void PlayRound(int holes = 0)
        {
            if (ScreenFade.Loading) return; // already leaving for a scene (a double Select on Play)
            if (CanResume) StartRound(Round.FromGame(serverGame, course.turnOrder, course.maxOverPar));
            else StartRound(new Round(0, new[] { course.soloPlayer }, holes > 0 ? holes : course.holes, course.turnOrder, course.maxOverPar));
        }

        // ---- lifecycle ----

        void Awake()
        {
            Instance = this;
            if (!course) course = CourseRound.Load();
            hud = CreateHud();
            TurnStarted += turn => hud?.Banner.AnnounceTurn(turn.player, Array.IndexOf(round.players, turn.player),
                                                             RoundHud.TurnInfo(turn.hole, round.Par, turn.strokes));
            connection = SimConnection.Create(ServerConfig.Load());
            connection.Connected += () => PublishState(force: true);
            connection.HelloReceived += hello => serverGame = hello.game;
            connection.GameStarted += OnGameStarted;
            connection.ScorecardReceived += game => serverGame = game;
            connection.GameFinished += OnGameFinished;
            connection.NavReceived += key => { if (NavInput.TryParse(key, out var nav)) NavInput.Push(nav); };
            connection.ShotReceived += OnRemoteShot;
            connection.ClubReceived += name => SetClub(name);
            connection.AimReceived += Aim;
            connection.AimResetReceived += () => Aim(-AimOffset);
            connection.MulliganReceived += Mulligan;
            connection.SkipReceived += PickUp;
            Shots.Gate = BlockedReason;
            Shots.Accepted += OnShotAccepted;
            HomeMenu.OpenChanged += OnPauseChanged;
            NavInput.Register(OnNav, NavInput.GamePriority);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void Start() => OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);

        void OnDestroy()
        {
            // Static hooks would otherwise outlive Play mode.
            if (Shots.Gate == BlockedReason) Shots.Gate = null;
            Shots.Accepted -= OnShotAccepted;
            HomeMenu.OpenChanged -= OnPauseChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            NavInput.Unregister(OnNav);
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            NavInput.Poll();
            if (pending != null && !HomeMenu.IsOpen && Time.realtimeSinceStartup >= pendingAt) RunPending();
        }

        /// <summary>Runs the scheduled step (next turn) now instead of after its delay; for tests and tools.</summary>
        public void RunPending()
        {
            var action = pending;
            pending = null;
            action?.Invoke();
        }

        void Later(float seconds, Action action)
        {
            pending = action;
            pendingAt = Time.realtimeSinceStartup + seconds;
        }

        RoundHud CreateHud()
        {
            if (!course.panelSettings || !course.hudLayout)
            {
                Debug.LogWarning("[RoundDirector] CourseRound has no panel settings or HUD layout; playing without the HUD.");
                return null;
            }
            var go = new GameObject("Round HUD");
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = course.panelSettings;
            doc.visualTreeAsset = course.hudLayout;
            doc.sortingOrder = 10; // over the scene's menus so the fade covers everything
            go.SetActive(true);
            return new RoundHud(doc);
        }

        // ---- rounds and scenes ----

        void OnGameStarted(GameView game)
        {
            serverGame = game;
            Debug.Log($"[RoundDirector] Game {game.id} started: {string.Join(", ", game.players.Select(p => p.name))}, {game.holesCount} holes.");
            StartRound(Round.FromGame(game, Instance.course.turnOrder, Instance.course.maxOverPar));
        }

        void OnGameFinished(GameView game)
        {
            serverGame = game;
            if (round == null || round.gameId != game.id || game.status != GameView.Abandoned) return;
            hud?.Toast("Game ended from the app");
            EndRound();
            LoadScene(course.menuScene, -1);
        }

        void StartRound(Round newRound)
        {
            round = newRound;
            FetchCourseHoles(() =>
            {
                int first = round.FirstUnfinishedHole();
                LoadHole(first < round.holeCount ? first : 0);
            });
        }

        void EndRound()
        {
            round = null;
            pending = null;
            hud?.HideScorecard();
            hud?.Banner.Clear();
        }

        void LoadHole(int index)
        {
            string scene = course.SceneFor(index);
            if (string.IsNullOrEmpty(scene))
            {
                Debug.LogError("[RoundDirector] CourseRound has no hole scenes.");
                return;
            }
            LoadScene(scene, index);
        }

        void LoadScene(string scene, int holeIndex)
        {
            loadingHole = holeIndex;
            pending = null;
            phase = Phase.Loading;
            PublishState();
            hud?.HideScorecard();
            hud?.Banner.Clear();
            if (hud != null) hud.Fade.LoadScene(scene);
            else SceneManager.LoadScene(scene);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Unbind();
            if (round != null) BuildCourseHole(loadingHole >= 0 ? loadingHole : Mathf.Max(0, round.HoleIndex));
            var holeInfo = FindAnyObjectByType<HoleInfo>();
            var golfBall = holeInfo ? FindAnyObjectByType<GolfBall>() : null;
            if (golfBall)
            {
                Bind(holeInfo, golfBall);
                // A hole we didn't load (HOME > Restart Hole) restarts the current hole.
                if (round != null) StartHole(loadingHole >= 0 ? loadingHole : Mathf.Max(0, round.HoleIndex));
                else StartPractice();
            }
            else
            {
                if (round != null) EndRound(); // HOME > Main Menu leaves the round (the server game can be resumed)
                phase = Phase.Menu;
            }
            loadingHole = -1;
            PublishState();
        }

        // ---- input ----

        bool OnNav(NavKey key)
        {
            if (HomeMenu.IsOpen) return false;
            if (phase is Phase.HoleSummary or Phase.Finished)
            {
                if (key == NavKey.Select) Continue();
                return key != NavKey.Back; // Back still opens the pause menu
            }
            if (!ball || phase is not (Phase.Playing or Phase.BetweenShots)) return false;
            switch (key)
            {
                case NavKey.Left: Aim(-AimStep); return true;
                case NavKey.Right: Aim(AimStep); return true;
                case NavKey.Up: SetClub(Clubs.Step(club, -1)); return true;
                case NavKey.Down: SetClub(Clubs.Step(club, 1)); return true;
                default: return false;
            }
        }

        void OnPauseChanged(bool paused) => PublishState();

        void OnShotAccepted(RemoteShotMessage shot)
        {
            if (Clubs.Normalize(shot.club) != null) SetClub(shot.club);
        }

        void OnRemoteShot(RemoteShotMessage shot)
        {
            var ack = Shots.Submit(shot, "WebSocket", out _);
            if (ack.status != "ok") hud?.Toast(ack.message);
        }

        /// <summary>Why a shot can't be hit now (the Shots gate), or null.</summary>
        string BlockedReason()
        {
            if (HomeMenu.IsOpen) return "The sim is paused";
            return phase switch
            {
                Phase.Menu => "Start a game on the sim first",
                Phase.Loading => "Loading the next hole",
                Phase.BetweenShots => "Wait for the next turn",
                Phase.HoleSummary or Phase.Finished => "Press Select on the remote to continue",
                _ => null,
            };
        }

        // ---- state for the phones ----

        /// <summary>What the sim shows now, as sent to the phones (and drawn by the HUD).</summary>
        public StateMessage BuildState()
        {
            var s = new StateMessage { screen = ScreenName, gameId = round?.gameId ?? 0, club = club, currentPlayer = "", lie = "" };
            if (ball && hole)
            {
                s.hole = round != null && round.HoleIndex >= 0 ? round.HoleNumber : 1;
                s.par = round != null && round.HoleIndex >= 0 ? round.Par : course.ParFor(hole.par);
                s.aim = (float)Math.Round(ball.aimOffset, 1);
                s.distanceToPin = Mathf.Round(YardsToPin);
                s.lie = round == null ? practiceLie : round.CurrentBall?.lie ?? "";
            }
            if (round?.CurrentBall is { } current)
            {
                s.currentPlayer = current.player;
                s.strokes = current.strokes;
            }
            return s;
        }

        /// <summary>Redraws the HUD and sends "state" when anything changed (always when forced, e.g. on reconnect).</summary>
        void PublishState(bool force = false)
        {
            var state = BuildState();
            hud?.Render(state, LieLabel(state.lie));
            string json = state.ToJson();
            if (!force && json == lastState) return;
            lastState = json;
            connection.Send(state);
        }
    }
}
