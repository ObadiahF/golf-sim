using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace GolfSim.Net
{
    /// <summary>
    /// Downloads one blob of a release into the staging folder: into "&lt;sha256&gt;.part", resuming an interrupted
    /// download with a Range request, then checks the sha256 and only then renames it to "&lt;sha256&gt;". A download
    /// that stalls is retried from where it stopped; a cancelled one keeps its part for next time.
    /// </summary>
    public static class BlobDownloader
    {
        const int Attempts = 5;
        const float StallSeconds = 30f, RetryDelay = 2f;

        /// <summary>progress(bytes of this blob on disk); failed(error) when every attempt failed.</summary>
        public static IEnumerator Download(string sha256, long size, string updatesFolder, Func<bool> cancelled,
                                           Action<long> progress, Action<string> failed)
        {
            string final = UpdatePlan.StagedBlob(updatesFolder, sha256), part = final + ".part";
            Directory.CreateDirectory(Path.GetDirectoryName(final));
            string error = null;
            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                if (attempt > 0) yield return new WaitForSecondsRealtime(RetryDelay);
                long have = File.Exists(part) ? new FileInfo(part).Length : 0;
                if (have > size) { File.Delete(part); have = 0; }
                if (size == 0) File.WriteAllBytes(part, new byte[0]); // nothing to fetch
                if (have < size)
                {
                    long code = 0;
                    string fault = null;
                    yield return Fetch(sha256, part, have, cancelled, n => progress(have + n), (c, f) => { code = c; fault = f; });
                    if (cancelled()) yield break;
                    if (code == 416 || (have > 0 && code == 200))
                    {
                        // The part is unusable (longer than the file, or the server sent the whole file after it).
                        File.Delete(part);
                        error = $"HTTP {code} resuming {sha256}";
                        continue;
                    }
                    if (fault != null)
                    {
                        error = fault;
                        Debug.LogWarning($"[Updates] {sha256}: {error} (attempt {attempt + 1} of {Attempts})");
                        continue;
                    }
                }
                if (cancelled()) yield break;
                var check = Task.Run(() => LocalFiles.Sha256(part, CancellationToken.None));
                while (!check.IsCompleted) yield return null;
                if (check.Status == TaskStatus.RanToCompletion && check.Result == sha256 && new FileInfo(part).Length == size)
                {
                    if (File.Exists(final)) File.Delete(final);
                    File.Move(part, final);
                    progress(size);
                    yield break;
                }
                error = check.Exception != null ? check.Exception.GetBaseException().Message : $"{sha256} downloaded with the wrong contents";
                File.Delete(part);
            }
            failed(error);
        }

        /// <summary>One GET appending to the part from byte `from`; done(status code, error or null). Aborts when cancelled or stalled.</summary>
        static IEnumerator Fetch(string sha256, string part, long from, Func<bool> cancelled, Action<long> progress, Action<long, string> done)
        {
            using var request = GameApi.Authorized(new UnityWebRequest(Release.BlobUrl(sha256), UnityWebRequest.kHttpVerbGET));
            request.downloadHandler = new DownloadHandlerFile(part, from > 0) { removeFileOnAbort = false };
            if (from > 0) request.SetRequestHeader("Range", $"bytes={from}-");
            var send = request.SendWebRequest();
            ulong seen = 0;
            float lastProgress = Time.realtimeSinceStartup;
            bool stalled = false;
            while (!send.isDone)
            {
                if (cancelled()) { request.Abort(); yield break; }
                if (request.downloadedBytes != seen) { seen = request.downloadedBytes; lastProgress = Time.realtimeSinceStartup; }
                else if (Time.realtimeSinceStartup - lastProgress > StallSeconds) { stalled = true; request.Abort(); } // the next attempt resumes
                progress((long)seen);
                yield return null;
            }
            string fault = request.result == UnityWebRequest.Result.Success ? null
                : stalled ? "the download stalled" : request.responseCode > 0 ? $"HTTP {request.responseCode}" : request.error;
            done(request.responseCode, fault);
        }
    }
}
