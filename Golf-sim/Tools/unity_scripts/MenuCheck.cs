// Dev check for the menus (main menu cards, "Choose a course", Practice, Settings, the pause menu), run with the Unity
// CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/MenuCheck.cs --entry MenuCheck.Check
// Edit mode. Check: the menu scene's cards (Play, Practice, Scores, Settings; Hole Simulator only as a developer mode),
// every course's preset ids against Tools/course_gen/style.py and its art and theme, the Sunset sky, and the settings
// (saved in PlayerPrefs and applied: wind scale, putting assist, round length, quality level and URP asset, developer
// modes), each put back afterwards.
//   printf "http://127.0.0.1:8871\n<its request log>" > Temp/menucheck_args.txt   (a stand-in trainer that serves the hole cache, with
//   unity command run_script ... --entry MenuCheck.TrainerStart   /mode/new and /mode/old; never the hosted trainer)
//   unity command run_script ... --entry MenuCheck.TrainerResult  (until not WAIT; the Editor only ticks while frontmost)
// Trainer: TrainerHoles.Fetch with course types: the preset param, a type with enough holes, one with too few (topped
// up, mixed), an older trainer that ignores preset, an unknown id (400, then any type), the trainer down (the cache),
// and GET /api/game/presets. Puts back holes/recent.json and the config.
// Play mode (GolfServer pointed at a local server first, see the round checks):
//   PlayNav: card order, Left/Right, Play -> courses, the grid's and the options' Up/Down/Left/Right order, Back,
//   Practice (2 cards; 3 with developer modes), Settings (rows in order, the phones' "settings" screen) and Back.
//   PlayCourse (args file: trainer URL): picks Winter at Night with 1 hole on the courses page and tees off; then
//   PlayCourseState: the round's course, the trainer request's preset=winter and the night sky; PlayWind (in a hole):
//   the Wind setting changes the hole's wind at once.
//   Shot <page[:course id]|settings:Section|pause|pause-settings> sets up a screen for a capture.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using GolfSim.Ball;
using GolfSim.Course;
using GolfSim.Game;
using GolfSim.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class MenuCheck
{
    const string ArgsPath = "Temp/menucheck_args.txt", Out = "Temp/menucheck_result.txt", ShotArgs = "Temp/menucheck_shot.txt";
    static readonly StringBuilder log = new StringBuilder();
    static int fails;

    static void Expect(bool ok, string what)
    {
        log.AppendLine($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok) fails++;
    }

    static string Done()
    {
        string text = $"{(fails == 0 ? "PASS" : $"FAIL ({fails})")}\n{log}";
        log.Clear();
        fails = 0;
        return text;
    }

    // ---- edit mode ----

    public static string Check()
    {
        // The menu scene's cards.
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Additive);
        try
        {
            var menu = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<MainMenu>(true)).First(m => m);
            var kinds = menu.modes.Select(m => m.kind).ToArray();
            Expect(kinds.SequenceEqual(new[] { GameMode.ModeKind.Round, GameMode.ModeKind.Practice, GameMode.ModeKind.Scores, GameMode.ModeKind.Settings }),
                   $"main cards: {string.Join(", ", menu.modes.Select(m => m.title))}");
            Expect(menu.modes.Concat(menu.practice).All(m => m.title != "Hole Simulator"), "Hole Simulator isn't a player card");
            Expect(menu.developerModes.Any(m => m.title == "Hole Simulator"), "Hole Simulator is a developer mode");
            Expect(menu.practice.Select(m => m.practice).SequenceEqual(new[] { PracticeMode.DrivingRange, PracticeMode.PuttingGreen }),
                   $"practice cards: {string.Join(", ", menu.practice.Select(m => m.title))}");
            Expect(menu.modes.Concat(menu.practice).All(m => m.banner), "every card has art");
        }
        finally { EditorSceneManager.CloseScene(scene, true); }

        // Courses against the generator's presets.
        var presets = Regex.Matches(File.ReadAllText("Tools/course_gen/style.py"), "_preset\\(\"(\\w+)\"").Select(m => m.Groups[1].Value).ToList();
        Expect(presets.Count == 11, $"style.py presets: {string.Join(", ", presets)}");
        var all = CourseCatalog.All;
        Expect(all.Select(c => c.id).Distinct().Count() == all.Length, $"{all.Length} courses, unique ids");
        foreach (var c in all)
        {
            Expect(c.presets.All(presets.Contains), $"{c.title}: presets [{string.Join(",", c.presets)}] are generator presets");
            Expect(c.Banner, $"{c.title}: art {(c.Banner ? $"{c.Banner.width}x{c.Banner.height}" : "missing")}");
            string theme = c.theme ?? c.presets.FirstOrDefault();
            if (theme != null)
                Expect(ThemeScenery.For(theme).theme == theme && CourseRound.Load().themes.themes.Any(t => t.name == theme),
                       $"{c.title}: dressed as '{theme}' (scenery row and baked theme)");
        }
        Expect(presets.All(p => all.Any(c => c.presets.SequenceEqual(new[] { p }))), "every preset has its own course card");
        string[] want = { "Classic Parkland", "Autumn", "Tropical Island", "Red Rock Canyon", "Winter", "Heathland", "Links", "Desert",
                          "Mountain", "Lakeside", "Forest", "Coastal", "Night Golf", "Sunset Round", "Surprise Me" };
        Expect(all.Select(c => c.title).SequenceEqual(want), "course order: " + string.Join(", ", all.Select(c => c.title)));
        Expect(CourseCatalog.Find("night").sky == SkyChoice.Night && CourseCatalog.Find("sunset").sky == SkyChoice.Sunset &&
               CourseCatalog.Find("surprise").sky == SkyChoice.Auto && !CourseCatalog.Find("surprise").IsType, "Night, Sunset and Surprise Me skies");
        Expect(CourseCatalog.SkyFor(CourseCatalog.Find("winter"), SkyOption.Night) == SkyChoice.Night &&
               CourseCatalog.SkyFor(CourseCatalog.Find("night"), SkyOption.CourseDefault) == SkyChoice.Night &&
               CourseCatalog.SkyFor(CourseCatalog.Find("parkland"), SkyOption.GoldenHour) == SkyChoice.GoldenHour, "Time of day maps onto the sky choice");

        // Sunset: golden hour on the first tee, dusk by SunsetDuskBy, never night, never back.
        bool sunset = true;
        for (uint seed = 1; seed < 300; seed++)
        for (int h = 0; h < 18; h++)
        {
            var t = SkySchedule.For(SkyChoice.Sunset, seed, h, ThemeScenery.For("parkland"));
            sunset &= h == 0 ? t == TimeOfDay.GoldenHour : t is TimeOfDay.GoldenHour or TimeOfDay.Dusk;
            sunset &= h < SkySchedule.SunsetDuskBy || t == TimeOfDay.Dusk;
            sunset &= h == 0 || t >= SkySchedule.For(SkyChoice.Sunset, seed, h - 1, ThemeScenery.For("parkland"));
        }
        Expect(sunset && SkyChoice.Sunset.Fixed() == null, "Sunset: golden hour into dusk");

        Settings();
        return Done();
    }

    /// <summary>Each setting saved and applied, then put back.</summary>
    static void Settings()
    {
        var (wind, assist, holes, quality, dev, level) = (GameSettings.Wind, GameSettings.PuttingAssist, GameSettings.RoundLength,
                                                         GameSettings.Quality, GameSettings.DeveloperModes, QualitySettings.GetQualityLevel());
        int changed = 0;
        Action count = () => changed++;
        GameSettings.Changed += count;
        try
        {
            float[] scales = { 0f, 0.5f, 1f, 1.5f };
            foreach (WindStrength w in Enum.GetValues(typeof(WindStrength)))
            {
                GameSettings.Wind = w;
                Expect(GameSettings.WindScale == scales[(int)w] && PlayerPrefs.GetInt("GolfSim.Settings.Wind") == (int)w, $"wind {w}: scale {GameSettings.WindScale}");
            }
            foreach (PuttingAssist a in Enum.GetValues(typeof(PuttingAssist)))
            {
                GameSettings.PuttingAssist = a;
                Expect(PuttPreview.Assist == a && PlayerPrefs.GetInt("GolfSim.PuttingAssist") == (int)a, $"putting assist {a} (PuttPreview)");
            }
            GameSettings.RoundLength = 18;
            Expect(GameSettings.RoundLength == 18 && PlayerPrefs.GetInt("GolfSim.Settings.RoundLength") == 18, "round length 18");
            GameSettings.RoundLength = 7;
            Expect(GameSettings.RoundLength == 9, "round length is 9 or 18");
            GameSettings.Quality = GraphicsQuality.Performance;
            var asset = QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline;
            Expect(QualitySettings.names[QualitySettings.GetQualityLevel()] == "Performant" && asset && asset.name == "GolfSim_URP_Performant",
                   $"Performance: level {QualitySettings.names[QualitySettings.GetQualityLevel()]}, {(asset ? asset.name : "no asset")}");
            GameSettings.Quality = GraphicsQuality.High;
            asset = QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline;
            Expect(QualitySettings.names[QualitySettings.GetQualityLevel()] == "High Fidelity" && asset && asset.name == "GolfSim_URP_High",
                   $"High: level {QualitySettings.names[QualitySettings.GetQualityLevel()]}, {(asset ? asset.name : "no asset")}");
            GameSettings.DeveloperModes = !dev;
            Expect(GameSettings.DeveloperModes == !dev && PlayerPrefs.GetInt("GolfSim.Settings.DeveloperModes") == (!dev ? 1 : 0), "developer modes toggle");
            Expect(changed >= 10, $"Changed fired on each change ({changed})");
        }
        finally
        {
            GameSettings.Changed -= count;
            GameSettings.Wind = wind;
            GameSettings.PuttingAssist = assist;
            GameSettings.RoundLength = holes;
            GameSettings.Quality = quality;
            GameSettings.DeveloperModes = dev;
            QualitySettings.SetQualityLevel(level, true);
        }
    }

    // ---- trainer: course types (edit mode, a stand-in trainer) ----

    public static string TrainerStart()
    {
        string url = Args[0];
        if (!url.StartsWith("http://127.0.0.1") && !url.StartsWith("http://localhost")) return "FAIL local stand-in trainers only";
        var config = ServerConfig.Load();
        var (oldUrl, oldUse, oldSelection) = (config.trainerUrl, config.useTrainerHoles, config.holeSelection);
        string env = Environment.GetEnvironmentVariable("GOLF_TRAINER_KEY");
        string recentPath = Path.Combine(TrainerHoles.CacheFolder, "recent.json");
        string recent = File.Exists(recentPath) ? File.ReadAllText(recentPath) : null;
        Environment.SetEnvironmentVariable("GOLF_TRAINER_KEY", "menucheck"); // never the real key, even locally
        (config.trainerUrl, config.useTrainerHoles, config.holeSelection) = (url, true, ServerConfig.HoleSelection.Random);
        if (File.Exists(Out)) File.Delete(Out);
        var known = typeof(TrainerHoles).GetProperty("KnownPresets", BindingFlags.Public | BindingFlags.Static);

        IEnumerator Steps()
        {
            TrainerHoles.Result r = null;
            IEnumerator Fetch(params string[] p) { r = null; return TrainerHoles.Fetch(config, 9, p, null, null, () => false, x => r = x); }
            string Kinds() => r?.holes == null ? r?.error : string.Join(",", r.holes.Select(h => TrainerHoles.PresetOf(h.hole)));

            yield return Mode(url, "new");
            known.SetValue(null, null); // nothing asked yet: an unknown id goes out as is
            yield return Fetch("coastal");
            Expect(r?.holes?.Count == 9 && r.mixed, $"unknown type 'coastal': 400, then any type, mixed ({Kinds()})");
            yield return TrainerHoles.FetchPresets(config, _ => { });
            Expect(TrainerHoles.KnownPresets?.Length == 11 && TrainerHoles.Playable(new[] { "forest" }) > 0, $"presets: forest {TrainerHoles.Playable(new[] { "forest" })} holes");
            yield return Fetch("forest");
            Expect(r?.holes?.Count == 9 && r.holes.All(h => TrainerHoles.PresetOf(h.hole) == "forest") && !r.mixed && !r.offline, $"forest round: {Kinds()}");
            yield return Fetch("links");
            int links = TrainerHoles.Playable(new[] { "links" });
            Expect(r?.holes?.Count == 9 && r.mixed && r.holes.Take(links).All(h => TrainerHoles.PresetOf(h.hole) == "links"),
                   $"links ({links} holes): links first, then others, mixed: {Kinds()}");
            yield return Fetch("winter");
            Expect(r?.holes?.Count == 9 && r.mixed, $"winter (none yet): any type, mixed: {Kinds()}");
            yield return Fetch();
            Expect(r?.holes?.Count == 9 && !r.mixed, $"Surprise Me (any type): {Kinds()}");
            yield return Mode(url, "old");
            yield return Fetch("forest");
            Expect(r?.holes?.Count == 9 && r.holes.All(h => TrainerHoles.PresetOf(h.hole) == "forest") && !r.mixed,
                   $"older trainer ignoring preset: forest from its draw and the cache: {Kinds()}");
            bool? none = false;
            yield return TrainerHoles.FetchPresets(config, p => none = p == null);
            Expect(none == true, "older trainer: no /presets (null)");
            config.trainerUrl = "http://127.0.0.1:1";
            yield return Fetch("lakes");
            Expect(r?.holes?.Count == 9 && r.offline && r.holes.All(h => TrainerHoles.PresetOf(h.hole) == "lakes"), $"trainer down: lakes from the cache: {Kinds()}");
            config.trainerUrl = url;
            yield return Mode(url, "new");
        }

        var routine = SafeCoroutine.Run(Steps(), e => Expect(false, $"exception {e}"));
        EditorApplication.CallbackFunction tick = null;
        tick = () =>
        {
            bool more;
            try { more = routine.MoveNext(); }
            catch (Exception e) { Expect(false, e.ToString()); more = false; }
            if (more) return;
            EditorApplication.update -= tick;
            (config.trainerUrl, config.useTrainerHoles, config.holeSelection) = (oldUrl, oldUse, oldSelection);
            Environment.SetEnvironmentVariable("GOLF_TRAINER_KEY", env);
            if (recent != null) File.WriteAllText(recentPath, recent);
            File.WriteAllText(Out, Done());
        };
        EditorApplication.update += tick;
        return "started; run MenuCheck.TrainerResult until it is not WAIT";
    }

    public static string TrainerResult() => File.Exists(Out) ? File.ReadAllText(Out) : "WAIT";

    static IEnumerator Mode(string url, string mode)
    {
        using var req = UnityWebRequest.Get($"{url}/mode/{mode}");
        yield return req.SendWebRequest();
    }

    // ---- play mode ----

    static RoundDirector D => RoundDirector.Instance;
    static MainMenu Menu => Object.FindAnyObjectByType<MainMenu>();
    static VisualElement MenuRoot => Menu.GetComponent<UIDocument>().rootVisualElement;
    static string Title(string name) => MenuRoot.Q<Label>(name).text;
    static string Screen => D.BuildState().screen;

    public static string PlayNav()
    {
        if (!Application.isPlaying || !Menu) return "WAIT enter Play mode on the main menu";
        Expect(ServerConfig.Load().ActiveUrl.StartsWith("ws://127.0.0.1"), $"server {ServerConfig.Load().ActiveUrl} is local");
        bool dev = GameSettings.DeveloperModes;
        var titles = new List<string>();
        for (int i = 0; i < 5; i++) { titles.Add(Title("title")); NavInput.Push(NavKey.Right); }
        Expect(titles.SequenceEqual(new[] { "Play a Round", "Practice", "Scores", "Settings", "Play a Round" }), "cards, Right wraps: " + string.Join(" > ", titles));
        NavInput.Push(NavKey.Left);
        NavInput.Push(NavKey.Left);
        Expect(Title("title") == "Settings", "Left wraps back to Settings");
        NavInput.Push(NavKey.Right);

        if (!RoundDirector.CanResume)
        {
            NavInput.Push(NavKey.Select);
            Expect(Menu.PageName == "courses", "Select on Play opens the courses");
            var picker = Menu.Courses;
            for (int i = 0; i < 20 && picker.Selected != CourseCatalog.All[0]; i++) NavInput.Push(NavKey.Left);
            var path = new List<string> { picker.Selected.id };
            foreach (var key in new[] { NavKey.Right, NavKey.Down, NavKey.Down, NavKey.Left, NavKey.Up })
            {
                NavInput.Push(key);
                path.Add(picker.Selected.id);
            }
            Expect(path.SequenceEqual(new[] { "parkland", "autumn", "links", "coastal", "forest", "heathland" }), "grid: Right, Down, Down, Left, Up: " + string.Join(" > ", path));
            NavInput.Push(NavKey.Down);
            NavInput.Push(NavKey.Down); // off the grid: Holes
            var grid = MenuRoot.Q("course-grid");
            Expect(grid.ClassListContains("grid--unfocused"), "Down from the last row reaches the options");
            int holes = GameSettings.RoundLength;
            NavInput.Push(holes == 9 ? NavKey.Right : NavKey.Left);
            Expect(GameSettings.RoundLength != holes, $"Holes: {holes} -> {GameSettings.RoundLength}");
            NavInput.Push(holes == 9 ? NavKey.Left : NavKey.Right);
            var sky = CourseCatalog.LastSky;
            NavInput.Push(NavKey.Down);
            NavInput.Push(sky == SkyOption.Night ? NavKey.Left : NavKey.Right);
            Expect(CourseCatalog.LastSky != sky, $"Time of day: {sky.Label()} -> {CourseCatalog.LastSky.Label()}");
            CourseCatalog.LastSky = sky;
            NavInput.Push(NavKey.Up);
            NavInput.Push(NavKey.Up);
            Expect(!grid.ClassListContains("grid--unfocused") && picker.Selected.id == "forest", $"Up, Up: back on the cards ({picker.Selected.id})");
            Expect(Screen == "menu", "the phones see the menu");
            NavInput.Push(NavKey.Back);
            Expect(Menu.PageName == "home" && Title("title") == "Play a Round", "Back: the main cards");
        }

        NavInput.Push(NavKey.Right); // Practice
        NavInput.Push(NavKey.Select);
        var practice = MenuRoot.Q("practice-cards");
        Expect(Menu.PageName == "practice" && practice.childCount == (dev ? 3 : 2), $"Practice: {practice.childCount} cards (developer modes {dev})");
        GameSettings.DeveloperModes = !dev;
        Expect(practice.childCount == (!dev ? 3 : 2), $"developer modes {!dev}: {practice.childCount} cards");
        GameSettings.DeveloperModes = dev;
        NavInput.Push(NavKey.Back);

        NavInput.Push(NavKey.Right);
        NavInput.Push(NavKey.Right); // Settings
        NavInput.Push(NavKey.Select);
        Expect(SettingsScreen.AnyOpen && Screen == "settings", $"Settings open, phones see '{Screen}'");
        var names = new List<string>();
        var rows = MenuRoot.Query(className: "setting").ToList();
        for (int i = 0; i < 16; i++)
        {
            var row = rows.FirstOrDefault(r => r.ClassListContains("setting--selected"));
            names.Add(row?.Q<Label>(className: "setting__name")?.text);
            NavInput.Push(NavKey.Down);
        }
        string[] order = { "Master", "Effects", "Crowd", "Ambience", "Interface", "Wind", "Putting assist", "Round length", "Quality", "Fullscreen",
                           "Updates", "Developer modes", "Developer modes" };
        Expect(names.Distinct().SequenceEqual(order.Distinct()), "Down through the settings (Room, Server, Version skipped): " + string.Join(" > ", names.Distinct()));
        var wind = GameSettings.Wind;
        for (int i = 0; i < 6; i++) NavInput.Push(NavKey.Up); // Developer modes up to Wind
        string on = rows.FirstOrDefault(r => r.ClassListContains("setting--selected"))?.Q<Label>(className: "setting__name")?.text;
        Expect(on == "Wind", $"Up x6: {on}");
        NavInput.Push(wind == WindStrength.Strong ? NavKey.Left : NavKey.Right);
        Expect(GameSettings.Wind != wind, $"Left/Right on Wind: {wind} -> {GameSettings.Wind}");
        GameSettings.Wind = wind;
        NavInput.Push(NavKey.Back);
        Expect(!SettingsScreen.AnyOpen && Screen == "menu", $"Back closes, phones see '{Screen}'");
        NavInput.Push(NavKey.Right); // Play again
        return Done();
    }

    /// <summary>Winter at night, one hole, from the courses page (the trainer in the args file, in memory only).</summary>
    public static string PlayCourse()
    {
        if (!Application.isPlaying || !Menu) return "WAIT enter Play mode on the main menu";
        string url = Args[0];
        if (!url.StartsWith("http://127.0.0.1")) return "FAIL local stand-in trainers only";
        Environment.SetEnvironmentVariable("GOLF_TRAINER_KEY", "menucheck");
        ServerConfig.Load().trainerUrl = url; // put back by PlayCourseState
        Menu.ShowPage("courses");
        var picker = Menu.Courses;
        for (int i = 0; i < 20 && picker.Selected.id != "winter"; i++) NavInput.Push(NavKey.Right);
        GameSettings.RoundLength = 9;
        CourseCatalog.LastSky = SkyOption.Night;
        picker.TeeOff();
        return $"teeing off on {picker.Selected.title} at night: run PlayCourseState once the hole is up";
    }

    public static string PlayCourseState()
    {
        if (D.IsFetching || D.Ball == null) return "WAIT the hole is loading";
        var config = ServerConfig.Load();
        config.trainerUrl = ScriptableObject.CreateInstance<ServerConfig>().trainerUrl; // the default (GolfServer.asset is restored after Play)
        Expect(D.Course?.id == "winter", $"the round's course: {D.Course?.title}");
        Expect(CourseCatalog.Last.id == "winter" && CourseCatalog.LastSky == SkyOption.Night, "the choice is remembered");
        Expect(D.Sky == TimeOfDay.Night, $"sky {D.Sky}");
        string requests = Args.Length > 1 && File.Exists(Args[1]) ? File.ReadAllText(Args[1]) : "";
        Expect(requests.Contains("random-holes?count=9") && requests.Contains("preset=winter"), "the trainer was asked for preset=winter");
        Expect(Screen == "game", $"screen {Screen}");
        CourseCatalog.LastSky = SkyOption.CourseDefault;
        return Done();
    }

    /// <summary>The args file: the stand-in trainer's URL, then (PlayCourseState) its request log.</summary>
    static string[] Args => File.ReadAllLines(ArgsPath).Select(l => l.Trim()).Where(l => l != "").ToArray();

    /// <summary>In a hole: the Wind setting changes the wind on the hole being played at once (and back).</summary>
    public static string PlayWind()
    {
        if (!Application.isPlaying || D.Ball == null) return "WAIT play a hole first";
        var before = GameSettings.Wind;
        int now = D.BuildState().wind;
        GameSettings.Wind = WindStrength.Off;
        int off = D.BuildState().wind;
        GameSettings.Wind = WindStrength.Strong;
        int strong = D.BuildState().wind;
        GameSettings.Wind = before;
        Expect(off == 0 && strong >= now && D.BuildState().wind == now, $"wind {before} {now} mph -> Off {off} -> Strong {strong} -> {D.BuildState().wind}");
        return Done();
    }

    /// <summary>Sets up a screen for a capture: a menu page, settings:&lt;Section&gt;, pause or pause-settings.</summary>
    public static string Shot()
    {
        string what = File.ReadAllText(ShotArgs).Trim();
        var home = Object.FindAnyObjectByType<HomeMenu>();
        if (what.StartsWith("pause"))
        {
            if (!home) return "WAIT not in a hole";
            if (!home.Open) home.Open = true;
            if (what == "pause-settings")
                ((SettingsScreen)typeof(HomeMenu).GetField("settings", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(home)).Show();
            return $"paused ({D.BuildState().screen})";
        }
        if (!Menu) return "WAIT not on the main menu";
        if (what.StartsWith("settings"))
        {
            var settings = (SettingsScreen)typeof(MainMenu).GetField("settings", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Menu);
            if (Menu.PageName != "home") Menu.ShowPage("home");
            settings.Show();
            if (what.Contains(':')) settings.ShowSection(what.Split(':')[1]);
            return $"settings {what}";
        }
        if (SettingsScreen.AnyOpen) NavInput.Push(NavKey.Back);
        if (what.Contains(':')) CourseCatalog.Last = CourseCatalog.Find(what.Split(':')[1]); // courses:autumn
        Menu.ShowPage(what.Split(':')[0]);
        return $"page {Menu.PageName}";
    }
}
