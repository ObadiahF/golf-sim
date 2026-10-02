using System;
using GolfSim.Ball;
using GolfSim.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// The instant replay. In a round, after an interesting shot (ReplaySettings.Reason) or when a player presses Up
    /// between turns, it holds the next turn (RoundDirector.Hold), dips to black, and replays the recorded shot
    /// (ShotRecorder) with TV cameras (ReplayCameraman), slow motion, a ghost ball and tracer (ReplayGhost) and
    /// letterboxed graphics (ReplayOverlay), then hands the camera back exactly as it was. Select or Back skips it,
    /// its lead-in included. The real ball, its tracer and aim line and the shot panel are hidden while it plays (the
    /// HUD hides itself on the "replay" screen). Leaving the hole (a scene load) drops the replay and the last shot.
    /// </summary>
    [RequireComponent(typeof(ShotRecorder))]
    public class ReplayDirector : MonoBehaviour
    {
        enum Stage { Idle, Waiting, DipIn, Playing, DipOut, Reveal }

        const float DipTime = 0.2f, RevealTime = 0.35f, FadeFromBlack = 0.3f;

        public static ReplayDirector Instance { get; private set; }

        public ReplaySettings settings = new ReplaySettings();

        /// <summary>A replay is starting (true) or has ended (false).</summary>
        public event Action<bool> Playing;
        /// <summary>The replay passed one of the recording's events (for its sounds).</summary>
        public event Action<ShotEvent, ShotRecording> EventPlayed;

        /// <summary>True from deciding to replay until the camera is handed back (the round waits meanwhile).</summary>
        public bool Busy => stage is not Stage.Idle and not Stage.Reveal;
        public bool IsPlaying => stage == Stage.Playing;
        public ReplayPlan Plan => plan;
        public ShotRecording Recording => rec;
        /// <summary>Recording time being shown.</summary>
        public float Time => time;
        public string CurrentShot => plan?.ShotAt(time)?.name;

        ShotRecorder recorder;
        ReplayOverlay overlay;
        ReplayGhost ghost;
        ShotRecording rec;
        ReplayPlan plan;
        Stage stage;
        float stageTime, time;
        int nextEvent;
        ScreenState saved;
        Func<bool> hold;

        void Awake()
        {
            Instance = this;
            recorder = GetComponent<ShotRecorder>();
            recorder.Recorded += OnRecorded;
            hold = () => Busy;
            RoundDirector.Hold = hold;
            NavInput.Register(OnNav, NavInput.OverlayPriority + 10); // over the pause menu: Back skips a replay
            SceneManager.sceneLoaded += OnSceneLoaded;
            CreateOverlay();
        }

        void OnDestroy()
        {
            recorder.Recorded -= OnRecorded;
            NavInput.Unregister(OnNav);
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (RoundDirector.Hold == hold) RoundDirector.Hold = null;
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            Tick(UnityEngine.Time.unscaledDeltaTime);
            bool offered = Offered;
            overlay?.ShowPrompt(offered);
            if (offered != lastOffered && RoundDirector.Instance) RoundDirector.Instance.PublishState(); // state.canReplay
            lastOffered = offered;
        }

        /// <summary>The "▲ Replay" offer is showing: the phones get it as state.canReplay.</summary>
        public bool Offered => stage == Stage.Idle && CanReplay;
        bool lastOffered;

        /// <summary>A replay of the last shot is possible: in a round, between turns or on its scorecard, the ball still where it finished.</summary>
        public bool CanReplay =>
            recorder.Last != null && recorder.LastIsCurrent && RoundDirector.Instance && RoundDirector.Instance.Round != null &&
            Shots.Blocked != null && !HomeMenu.IsOpen && !ScreenFade.Loading &&
            RoundDirector.Instance.ScreenName is StateMessage.Game or StateMessage.HoleComplete or StateMessage.Results;

        // ---- starting and stopping ----

        void OnRecorded(ShotRecording recording)
        {
            if (!RoundDirector.Instance || RoundDirector.Instance.Round == null) return;
            string reason = settings.Reason(recording);
            if (reason == null) return;
            Debug.Log($"[Replay] {recording.setup.player} {recording.setup.club}: {reason}");
            Queue(recording, settings.leadIn);
        }

        /// <summary>The queued shot is still the last one and its ball still lies where it finished (no mulligan, no restart).</summary>
        bool Current => rec != null && rec == recorder.Last && recorder.LastIsCurrent;

        /// <summary>Replays this shot after `delay` real seconds (0: now).</summary>
        public void Queue(ShotRecording recording, float delay = 0f)
        {
            if (stage != Stage.Idle && stage != Stage.Reveal) return;
            rec = recording;
            Enter(Stage.Waiting);
            stageTime = -delay;
        }

        /// <summary>Ends the replay now (Select / Back): dips to black and hands the camera back.</summary>
        public void Skip()
        {
            switch (stage)
            {
                case Stage.Waiting: Enter(Stage.Idle); break;
                case Stage.DipIn or Stage.Playing: Enter(Stage.DipOut); break;
            }
        }

        /// <summary>Leaving the hole (Select on the scorecard, the menu, a new round): drops any replay and the last shot.</summary>
        public void Forget()
        {
            Stop();
            recorder.Forget();
            overlay?.ShowPrompt(false);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Forget(); // the camera and ball it would use are gone

        bool OnNav(NavKey key)
        {
            if (HomeMenu.IsOpen) return false;
            if (stage == Stage.Waiting)
            {
                // The lead-in (or an Up just pressed): Select or Back skips it, Up starts it now. The key never falls
                // through to the scorecard, which would load the next hole under a replay about to start.
                if (key is NavKey.Select or NavKey.Back) Skip();
                else if (key == NavKey.Up) stageTime = Mathf.Max(stageTime, 0f);
                return true;
            }
            if (Busy)
            {
                if (key is NavKey.Select or NavKey.Back) Skip();
                return true; // nothing else reacts while the replay is on screen
            }
            if (key == NavKey.Up && stage == Stage.Idle && CanReplay)
            {
                Queue(recorder.Last);
                return true;
            }
            return false;
        }

        // ---- playback ----

        /// <summary>Runs the replay forward by this much real time (the Editor's frames, or a test's steps).</summary>
        public void Tick(float realDelta)
        {
            if (stage == Stage.Idle || HomeMenu.IsOpen) return; // the pause menu freezes it
            stageTime += realDelta;
            switch (stage)
            {
                case Stage.Waiting:
                    if (!Current) Enter(Stage.Idle); // the shot was taken back or the hole restarted during the lead-in
                    else if (stageTime >= 0f) Enter(Stage.DipIn);
                    break;
                case Stage.DipIn:
                    overlay?.Show(0f, 0f, Mathf.Clamp01(stageTime / DipTime));
                    if (stageTime >= DipTime) Begin();
                    break;
                case Stage.Playing:
                    Advance(realDelta);
                    break;
                case Stage.DipOut:
                    overlay?.Show(1f, 1f - stageTime / DipTime, Mathf.Clamp01(stageTime / DipTime));
                    if (stageTime >= DipTime) End(restore: true);
                    break;
                case Stage.Reveal:
                    float u = Mathf.Clamp01(stageTime / RevealTime);
                    overlay?.Show(1f - u, 0f, 1f - u);
                    if (u >= 1f) Enter(Stage.Idle);
                    break;
            }
        }

        void Enter(Stage next)
        {
            stage = next;
            stageTime = 0f;
            if (next == Stage.Idle) overlay?.Show(0f, 0f, 0f);
        }

        void Begin()
        {
            var director = RoundDirector.Instance;
            var ball = director ? director.Ball : null;
            var hole = FindAnyObjectByType<GolfSim.Course.HoleInfo>();
            var cam = Camera.main;
            if (rec == null || !ball || !hole || !cam)
            {
                Enter(Stage.Idle);
                return;
            }
            plan = new ReplayCameraman(settings, CameraSpots.For(hole, ball)).Plan(rec);
            saved = ScreenState.Hide(cam, ball);
            ghost ??= new ReplayGhost(ball, transform);
            ghost.Begin(rec);
            ghost.Show(true);
            time = plan.start;
            nextEvent = 0;
            overlay?.SetShot(rec.setup.player, Describe(rec));
            ball.Placed += OnBallPlaced;
            Enter(Stage.Playing);
            Playing?.Invoke(true);
            Debug.Log($"[Replay] {plan.shots.Count} cameras ({string.Join(", ", plan.shots.ConvertAll(s => s.name))}), " +
                      $"{plan.RealLength():0.0} s");
            Advance(0f);
        }

        void Advance(float realDelta)
        {
            time = Mathf.Min(time + realDelta * plan.SpeedAt(time), plan.end);
            var cam = saved.camera;
            if (!cam)
            {
                End(restore: false);
                return;
            }
            var shot = plan.ShotAt(time);
            shot.Apply(cam, time, rec.PositionAt(time), rec.VelocityAt(time), realDelta);
            ghost.Draw(time, cam, realDelta);
            for (; nextEvent < rec.events.Count && rec.events[nextEvent].time <= time; nextEvent++)
                EventPlayed?.Invoke(rec.events[nextEvent], rec);

            float shown = Mathf.Clamp01(stageTime / FadeFromBlack);
            overlay?.Show(Mathf.Clamp01(stageTime / 0.45f), Mathf.Clamp01((stageTime - 0.35f) / 0.4f), 1f - shown);
            if (time >= plan.end) Enter(Stage.DipOut);
        }

        void OnBallPlaced(GolfBall ball) => End(restore: true, reveal: false); // the next turn started (e.g. a mulligan)

        void End(bool restore, bool reveal = true)
        {
            if (saved.ball) saved.ball.Placed -= OnBallPlaced;
            ghost?.Show(false);
            if (restore) saved.Restore();
            saved = default;
            Enter(reveal ? Stage.Reveal : Stage.Idle);
            if (reveal) overlay?.Show(1f, 0f, 1f);
            Playing?.Invoke(false);
        }

        void OnDisable() => Stop();

        /// <summary>Ends any replay at once, with no reveal (the camera and screen are handed back if it had begun).</summary>
        void Stop()
        {
            if (stage is Stage.Playing or Stage.DipOut) End(restore: true, reveal: false);
            else Enter(Stage.Idle);
        }

        // ---- graphics ----

        void CreateOverlay()
        {
            var course = CourseRound.Load();
            if (!course.panelSettings) return;
            var go = new GameObject("Replay Overlay");
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = course.panelSettings;
            doc.sortingOrder = 20; // over the HUD
            go.SetActive(true);
            overlay = new ReplayOverlay(doc);
        }

        /// <summary>"Driver  ·  268 yd carry  ·  291 yd", "Putter  ·  7.4 m  ·  Holed".</summary>
        static string Describe(ShotRecording r)
        {
            string club = string.IsNullOrEmpty(r.setup.club) ? "Shot" : r.setup.club;
            string end = r.Holed ? "Holed" : r.Water ? "Water" : r.end == BallStatus.OutOfBounds ? "Out of bounds" : r.ObstacleTime >= 0f ? "Hit a tree" : null;
            string distance = r.Rolled
                ? $"{r.StartToPin:0.0} m"
                : $"{r.Carry * ShotData.YardsPerMeter:0} yd carry  ·  {r.Total * ShotData.YardsPerMeter:0} yd";
            return end == null ? $"{club}  ·  {distance}" : $"{club}  ·  {distance}  ·  {end}";
        }
    }
}
