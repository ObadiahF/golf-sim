using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GolfSim.Net
{
    /// <summary>
    /// Self-update, like a game launcher: Check asks the game server for the platform's latest release
    /// (quietly: no server, no release or an older one leaves the game alone), then works out in the background what
    /// would change (hashing the installed files). Install downloads only the files whose sha256 differs into
    /// persistentDataPath/updates/blobs/ (resuming, verifying each), starts the updater script and returns; the caller
    /// then quits. Only a built player with a BuildInfo updates itself. The menu decides when (never during a round).
    /// See Game-server/docs/UPDATES.md.
    /// </summary>
    public static class GameUpdater
    {
        public enum Status
        {
            /// <summary>The Editor, or a build without BuildInfo: no updates.</summary>
            Off,
            Idle,
            Checking,
            /// <summary>The server has nothing newer (or couldn't be reached: see Error).</summary>
            UpToDate,
            /// <summary>Latest is newer; Plan is filled in once the installed files are checked.</summary>
            Available,
            Downloading,
            /// <summary>The updater is starting: quit now.</summary>
            Installing,
            Failed,
        }

        const float CheckInterval = 60f; // s: returning to the menu more often than this doesn't ask again

        public static Status State { get; private set; } = Status.Idle;
        public static Release Latest { get; private set; }
        public static UpdatePlan Plan { get; private set; }
        public static string Error { get; private set; }
        /// <summary>Bytes downloaded so far and in all (this install).</summary>
        public static long BytesDone { get; private set; }
        public static long BytesTotal { get; private set; }
        /// <summary>Anything above changed (state, plan, progress).</summary>
        public static event Action Changed;

        public static bool Busy => State is Status.Downloading or Status.Installing;

        /// <summary>Seconds since the server was last asked.</summary>
        public static float SinceLastCheck => Time.realtimeSinceStartup - lastCheck;

        static float lastCheck = float.NegativeInfinity;
        static bool cancel;
        /// <summary>The plan being made for planFor (on a worker thread), and how to stop it.</summary>
        static Task<UpdatePlan> planTask;
        static Release planFor;
        static CancellationTokenSource planning;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            State = Status.Idle;
            Latest = null;
            Plan = null;
            Error = null;
            Changed = null;
            lastCheck = float.NegativeInfinity;
            planning?.Cancel();
            planTask = null;
            planFor = null;
        }

        /// <summary>
        /// Asks the server for a newer release (at most once a minute unless forced); runs on host's coroutines.
        /// </summary>
        public static void Check(MonoBehaviour host, bool force = false)
        {
            var build = BuildInfo.Current;
            if (build == null || !InstallFolder.Supported) { Set(Status.Off); return; }
            if (Busy || State == Status.Checking || (!force && Time.realtimeSinceStartup - lastCheck < CheckInterval)) return;
            lastCheck = Time.realtimeSinceStartup;
            Set(Status.Checking);
            host.StartCoroutine(Release.Latest(build.platform, release =>
            {
                if (release == null || release.build <= build.build || release.files == null || release.files.Length == 0)
                {
                    Latest = null;
                    Plan = null;
                    Set(Status.UpToDate);
                    return;
                }
                Error = null;
                if (Latest == null || Latest.version != release.version || Plan == null)
                {
                    Latest = release;
                    Prepare(release);
                    host.StartCoroutine(AwaitPlan());
                }
                Set(Status.Available);
            }, error =>
            {
                Error = error;
                Debug.Log($"[Updates] Couldn't check for updates: {error}");
                Set(Latest != null ? Status.Available : Status.UpToDate);
            }));
        }

        /// <summary>Downloads and verifies the update, then starts the updater: quit when this ends in Installing.</summary>
        public static IEnumerator Install()
        {
            if (State is not (Status.Available or Status.Failed) || Latest == null) yield break;
            cancel = false;
            Error = null;
            BytesDone = 0;
            BytesTotal = 0;
            Set(Status.Downloading);
            yield return SafeCoroutine.Run(Download(), e => Error = e.Message);
            if (cancel) { Set(Status.Available); yield break; }
            if (Error != null)
            {
                Debug.LogWarning($"[Updates] Update to {Latest.version} failed: {Error}");
                Set(Status.Failed);
                yield break;
            }
            Set(Status.Installing);
        }

        /// <summary>Stops the download (the part on disk resumes next time).</summary>
        public static void Cancel() => cancel = true;

        /// <summary>The outcome of the last update (see UpdateInstaller.TakeResult); read once, at startup.</summary>
        public static (bool ok, string detail)? TakeLastResult() =>
            BuildInfo.Current != null && InstallFolder.Supported ? UpdateInstaller.TakeResult(InstallFolder.UpdatesFolder) : null;

        static IEnumerator Download()
        {
            string root = InstallFolder.Root, updates = InstallFolder.UpdatesFolder;
            if (Plan == null || Plan.release != Latest)
            {
                if (planTask == null || planFor != Latest || planTask.IsFaulted) Prepare(Latest); // the background check failed: again
                yield return AwaitPlan();
                if (cancel) yield break;
                if (Plan == null) { Error = Error ?? "Couldn't check the installed files"; yield break; }
            }
            var plan = Plan;
            if (plan.NothingToDo) { Error = "This version is already installed"; yield break; }
            plan.ForgetOtherBlobs(updates);
            plan.FindDownloads(updates);
            BytesTotal = plan.downloadBytes;
            long finished = 0;
            foreach (var blob in plan.downloads)
            {
                string failure = null;
                yield return BlobDownloader.Download(blob.Key, blob.Value, updates, () => cancel,
                    n => { BytesDone = finished + n; Changed?.Invoke(); }, e => failure = e);
                if (cancel) yield break;
                if (failure != null) { Error = failure; yield break; }
                finished += blob.Value;
            }
            BytesDone = BytesTotal;
            UpdateInstaller.Launch(plan, root, updates);
        }

        /// <summary>Starts checking the installed files against the release on a worker thread (AwaitPlan fills Plan).</summary>
        static void Prepare(Release release)
        {
            planning?.Cancel();
            planning = new CancellationTokenSource();
            var token = planning.Token;
            string root = InstallFolder.Root, updates = InstallFolder.UpdatesFolder;
            Plan = null;
            planFor = release;
            planTask = Task.Run(() => UpdatePlan.Make(release, root, updates, null, token), token);
        }

        /// <summary>Waits for the plan being made; sets Plan if it is still for the latest release.</summary>
        static IEnumerator AwaitPlan()
        {
            var task = planTask;
            var release = planFor;
            while (!task.IsCompleted) yield return null;
            if (task != planTask || release != Latest) yield break; // superseded by a newer check
            if (task.Status == TaskStatus.RanToCompletion)
            {
                Plan = task.Result;
                Debug.Log($"[Updates] {release.version} is available: {Plan.writes.Count} files to update " +
                          $"({Mb(Plan.downloadBytes)} to download), {Plan.deletes.Count} to delete.");
            }
            else if (task.IsFaulted)
            {
                Error = $"Couldn't check the installed files: {task.Exception.GetBaseException().Message}";
                Debug.LogWarning($"[Updates] {Error}");
            }
            Changed?.Invoke();
        }

        static void Set(Status state)
        {
            State = state;
            Changed?.Invoke();
        }

        /// <summary>"12.3 MB".</summary>
        public static string Mb(long bytes) => $"{bytes / 1048576.0:0.0} MB";
    }
}
