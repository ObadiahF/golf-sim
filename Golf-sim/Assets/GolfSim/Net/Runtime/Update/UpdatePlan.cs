using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace GolfSim.Net
{
    /// <summary>
    /// What updating to a release takes: the files whose installed copy differs (by sha256), the files to delete, and
    /// the blobs still to download into the staging folder (updates/blobs/). Made on a worker thread (Make hashes the
    /// installed files).
    /// </summary>
    public class UpdatePlan
    {
        public Release release;
        /// <summary>Files to (over)write from the staged blobs.</summary>
        public readonly List<ReleaseFile> writes = new List<ReleaseFile>();
        /// <summary>Installed files the release no longer has (see LocalFiles.Extra).</summary>
        public readonly List<string> deletes = new List<string>();
        /// <summary>Blobs to download: sha256 and size (each once, however many files share it).</summary>
        public readonly Dictionary<string, long> downloads = new Dictionary<string, long>();
        /// <summary>Bytes still to download, and bytes of the changed files already staged by an earlier attempt.</summary>
        public long downloadBytes, stagedBytes;

        public bool NothingToDo => writes.Count == 0 && deletes.Count == 0;

        public static string BlobsFolder(string updatesFolder) => Path.Combine(updatesFolder, "blobs");

        /// <summary>A downloaded and verified blob (an unfinished one is "&lt;sha256&gt;.part").</summary>
        public static string StagedBlob(string updatesFolder, string sha256) => Path.Combine(BlobsFolder(updatesFolder), sha256);

        /// <summary>progress(bytes hashed, bytes to hash) while the installed files are checked.</summary>
        public static UpdatePlan Make(Release release, string root, string updatesFolder, Action<long, long> progress, CancellationToken cancel)
        {
            var plan = new UpdatePlan { release = release };
            var installed = LocalFiles.Hashes(root, release.files, Path.Combine(updatesFolder, "hashes.json"), progress, cancel);
            foreach (var file in release.files)
                if (!installed.TryGetValue(file.path, out var sha) || sha != file.sha256) plan.writes.Add(file);
            plan.deletes.AddRange(LocalFiles.Extra(root, release.files));
            plan.FindDownloads(updatesFolder);
            return plan;
        }

        /// <summary>Which blobs of the files to write aren't staged yet (again before downloading: staging may have changed).</summary>
        public void FindDownloads(string updatesFolder)
        {
            downloads.Clear();
            downloadBytes = stagedBytes = 0;
            var seen = new HashSet<string>();
            foreach (var file in writes)
            {
                if (!seen.Add(file.sha256)) continue;
                var staged = new FileInfo(StagedBlob(updatesFolder, file.sha256));
                if (staged.Exists && staged.Length == file.size) stagedBytes += file.size;
                else
                {
                    downloads[file.sha256] = file.size;
                    downloadBytes += file.size;
                }
            }
        }

        /// <summary>Deletes staged blobs (and partial downloads) this plan doesn't need, e.g. from an older release.</summary>
        public void ForgetOtherBlobs(string updatesFolder)
        {
            string folder = BlobsFolder(updatesFolder);
            if (!Directory.Exists(folder)) return;
            var needed = new HashSet<string>();
            foreach (var file in writes) needed.Add(file.sha256);
            foreach (var path in Directory.GetFiles(folder))
            {
                string name = Path.GetFileName(path);
                string sha = name.EndsWith(".part") ? name.Substring(0, name.Length - 5) : name;
                if (!needed.Contains(sha)) File.Delete(path);
            }
        }
    }
}
