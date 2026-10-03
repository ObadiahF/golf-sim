using System;
using System.Collections;
using GolfSim.Net;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// The main menu's side of the self-update (GameUpdater): checks for a newer release when the menu opens (at
    /// startup and on every return from a round) and every few minutes while it stays open, offers it as an "Update" card when there is one, and on Play
    /// downloads it with a progress overlay (Back cancels; the download resumes next time), then quits so the updater
    /// can replace the files and start the new version. Only on the menu, so never during a round. Also shows how the
    /// last update went, once, after the restart.
    /// </summary>
    public class UpdateFlow
    {
        /// <summary>The menu card for an available update (made at runtime: it only exists while there is one).</summary>
        public static GameMode Mode
        {
            get
            {
                if (mode) return mode;
                mode = ScriptableObject.CreateInstance<GameMode>();
                mode.kind = GameMode.ModeKind.Update;
                mode.title = "Update available";
                mode.hideFlags = HideFlags.DontSave;
                return mode;
            }
        }

        const float IdleCheckInterval = 300f; // s: a menu left open asks again this often

        static GameMode mode;
        static bool resultShown;

        readonly MonoBehaviour host;
        readonly Action changed;
        bool running;
        GameUpdater.Status published;

        /// <summary>changed: the card or its text should be redrawn.</summary>
        public UpdateFlow(MonoBehaviour host, Action changed)
        {
            this.host = host;
            this.changed = changed;
            GameUpdater.Changed += OnChanged;
            GameUpdater.Check(host);
            host.StartCoroutine(ShowLastResult());
        }

        public void Dispose()
        {
            GameUpdater.Changed -= OnChanged;
            if (!running) return;
            // Leaving the menu (a game started from the phone) cancels the download; it resumes next time.
            GameUpdater.Cancel();
            Stop();
        }

        /// <summary>Called every frame by the menu: a menu left open on the TV still notices a new release.</summary>
        public void Tick()
        {
            if (!running && GameUpdater.SinceLastCheck > IdleCheckInterval) GameUpdater.Check(host);
        }

        /// <summary>Whether the menu shows the Update card.</summary>
        public static bool CardWanted => GameUpdater.Latest != null && GameUpdater.State is
            GameUpdater.Status.Available or GameUpdater.Status.Downloading or GameUpdater.Status.Failed or GameUpdater.Status.Installing;

        /// <summary>The menu's top bar: this build's version, and whether a newer one is waiting.</summary>
        public static string VersionLine => CardWanted ? $"{BuildInfo.Label}  ·  update available" : BuildInfo.Label;

        /// <summary>The hero text of the Update card: what Play will do.</summary>
        public static string Description
        {
            get
            {
                var latest = GameUpdater.Latest;
                if (latest == null) return "";
                string versions = $"Version {latest.version} is ready (this is {BuildInfo.Current?.version ?? "a development build"}).";
                string note = string.IsNullOrWhiteSpace(latest.note) ? "" : $" {latest.note.Trim()}";
                if (GameUpdater.State == GameUpdater.Status.Failed)
                    return $"{versions} The update didn't finish: {GameUpdater.Error}. Select Update to try again.{note}";
                var plan = GameUpdater.Plan;
                if (plan == null) return $"{versions} Checking your files…{note}";
                string size = plan.downloadBytes > 0 ? $"Downloads {GameUpdater.Mb(plan.downloadBytes)}" : "Everything is downloaded";
                return $"{versions} {size}, then the game restarts.{note}";
            }
        }

        /// <summary>Play on the Update card.</summary>
        public void Start()
        {
            if (running || !CardWanted || RoundDirector.Instance?.Round != null) return;
            running = true;
            NavInput.Register(OnNav, NavInput.ModalPriority);
            Progress("Getting ready…", 0f);
            host.StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            yield return GameUpdater.Install();
            if (!running) yield break; // the menu went away meanwhile
            if (GameUpdater.State == GameUpdater.Status.Installing)
            {
                Progress("Restarting with the new version…", 1f);
                yield return new WaitForSecondsRealtime(0.6f);
                Application.Quit();
                yield break;
            }
            Stop();
            if (GameUpdater.State == GameUpdater.Status.Failed) RoundDirector.Instance?.Toast($"Update failed: {GameUpdater.Error}", 5f);
        }

        /// <summary>The download is modal: Back cancels it, every other key is swallowed.</summary>
        bool OnNav(NavKey key)
        {
            if (key == NavKey.Back && GameUpdater.State == GameUpdater.Status.Downloading) GameUpdater.Cancel();
            return true;
        }

        void Stop()
        {
            running = false;
            NavInput.Unregister(OnNav);
            RoundDirector.Instance?.HideProgress();
        }

        void OnChanged()
        {
            if (running && GameUpdater.State == GameUpdater.Status.Downloading && GameUpdater.BytesTotal > 0)
            {
                long done = GameUpdater.BytesDone, total = GameUpdater.BytesTotal;
                Progress($"Downloading {GameUpdater.Mb(done)} of {GameUpdater.Mb(total)} ({100.0 * done / total:0} %)", (float)done / total);
            }
            if (GameUpdater.State != published)
            {
                published = GameUpdater.State;
                RoundDirector.Instance?.PublishState(); // the phones show "loading" while the sim updates
            }
            changed();
        }

        void Progress(string detail, float progress) =>
            RoundDirector.Instance?.ShowProgress($"Updating to {GameUpdater.Latest?.version}", detail, progress);

        /// <summary>After an update's restart: "Updated to …" or why it failed (once per run; the session creates the HUD after the menu).</summary>
        static IEnumerator ShowLastResult()
        {
            if (resultShown) yield break;
            resultShown = true;
            var result = GameUpdater.TakeLastResult();
            if (result == null) yield break;
            while (!RoundDirector.Instance) yield return null;
            yield return new WaitForSecondsRealtime(1f);
            var (ok, detail) = result.Value;
            RoundDirector.Instance.Toast(ok ? $"Updated to {detail}" : $"The update failed ({detail}); still on {BuildInfo.Label}", 6f);
            Debug.Log(ok ? $"[Updates] Updated to {detail}." : $"[Updates] The last update failed: {detail}");
        }
    }
}
