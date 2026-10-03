using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GolfSim.Net
{
    /// <summary>
    /// Applies a downloaded update. A running game can't overwrite its own exe and dlls, so this writes the plan and the
    /// updater script (Resources/Updater/ApplyWindows.txt as PowerShell, ApplyMac.txt as bash) to updates/apply/,
    /// starts it, and the game then quits: the script waits for the game to exit, backs up and replaces the changed
    /// files, deletes the old ones, and starts the game again (restoring the backup if anything fails). Its outcome is
    /// in updates/result.txt, which the next start reads (TakeResult) and its log in updates/update.log.
    /// </summary>
    public static class UpdateInstaller
    {
        /// <summary>Writes the plan and script and starts the updater; the caller must quit right after.</summary>
        public static void Launch(UpdatePlan plan, string root, string updatesFolder)
        {
            string folder = Path.Combine(updatesFolder, "apply");
            Directory.CreateDirectory(folder);
            File.Delete(ResultPath(updatesFolder));
            File.WriteAllText(Path.Combine(folder, "plan.txt"), PlanText(plan, root, updatesFolder), new UTF8Encoding(false));

            bool windows = InstallFolder.IsWindows;
            string script = Path.Combine(folder, windows ? "apply.ps1" : "apply.sh");
            var source = Resources.Load<TextAsset>(windows ? "Updater/ApplyWindows" : "Updater/ApplyMac");
            if (!source) throw new InvalidOperationException("The updater script is missing from Resources/Updater");
            // PowerShell reads a BOM-less script as the ANSI code page; the BOM makes it UTF-8. bash wants LF line ends.
            string text = windows ? source.text.Replace("\r\n", "\n").Replace("\n", "\r\n") : source.text.Replace("\r\n", "\n");
            File.WriteAllText(script, text, new UTF8Encoding(windows));

            var start = windows
                ? new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                                       $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"")
                : new ProcessStartInfo("/bin/bash", $"\"{script}\"");
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WorkingDirectory = folder;
            Process.Start(start);
            Debug.Log($"[Updates] Started the updater ({script}) for {plan.release.version}: {plan.writes.Count} files to write, {plan.deletes.Count} to delete.");
        }

        /// <summary>
        /// The last update's outcome, once: (true, version) after an update, (false, reason) after a failed one (the
        /// old version was restored), null when there was none.
        /// </summary>
        public static (bool ok, string detail)? TakeResult(string updatesFolder)
        {
            string path = ResultPath(updatesFolder);
            try
            {
                if (!File.Exists(path)) return null;
                string[] fields = File.ReadAllText(path).Trim().Split(new[] { '\t' }, 2);
                File.Delete(path);
                return (fields[0] == "ok", fields.Length > 1 ? fields[1] : "");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Updates] Couldn't read {path}: {e.Message}");
                return null;
            }
        }

        static string ResultPath(string updatesFolder) => Path.Combine(updatesFolder, "result.txt");

        /// <summary>One tab-separated record per line: where things are, then the files to write and delete.</summary>
        static string PlanText(UpdatePlan plan, string root, string updatesFolder)
        {
            var sb = new StringBuilder();
            void Line(params string[] fields)
            {
                foreach (var f in fields)
                    if (f.IndexOfAny(new[] { '\t', '\n', '\r' }) >= 0) throw new ArgumentException($"Can't put {f} in the update plan");
                sb.Append(string.Join("\t", fields)).Append('\n');
            }
            Line("root", root);
            Line("launch", InstallFolder.Launcher);
            Line("pid", Process.GetCurrentProcess().Id.ToString());
            Line("version", plan.release.version);
            Line("blobs", UpdatePlan.BlobsFolder(updatesFolder));
            foreach (var arg in RelaunchArguments()) Line("arg", arg);
            foreach (var file in plan.writes)
            {
                InstallFolder.FullPath(root, file.path); // throws for an unsafe path before anything is written
                Line("write", file.sha256, file.executable ? "1" : "0", file.path);
            }
            foreach (var path in plan.deletes)
            {
                InstallFolder.FullPath(root, path);
                Line("delete", path);
            }
            return sb.ToString();
        }

        /// <summary>This run's command line minus the program (e.g. -server-url), so the new version starts the same way.</summary>
        static IEnumerable<string> RelaunchArguments()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++)
            {
                // macOS adds -psn_... when started from the Finder; open adds its own.
                if (args[i].StartsWith("-psn_") || args[i].IndexOfAny(new[] { '\t', '\n', '\r' }) >= 0) continue;
                yield return args[i];
            }
        }
    }
}
