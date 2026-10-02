using GolfSim.Course;
using System;
using GolfSim.Course;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>
    /// Golf > Course Generator: generate random holes in a style, build them into the scene, and
    /// rate them 👍 / 👎 so the generator learns what you like (Tools/course_gen).
    /// </summary>
    public class CourseGeneratorWindow : EditorWindow
    {
        const string OutputFolder = "Assets/CourseData/generated";

        // Style parameters exposed as optional slider overrides (names match Tools/course_gen/style.py).
        static readonly (string param, string label)[] Knobs =
        {
            ("tree_density", "Trees"), ("water", "Water"), ("bunkers", "Bunkers"), ("relief", "Relief"),
            ("dogleg", "Dogleg"), ("fairway_width", "Fairway width"), ("scrub", "Scrub / waste"),
        };
        static readonly string[] ParOptions = { "Any", "3", "4", "5" };
        static readonly string[] FallbackPresets = { "parkland", "forest", "lakes", "links", "desert", "mountain" };

        string[] presetNames = FallbackPresets;
        string[] presetLabels = FallbackPresets;
        int presetIndex;
        int parIndex;
        bool randomSeed = true;
        int seed = 1;
        bool customize;
        bool[] knobOn = new bool[Knobs.Length];
        float[] knobValue = Enumerable.Repeat(0.5f, Knobs.Length).ToArray();
        bool usePreferences = true;
        bool scatterEnabled = true;

        GeneratedHoleResult last;
        bool lastRated;
        Texture2D lastPreview;
        GeneratorStatus status;
        Vector2 scroll;

        [MenuItem("Golf/Course Generator")]
        public static void Open() => GetWindow<CourseGeneratorWindow>("Course Generator");

        void OnEnable() => RefreshFromPython();

        void RefreshFromPython()
        {
            if (!PythonRunner.IsConfigured) return;
            try
            {
                var list = PythonRunner.LastJson<PresetList>(PythonRunner.Run("Reading presets", "presets", "--json"));
                if (list.presets.Count > 0)
                {
                    presetNames = list.presets.Select(p => p.name).ToArray();
                    presetLabels = list.presets.Select(p => p.label).ToArray();
                }
                status = PythonRunner.LastJson<GeneratorStatus>(PythonRunner.Run("Reading ratings", "status", "--json"));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CourseGenerator] {e.Message}");
            }
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawSetup();
            EditorGUILayout.Space();
            DrawStyle();
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || !PythonRunner.IsConfigured))
                if (GUILayout.Button("Generate Random Hole", GUILayout.Height(36)))
                    Guard(Generate);
            DrawLastHole();
            EditorGUILayout.Space();
            DrawLearning();
            EditorGUILayout.EndScrollView();
        }

        void DrawSetup()
        {
            if (PythonRunner.IsConfigured) return;
            EditorGUILayout.HelpBox("Python environment not found. Create it (Tools/course_prep/README.md) or point to your Python below.", MessageType.Warning);
            EditorGUI.BeginChangeCheck();
            PythonRunner.PythonPath = EditorGUILayout.TextField("Python", PythonRunner.PythonPath);
            if (EditorGUI.EndChangeCheck()) RefreshFromPython();
        }

        void DrawStyle()
        {
            EditorGUILayout.LabelField("Style", EditorStyles.boldLabel);
            presetIndex = EditorGUILayout.Popup("Preset", Mathf.Min(presetIndex, presetNames.Length - 1), presetLabels);
            parIndex = EditorGUILayout.Popup("Par", parIndex, ParOptions);
            using (new EditorGUILayout.HorizontalScope())
            {
                randomSeed = EditorGUILayout.ToggleLeft("Random seed", randomSeed, GUILayout.Width(110));
                using (new EditorGUI.DisabledScope(randomSeed))
                    seed = Mathf.Max(0, EditorGUILayout.IntField(seed));
            }
            customize = EditorGUILayout.Foldout(customize, "Customize (pin individual knobs)", true);
            if (customize)
            {
                for (int i = 0; i < Knobs.Length; i++)
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        knobOn[i] = EditorGUILayout.ToggleLeft(Knobs[i].label, knobOn[i], GUILayout.Width(110));
                        using (new EditorGUI.DisabledScope(!knobOn[i]))
                            knobValue[i] = EditorGUILayout.Slider(knobValue[i], 0f, 1f);
                    }
            }
            usePreferences = EditorGUILayout.Toggle(new GUIContent("Use learned taste", "Steer generation toward holes you rated up"), usePreferences);
            scatterEnabled = EditorGUILayout.Toggle("Trees, rocks, grass", scatterEnabled);
        }

        void DrawLastHole()
        {
            if (last == null) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"{last.id}  ·  par {last.par}  ·  theme {last.theme}", EditorStyles.boldLabel);
            if (lastPreview)
            {
                float size = Mathf.Min(position.width - 20, 320);
                GUILayout.Label(lastPreview, GUILayout.Width(size), GUILayout.Height(size));
            }
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(lastRated))
            {
                if (GUILayout.Button("👍  Like", GUILayout.Height(30))) Guard(() => Rate("up"));
                if (GUILayout.Button("👎  Dislike", GUILayout.Height(30))) Guard(() => Rate("down"));
            }
            if (lastRated) EditorGUILayout.LabelField(" ", "Rated. Thanks!");
        }

        void DrawLearning()
        {
            EditorGUILayout.LabelField("Learning", EditorStyles.boldLabel);
            if (status != null)
            {
                string taste = status.likes.Count + status.dislikes.Count == 0 ? "no clear preferences yet"
                    : string.Join(", ", status.likes.Select(l => "likes " + l).Concat(status.dislikes.Select(d => "dislikes " + d)));
                EditorGUILayout.HelpBox($"{status.ratings} rated ({status.up} 👍). Model trained on {status.trainedOn}: {taste}.", MessageType.None);
            }
            using (new EditorGUI.DisabledScope(!PythonRunner.IsConfigured))
                if (GUILayout.Button("Retrain From Ratings")) Guard(Retrain);
        }

        void Generate()
        {
            var args = new List<string> { "generate", "--preset", presetNames[presetIndex], "--out", Path.Combine(PythonRunner.ProjectRoot, OutputFolder) };
            if (!randomSeed) args.AddRange(new[] { "--seed", seed.ToString() });
            if (parIndex > 0) args.AddRange(new[] { "--par", ParOptions[parIndex] });
            if (!usePreferences) args.Add("--no-model");
            var pins = Enumerable.Range(0, Knobs.Length).Where(i => customize && knobOn[i])
                .Select(i => $"{Knobs[i].param}={knobValue[i].ToString("0.##", CultureInfo.InvariantCulture)}").ToList();
            if (pins.Count > 0) { args.Add("--set"); args.AddRange(pins); }

            string output = PythonRunner.Run("Generating hole layout and terrain", args.ToArray());
            PythonRunner.Log(output);
            last = PythonRunner.LastJson<GeneratedHoleResult>(output);
            lastRated = false;
            seed = last.seed;

            string folder = PythonRunner.ToAssetPath(last.package);
            AssetDatabase.Refresh();
            lastPreview = AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/preview.png");

            var pkg = HolePackage.Load($"{folder}/{HolePackage.FileName}");
            var catalog = AssetCatalog.Load();
            var fallback = new HoleBuildOptions
            {
                layers = CourseDefaults.Layers(),
                scatter = scatterEnabled ? CourseDefaults.Scatter() : null,
                seed = last.seed,
            };
            ThemeResolver.Build(pkg, ThemeResolver.ThemeFor(pkg, catalog), catalog, fallback);
        }

        void Rate(string rating)
        {
            PythonRunner.Log(PythonRunner.Run("Saving rating", "rate", last.package, rating));
            lastRated = true;
            RefreshFromPython();
        }

        void Retrain()
        {
            PythonRunner.Log(PythonRunner.Run("Training preference model", "train"));
            RefreshFromPython();
        }

        static void Guard(Action action)
        {
            try { action(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("Course Generator", e.Message, "OK");
            }
        }
    }
}
