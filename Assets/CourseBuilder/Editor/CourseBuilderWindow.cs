using System;
using System.Linq;
using UnityEditor;
using GolfSim.Course;
using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>Golf > Course Builder: pick a hole package and generate it into the open scene.</summary>
    public class CourseBuilderWindow : EditorWindow
    {
        const string PackageRoot = "Assets/CourseData";
        const string PrepCommand =
            "cd Tools/course_prep\n" +
            ".venv/bin/python prep_hole.py fetch --bbox S,W,N,E --out ../../CourseSources/<course>/osm.json\n" +
            ".venv/bin/python prep_hole.py list  --osm ../../CourseSources/<course>/osm.json\n" +
            ".venv/bin/python prep_hole.py build --osm ../../CourseSources/<course>/osm.json --hole 3 --course South";

        string[] packagePaths = Array.Empty<string>();
        string[] packageLabels = Array.Empty<string>();
        int selected;
        HolePackage preview;
        SurfaceLayerSet layers;
        ScatterSet scatter;
        bool scatterEnabled = true;
        bool useThemes = true;
        CourseTheme themeOverride;
        AssetCatalog catalog;
        int seed = 1;
        int blurRadius = 1;
        Vector2 scroll;

        [MenuItem("Golf/Course Builder")]
        public static void Open() => GetWindow<CourseBuilderWindow>("Course Builder");

        void OnEnable()
        {
            if (!layers) layers = SurfaceLayerSet.LoadOrCreateDefault();
            if (!scatter) scatter = ScatterSet.LoadOrCreateDefault();
            catalog = AssetCatalog.Load();
            RefreshPackages();
        }

        void OnProjectChange()
        {
            catalog = AssetCatalog.Load();
            RefreshPackages();
        }

        void RefreshPackages()
        {
            string current = selected < packagePaths.Length ? packagePaths[selected] : null;
            packagePaths = AssetDatabase.IsValidFolder(PackageRoot)
                ? AssetDatabase.FindAssets("t:TextAsset", new[] { PackageRoot })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => p.EndsWith("/" + HolePackage.FileName))
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .ToArray()
                : Array.Empty<string>();
            packageLabels = packagePaths.Select(p => p.Substring(PackageRoot.Length + 1).Replace("/" + HolePackage.FileName, "")).ToArray();
            selected = Mathf.Max(0, Array.IndexOf(packagePaths, current));
            LoadPreview();
            Repaint();
        }

        void LoadPreview()
        {
            preview = null;
            if (packagePaths.Length == 0) return;
            try { preview = HolePackage.Load(packagePaths[selected]); }
            catch (Exception e) { Debug.LogError($"[CourseBuilder] {e.Message}"); }
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Hole package", EditorStyles.boldLabel);

            if (packagePaths.Length == 0)
            {
                EditorGUILayout.HelpBox($"No {HolePackage.FileName} found under {PackageRoot}. Build one with:\n\n{PrepCommand}", MessageType.Info);
                if (GUILayout.Button("Refresh")) RefreshPackages();
                EditorGUILayout.EndScrollView();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                selected = EditorGUILayout.Popup(selected, packageLabels);
                if (EditorGUI.EndChangeCheck()) LoadPreview();
                if (GUILayout.Button("Refresh", GUILayout.Width(70))) RefreshPackages();
            }

            if (preview != null) DrawSummary(preview);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            DrawThemeSettings();
            using (new EditorGUI.DisabledScope(ActiveTheme != null))
                layers = (SurfaceLayerSet)EditorGUILayout.ObjectField("Surface layers", layers, typeof(SurfaceLayerSet), false);
            blurRadius = EditorGUILayout.IntSlider(new GUIContent("Edge blend (px)", "Softens edges between surfaces"), blurRadius, 0, 4);
            scatterEnabled = EditorGUILayout.Toggle("Trees, rocks, grass", scatterEnabled);
            using (new EditorGUI.DisabledScope(!scatterEnabled))
            {
                using (new EditorGUI.DisabledScope(ActiveTheme != null))
                    scatter = (ScatterSet)EditorGUILayout.ObjectField("Scatter rules", scatter, typeof(ScatterSet), false);
                using (new EditorGUILayout.HorizontalScope())
                {
                    seed = EditorGUILayout.IntField(new GUIContent("Seed", "Same seed = same layout"), seed);
                    if (GUILayout.Button("Shuffle", GUILayout.Width(70))) seed = UnityEngine.Random.Range(1, 99999);
                }
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(preview == null || layers == null || EditorApplication.isPlaying))
                if (GUILayout.Button("Generate Hole", GUILayout.Height(36)))
                    Generate();

            DrawViewButtons();
            EditorGUILayout.EndScrollView();
        }

        /// <summary>Theme that will dress the selected package, or null to use the layer / scatter sets.</summary>
        CourseTheme ActiveTheme =>
            !useThemes || !catalog ? null : themeOverride ? themeOverride : preview != null ? ThemeResolver.ThemeFor(preview, catalog) : null;

        void DrawThemeSettings()
        {
            if (!catalog)
            {
                EditorGUILayout.HelpBox("No asset catalog yet: using the sets below. Run Golf > Catalog > Build Catalog From Current Assets to switch to themes.", MessageType.None);
                return;
            }
            useThemes = EditorGUILayout.Toggle(new GUIContent("Use catalog themes", "Dress the hole with a CourseTheme instead of the sets below"), useThemes);
            using (new EditorGUI.DisabledScope(!useThemes))
            {
                themeOverride = (CourseTheme)EditorGUILayout.ObjectField(
                    new GUIContent("Theme override", "Empty = the package's own theme, else the catalog default"), themeOverride, typeof(CourseTheme), false);
                if (useThemes) EditorGUILayout.LabelField(" ", $"Using: {(ActiveTheme ? ActiveTheme.name : "(none)")}");
            }
        }

        static void DrawViewButtons()
        {
            var hole = HoleNavigation.FindHole();
            if (!hole) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Scene View: Hole {hole.holeRef}", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
                foreach (HoleView view in System.Enum.GetValues(typeof(HoleView)))
                    if (GUILayout.Button(view.ToString(), GUILayout.Height(24)))
                        HoleNavigation.ShowInSceneView(hole, view);
            EditorGUILayout.HelpBox("Press Play to fly around: right mouse looks, WASD moves, 1/2/3 jump to tee/green/overview.", MessageType.None);
        }

        static void DrawSummary(HolePackage pkg)
        {
            float spacing = pkg.sizeMeters / (pkg.heightmapResolution - 1);
            var counts = pkg.areas.GroupBy(a => a.surface).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}");
            EditorGUILayout.HelpBox(
                $"{pkg.DisplayName}\n" +
                $"Tee to pin: {Vector2.Distance(pkg.tee, pkg.pin):0} m\n" +
                $"Terrain: {pkg.sizeMeters:0} m square, {pkg.heightmapResolution}px ({spacing:0.00} m/px)\n" +
                $"Elevation: {pkg.minElevation:0.0} to {pkg.maxElevation:0.0} m\n" +
                $"Areas: {string.Join(", ", counts)}",
                MessageType.None);
        }

        void Generate()
        {
            try
            {
                var options = new HoleBuildOptions
                {
                    layers = layers,
                    scatter = scatterEnabled ? scatter : null,
                    seed = seed,
                    blurRadius = blurRadius,
                };
                var pkg = HolePackage.Load(packagePaths[selected]);
                var root = ThemeResolver.Build(pkg, ActiveTheme, catalog, options);
                Debug.Log($"[CourseBuilder] Generated '{root.name}'");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("Course Builder", $"Generation failed:\n{e.Message}", "OK");
            }
        }
    }
}
