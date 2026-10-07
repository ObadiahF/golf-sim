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

        /// <summary>While this returns true the scheduled next step (next turn, scorecard) waits, e.g. for an instant replay.</summary>
        public static Func<bool> Hold;

        public CourseRound course;

        /// <summary>The round being played, or null (menu or practice).</summary>
        public Round Round => round;
        public SimConnection Connection => connection;
        /// <summary>The server's game in progress (from hello / gameStarted / scorecard), if any.</summary>
        public GameView ServerGame => serverGame;
        public string ScreenName => phase switch
        {
            Phase.Menu => GameUpdater.Busy ? StateMessage.Loading : AudioSettingsPanel.AnyOpen ? StateMessage.Settings : StateMessage.Menu,
            Phase.Loading => StateMessage.Loading,
            _ when HoleGoing => StateMessage.Loading,
            _ when replaying => StateMessage.Replay,
            Phase.HoleSummary => StateMessage.HoleComplete,
            Phase.Finished => StateMessage.Results,
            _ => !HomeMenu.IsOpen ? StateMessage.Game : AudioSettingsPanel.AnyOpen ? StateMessage.Settings : StateMessage.Paused,
        };

        /// <summary>
        /// The hole is on its way out under a round or practice (pause menu > Main Menu / Restart Hole): the scene is
        /// fading out, or its ball is already gone, before OnSceneLoaded moves the phase on. The pause menu closing
        /// meanwhile must not send a "game" state with canShoot (the phone would flip to its swing view).
        /// </summary>
        bool HoleGoing => phase is (Phase.Playing or Phase.BetweenShots) && (ScreenFade.Loading || !ball);

        SimConnection connection;
        RoundHud hud;
        ReplayDirector replay;
        Round round;
        GameView serverGame;
        Phase phase = Phase.Menu;
        int loadingHole = -1;
        Action pending, queuedLoad;
        float pendingAt;
        string lastState;
        float lastSentAt;
        /// <summary>
        /// Real seconds after which "state" is sent again unchanged: a phone that missed one (a dropped frame, a reconnect
        /// racing a change) catches up within this instead of showing the wrong screen until something else changes.
        /// </summary>
        const float StateRefresh = 3f;

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

        /// <summary>The round card's description: what pressing Play will do (a solo round of `holes`, or the resume).</summary>
        public static string MenuDescription(GameMode mode, int holes)
        {
            if (!CanResume) return mode.description.Replace("{holes}", holes.ToString());
            var game = Instance.serverGame;
            var resume = Round.FromGame(game, Instance.course.turnOrder, Instance.course.maxOverPar).FirstUnfinishedHole();
            return $"Resume game {game.id}: {string.Join(", ", game.players.Select(p => p.name))}. " +
                   $"Hole {Mathf.Min(resume + 1, game.holesCount)} of {game.holesCount}.";
        }

        /// <summary>Resumes the server's game in progress, else starts a solo round (holes: 0 = CourseRound.holes).</summary>
        public void PlayRound(int holes = 0)
        {
            if (ScreenFade.Loading || IsFetching) return; // already leaving for a scene or getting its holes (a double Select on Play)
            if (CanResume) StartRound(Round.FromGame(serverGame, course.turnOrder, course.maxOverPar));
            else StartRound(new Round(0, new[] { course.soloPlayer }, holes > 0 ? holes : course.holes, course.turnOrder, course.maxOverPar));
        }

        // ---- lifecycle ----

        void Awake()
        {
            Instance = this;
            if (!course) course = CourseRound.Load();
            hud = CreateHud();
            CreateMap();
            replay = gameObject.AddComponent<ReplayDirector>(); // presentation: sounds and replays follow the ball and round events
            replay.Playing += OnReplayPlaying;
            gameObject.AddComponent<GameAudio>();
            TurnStarted += turn => hud?.Banner.AnnounceTurn(turn.player, Array.IndexOf(round.players, turn.player),
                                                             RoundHud.TurnInfo(turn.hole, round.Par, turn.strokes));
            connection = SimConnection.Create(ServerConfig.Load());
            connection.Connected += () => PublishState(force: true);
            connection.HelloReceived += OnHello;
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
            connection.MapReceived += ShowMap;
            connection.Rejected += reason => hud?.Toast(reason, 6f); // another sim has this room: say so mid-round too
            Shots.Gate = BlockedReason;
            Shots.Accepted += OnShotAccepted;
            HomeMenu.OpenChanged += OnPauseChanged;
            AudioSettingsPanel.OpenChanged += OnPauseChanged;
            HomeMenu.CanRestart = CanRestartHole;
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
            AudioSettingsPanel.OpenChanged -= OnPauseChanged;
            if (HomeMenu.CanRestart == CanRestartHole) HomeMenu.CanRestart = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            NavInput.Unregister(OnNav);
            NavInput.Unregister(OnMapNav);
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            NavInput.Poll();
            PollMapKeys();
            TrackMiniMap();
            if (Time.realtimeSinceStartup - lastSentAt > StateRefresh) PublishState(force: true);
            if (pending != null && !HomeMenu.IsOpen && Hold?.Invoke() != true && Time.realtimeSinceStartup >= pendingAt) RunPending();
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

        /// <summary>The download overlay over everything (the menu's self-update uses it too): progress 0..1.</summary>
        internal void ShowProgress(string heading, string detail, float progress) => hud?.ShowLoading(heading, detail, progress);
        internal void HideProgress() => hud?.HideLoading();
        internal void Toast(string text, float seconds = 2.5f) => hud?.Toast(text, seconds);

        // ---- rounds and scenes ----

        /// <summary>Starts a round (game started, Play a Round, a resume), replacing any round in progress.</summary>
        void StartRound(Round newRound)
        {
            if (IsFetching) StopFetching();
            if (round != null) EndRound();
            round = newRound;
            SeedWind(newRound);
            starting = true;
            phase = Phase.Loading;
            FetchCourseHoles(newRound, () =>
            {
                if (round != newRound) return; // replaced or cancelled meanwhile
                int first = newRound.FirstUnfinishedHole();
                LoadHole(first < newRound.holeCount ? first : 0);
            });
        }

        void EndRound()
        {
            round = null;
            starting = false;
            pending = null;
            if (replay) replay.Forget();
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
            if (ScreenFade.Loading)
            {
                // Another scene is on its way (e.g. the menu after the last game was abandoned): load this one after it.
                queuedLoad = () => LoadScene(scene, holeIndex);
                return;
            }
            loadingHole = holeIndex;
            pending = null;
            if (replay) replay.Forget(); // the hole is over: no replay on the way out, no "Replay" hint on the next screen
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
            PrepareFacility(); // practice: the driving range or putting green picked on the menu (RoundDirector.Practice.cs)
            var holeInfo = FindAnyObjectByType<HoleInfo>();
            var golfBall = holeInfo ? FindAnyObjectByType<GolfBall>() : null;
            if (round != null && phase == Phase.Finished) EndRound(); // a finished round is never replayed (a reload is practice)
            if (golfBall)
            {
                starting = false;
                Bind(holeInfo, golfBall);
                int holeIndex = round != null ? (loadingHole >= 0 ? loadingHole : Mathf.Max(0, round.HoleIndex)) : 0;
                ApplyScenery(holeIndex); // its time of day (RoundDirector.Scenery.cs)
                // A hole we didn't load (HOME > Restart Hole) restarts the current hole.
                if (round != null) StartHole(holeIndex);
                else StartPractice();
            }
            else
            {
                // HOME > Main Menu leaves the round (the server game can be resumed), unless a new one is on its way.
                if (round != null && !starting) EndRound();
                phase = starting ? Phase.Loading : Phase.Menu;
            }
            loadingHole = -1;
            PublishState();
            if (queuedLoad == null) return;
            var load = queuedLoad;
            queuedLoad = null;
            load();
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
            if (FacilityNav(key)) return true;
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
            if (ack.status != "ok") RejectShot(shot, ack.message);
        }

        /// <summary>Why a shot can't be hit now (the Shots gate), or null.</summary>
        string BlockedReason()
        {
            if (HomeMenu.IsOpen) return "The sim is paused";
            if (replaying) return "Wait for the replay to finish";
            return phase switch
            {
                Phase.Menu => "Start a game on the sim first",
                Phase.Loading => "Loading the next hole",
                _ when HoleGoing => "Loading the next hole",
                Phase.BetweenShots => facility?.Waiting ?? "Wait for the next turn",
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
                FillWind(s);
                s.lie = round == null ? practiceLie : round.CurrentBall?.lie ?? "";
                // Putting only while a player is still on this hole (not with the ball in the cup, nor on the scorecard).
                if (phase is (Phase.Playing or Phase.BetweenShots) && round?.CurrentBall?.Done != true) FillPutting(s);
                else s.puttingAssist = PuttPreview.AssistName(PuttPreview.Assist);
            }
            FillCanShoot(s);
            facility?.Fill(s);
            FillMap(s);
            if (round?.CurrentBall is { } current)
            {
                s.currentPlayer = current.player;
                s.strokes = current.strokes;
            }
            return s;
        }

        /// <summary>Redraws the HUD and sends "state" when anything changed (always when forced: on reconnect, and every StateRefresh).</summary>
        internal void PublishState(bool force = false)
        {
            var state = BuildState();
            ShowPutting(state);
            ShowPractice(state);
            RenderMap(state);
            hud?.Render(state, LieLabel(state.lie), round?.CurrentBall);
            string json = state.ToJson();
            if (!force && json == lastState) return;
            lastState = json;
            lastSentAt = Time.realtimeSinceStartup;
            connection.Send(state);
        }
    }
}
