using GolfSim.Course;
using System;
using GolfSim.Course;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GolfSim.CourseEditor
{
    /// <summary>Runs Tools/course_gen/gen_hole.py with the project's Python and returns its output.</summary>
    public static class PythonRunner
    {
        const string PythonPrefKey = "GolfSim.PythonPath";
        const int TimeoutMs = 180_000;

        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        public static string Script => Path.Combine(ProjectRoot, "Tools", "course_gen", "gen_hole.py");
        static string DefaultPython => Path.Combine(ProjectRoot, "Tools", "course_prep", ".venv", "bin", "python");

        public static string PythonPath
        {
            get => EditorPrefs.GetString(PythonPrefKey, DefaultPython);
            set => EditorPrefs.SetString(PythonPrefKey, value);
        }

        public static bool IsConfigured => File.Exists(PythonPath) && File.Exists(Script);

        /// <summary>Runs gen_hole.py with the arguments; throws with stderr on failure. Shows a progress bar.</summary>
        public static string Run(string title, params string[] args)
        {
            if (!IsConfigured)
                throw new FileNotFoundException($"Python or generator script not found.\nPython: {PythonPath}\nScript: {Script}\n" +
                                                "Set up the venv (see Tools/course_prep/README.md) or set the Python path in the generator window.");

            var info = new ProcessStartInfo(PythonPath, string.Join(" ", new[] { Script }.Concat(args).Select(Quote)))
            {
                WorkingDirectory = Path.GetDirectoryName(Script),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            using var process = new Process { StartInfo = info };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (stdout) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data); };
            try
            {
                EditorUtility.DisplayProgressBar("Course Generator", title, 0.3f);
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(TimeoutMs))
                {
                    process.Kill();
                    throw new TimeoutException($"{title} took longer than {TimeoutMs / 1000}s");
                }
                process.WaitForExit(); // flush async output
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            if (process.ExitCode != 0)
                throw new Exception($"{title} failed:\n{stderr}{stdout}");
            return stdout.ToString();
        }

        /// <summary>The last line of output that parses as a JSON object (gen_hole.py prints results last).</summary>
        public static T LastJson<T>(string output)
        {
            string line = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("{"));
            if (line == null) throw new FormatException("Generator printed no result:\n" + output);
            return JsonUtility.FromJson<T>(line);
        }

        static string Quote(string arg) => arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '"', '\'' }) < 0
            ? arg : "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        /// <summary>Absolute path inside the project -> "Assets/..." asset path.</summary>
        public static string ToAssetPath(string absolute) =>
            Path.GetRelativePath(ProjectRoot, absolute).Replace('\\', '/');

        public static void Log(string output)
        {
            var lines = output.Split('\n').Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("{")).ToList();
            if (lines.Count > 0) Debug.Log("[CourseGenerator] " + string.Join("\n", lines));
        }
    }

    [Serializable]
    public class GeneratedHoleResult
    {
        public string id;
        public string package;
        public int par;
        public string preset;
        public string theme;
        public int seed;
    }

    [Serializable]
    public class GeneratorStatus
    {
        public int ratings;
        public int up;
        public int trainedOn;
        public List<string> likes = new List<string>();
        public List<string> dislikes = new List<string>();
    }

    [Serializable]
    public class PresetList
    {
        [Serializable]
        public class Preset
        {
            public string name;
            public string label;
            public string theme;
        }

        public List<Preset> presets = new List<Preset>();
    }
}
