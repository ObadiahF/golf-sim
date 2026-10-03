// Dev setup and checks for the practice facilities (not compiled into the project), run with the Unity CLI:
//   unity command run_script --file Tools/unity_scripts/PracticeModesCheck.cs --entry PracticeModesCheck.<Entry>
//   Modes        (Edit mode) the Driving Range and Putting Green GameMode assets (practice scene HoleSimulator), their
//                banners from Art/ (Banner makes them), and both cards on MainMenu after Hole Simulator
//   MenuRange / MenuGreen  (Play mode, on the main menu) Right to the card and Select, as the phone's D-pad does
//                (MenuShow: Right to the Driving Range card only)
//   ToMenu       (Play mode) back to the main menu, leaving any round
//   PauseRestart / PauseMainMenu  (Play mode, on a facility) the pause menu's Restart / Main Menu; Status: what is on
//   Range        (Play mode, on the range) flags at their distances from the tee; a shot with each club through the
//                remote shot path, the ball back on the tee after each; an aimed shot goes right; state fields
//   Green        (Play mode, on the putting green) the cycle's spots on the green; putts at the read (aimed like the
//                preview), one left short, a mulligan and Down: the ball moves on, made / attempts in state
//   Banner       (Play mode, on a facility) renders the menu banner to Art/<facility>.png
//   Look         (Play mode) lines the camera up for a screenshot (the range: after the last shot; the green: a putt)
//   Swing        (Play mode) a shot through the remote path, run by the game itself (a picture in flight)
//   Overview / CloseUp  (Play mode, on a facility) the fly camera over the whole facility / close to a target
// The Editor doesn't tick Play mode in the background, so the checks call GolfBall.Advance and RunPending themselves.
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using GolfSim.Ball;
using GolfSim.Course;
using GolfSim.Game;
using UnityEngine;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public static class PracticeModesCheck
{
    const string Game = "Assets/GolfSim/Game";
    const float Frame = 0.02f;

    // ---- menu setup (Edit mode) ----

    public static string Modes()
    {
#if UNITY_EDITOR
        if (EditorApplication.isPlaying) return "exit Play mode first";
        var range = Mode("DrivingRange", "Driving Range", PracticeMode.DrivingRange,
            "Hit any club at flags from 50 to 300 yards. Every shot shows its carry, total and offline, then the next ball is teed up.");
        var green = Mode("PuttingGreen", "Putting Green", PracticeMode.PuttingGreen,
            "Short, long and breaking putts to six cups on a big sloping green, with the break line and the phone's power meter.");

        const string menuPath = "Assets/Scenes/MainMenu.unity";
        var scene = EditorSceneManager.GetSceneByPath(menuPath);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(menuPath, OpenSceneMode.Additive);
        var menu = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<MainMenu>()).First(m => m);
        var modes = menu.modes.Where(m => m && m != range && m != green).ToList();
        int after = modes.FindIndex(m => m.name == "HoleSimulator");
        modes.InsertRange(after + 1, new[] { range, green });
        menu.modes = modes.ToArray();
        EditorUtility.SetDirty(menu);
        EditorSceneManager.SaveScene(scene);
        if (opened) EditorSceneManager.CloseScene(scene, true);
        return "main menu cards: " + string.Join(", ", menu.modes.Select(m => m.title));
#else
        return "editor only";
#endif
    }

#if UNITY_EDITOR
    static GameMode Mode(string file, string title, PracticeMode practice, string description)
    {
        string path = $"{Game}/Modes/{file}.asset";
        var mode = AssetDatabase.LoadAssetAtPath<GameMode>(path);
        if (!mode)
        {
            mode = ScriptableObject.CreateInstance<GameMode>();
            AssetDatabase.CreateAsset(mode, path);
        }
        mode.kind = GameMode.ModeKind.Scene;
        mode.title = title;
        mode.description = description;
        mode.sceneName = "HoleSimulator";
        mode.practice = practice;
        string art = $"{Game}/Art/{file}.png";
        if (File.Exists(art))
        {
            AssetDatabase.ImportAsset(art);
            // The same import settings as the Hole Simulator's banner.
            var source = (TextureImporter)AssetImporter.GetAtPath($"{Game}/Art/HoleSimulator.png");
            var target = (TextureImporter)AssetImporter.GetAtPath(art);
            var settings = new TextureImporterSettings();
            source.ReadTextureSettings(settings);
            target.SetTextureSettings(settings);
            target.maxTextureSize = source.maxTextureSize;
            target.textureCompression = source.textureCompression;
            target.SaveAndReimport();
            mode.banner = AssetDatabase.LoadAssetAtPath<Texture2D>(art);
        }
        EditorUtility.SetDirty(mode);
        AssetDatabase.SaveAssets();
        return mode;
    }
#endif

    // ---- getting there from the menu (Play mode) ----

    /// <summary>Back to the main menu (as the pause menu's Main Menu does), leaving any round.</summary>
    public static string ToMenu()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        return "loading the main menu";
    }

    public static string MenuRange() => FromMenu("Driving Range");
    public static string MenuGreen() => FromMenu("Putting Green");
    /// <summary>Selects the Driving Range card without playing it (for a picture of the menu).</summary>
    public static string MenuShow() => FromMenu("Driving Range", play: false);

    static string FromMenu(string title, bool play = true)
    {
        var menu = Object.FindAnyObjectByType<MainMenu>();
        if (!menu) return "not on the main menu";
        int index = System.Array.FindIndex(menu.modes, m => m.title == title);
        if (index < 0) return $"no {title} card";
        for (int i = 0; i < index; i++) NavInput.Push(NavKey.Right);
        if (play) NavInput.Push(NavKey.Select);
        return $"Right x{index}, Select: loading {title} (bring the Editor to the front to let it load)";
    }

    /// <summary>The pause menu, as the phone's Menu button and D-pad: Restart (a fresh session) or Main Menu.</summary>
    public static string PauseRestart() => Pause(1);
    public static string PauseMainMenu() => Pause(3);

    static string Pause(int item)
    {
        if (RoundDirector.Instance?.Facility == null) return "needs Play mode on a facility";
        NavInput.Push(NavKey.Back);
        for (int i = 0; i < item; i++) NavInput.Push(NavKey.Down);
        NavInput.Push(NavKey.Select);
        return $"Back, Down x{item}, Select";
    }

    /// <summary>What is on now: the facility, its counts and the screen the phones are told.</summary>
    public static string Status()
    {
        var d = RoundDirector.Instance;
        var f = d ? d.Facility : null;
        return $"{(f == null ? "no facility" : f.GetType().Name)}, attempts {f?.Attempts ?? 0}, screen {(d ? d.ScreenName : "-")}, " +
               $"scene {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}";
    }

    // ---- the driving range ----

    public static string Range()
    {
        var d = RoundDirector.Instance;
        if (!d || d.Facility is not DrivingRange) return "needs Play mode on the driving range";
        var ball = d.Ball;
        var hole = Object.FindAnyObjectByType<HoleInfo>();
        var log = new StringBuilder();
        bool ok = true;

        // Every flag stands its yardage from the tee.
        foreach (Transform t in hole.transform)
        {
            if (!t.name.StartsWith("Pin")) continue;
            float yards = Round.FlatDistance(hole.TeeWorld, t.position) * ShotData.YardsPerMeter;
            int expected = t.name == "Pin" ? 300 : int.Parse(t.name.Substring(4));
            bool pass = Mathf.Abs(yards - expected) < 0.5f;
            ok &= pass;
            log.AppendLine($"{(pass ? "PASS" : "FAIL")} flag {expected}: {yards:0.0} yd from the tee");
        }

        int before = d.Facility.Attempts;
        foreach (var club in Clubs.Bag.Where(c => !c.IsPutter))
            ok &= Shoot(d, ball, hole, club, 0f, log);
        ok &= Shoot(d, ball, hole, Clubs.Bag[3], 5f, log); // aimed 5° right
        d.Aim(-d.Ball.aimOffset);
        bool counted = d.Facility.Attempts == before + Clubs.Bag.Length;
        ok &= counted;
        var state = d.BuildState();
        bool fields = state.practice == "range" && state.screen == "game" && state.canShoot && !state.putting;
        ok &= fields;
        log.AppendLine($"{(counted ? "PASS" : "FAIL")} attempts {d.Facility.Attempts}; {(fields ? "PASS" : "FAIL")} state {state.ToJson()}");
        return (ok ? "ALL PASS\n" : "SOME FAILED\n") + log;
    }

    static bool Shoot(RoundDirector d, GolfBall ball, HoleInfo hole, Club club, float aim, StringBuilder log)
    {
        d.SetClub(club.name);
        d.Aim(aim - ball.aimOffset);
        var tee = ball.transform.position;
        var aimDir = ball.AimDirection;
        var straight = Vector3.ProjectOnPlane(hole.PinWorld - tee, Vector3.up).normalized;
        float flag = Round.FlatDistance(hole.TeeWorld, hole.PinWorld) * ShotData.YardsPerMeter;
        var s = club.shot;
        var msg = new RemoteShotMessage { type = "shot", id = Random.Range(1, 1 << 30), club = club.name, speed = s.ballSpeed, launch = s.launchAngle, back = s.backspin };
        var ack = Shots.Submit(msg, "PracticeModesCheck", out _);
        for (int i = 0; i < 6000 && ball.InMotion; i++) ball.Advance(Frame);
        var r = ball.Result;
        var rest = ball.transform.position - tee;
        float angle = Vector3.SignedAngle(straight, Vector3.ProjectOnPlane(rest, Vector3.up), Vector3.up);
        string between = d.BuildState().waitReason;
        d.RunPending(); // the next ball
        bool reset = Round.FlatDistance(ball.transform.position, hole.TeeWorld) < 0.05f && ball.Status == BallStatus.Ready && ball.Lie == "tee";
        bool aimed = aim == 0f || Mathf.Abs(angle - aim) < 3f;
        bool pass = ack.status == "ok" && reset && aimed && Vector3.Angle(aimDir, straight) - Mathf.Abs(aim) < 0.1f;
        log.AppendLine($"{(pass ? "PASS" : "FAIL")} {club.name,-7} at the {flag:0} flag{(aim != 0f ? $", aimed {aim:+0}°" : "")}: carry {r.carry * ShotData.YardsPerMeter:0.0}, " +
                       $"total {r.total * ShotData.YardsPerMeter:0.0}, offline {r.offline * ShotData.YardsPerMeter:+0.0;-0.0} yd, finished {angle:+0.0;-0.0}° off straight, " +
                       $"{ball.Status}; between: \"{between}\"; back on the tee: {reset}");
        return pass;
    }

    // ---- the putting green ----

    public static string Green()
    {
        var d = RoundDirector.Instance;
        if (!d || d.Facility is not PuttingGreen) return "needs Play mode on the putting green";
        var ball = d.Ball;
        var hole = Object.FindAnyObjectByType<HoleInfo>();
        var preview = ball.GetComponent<PuttPreview>();
        var log = new StringBuilder();
        bool ok = true;
        for (int n = 0; n < 9; n++)
        {
            var spot = ball.transform.position;
            var pin = hole.PinWorld;
            var state = d.BuildState();
            bool putting = state.putting && ball.Lie == "green" && state.club == Clubs.Putter;
            ok &= putting;
            float aim = BestAim(d, ball, preview);
            float strength = n == 2 ? 0.6f : 1f; // one left short
            float speed = PuttModel.SpeedFor(preview.PlaysAs * strength, preview.Stimp);
            var msg = new RemoteShotMessage { type = "shot", id = Random.Range(1, 1 << 30), club = Clubs.Putter, speed = speed, launch = 1f };
            var ack = Shots.Submit(msg, "PracticeModesCheck", out _);
            for (int i = 0; i < 3000 && ball.InMotion; i++) ball.Advance(Frame);
            string result = ball.Status == BallStatus.Holed ? "holed" : $"{Round.FlatDistance(ball.transform.position, pin):0.00} m away";
            if (n == 4)
            {
                d.Mulligan(); // the same putt again
                bool same = Round.FlatDistance(ball.transform.position, spot) < 0.01f && hole.PinWorld == pin;
                ok &= same;
                log.AppendLine($"{(same ? "PASS" : "FAIL")} mulligan: back on the same spot, same cup");
                continue;
            }
            d.RunPending();
            bool moved = Round.FlatDistance(ball.transform.position, spot) > 0.5f && ball.Status == BallStatus.Ready;
            ok &= moved && ack.status == "ok";
            log.AppendLine($"{(putting && moved ? "PASS" : "FAIL")} putt {n + 1}: {state.puttDistance:0.0} m, rise {state.elevation * 100:+0;-0} cm, plays {state.puttPlaysAs:0.0} m, " +
                           $"stimp {state.stimp}, aim {aim:+0;-0}°, hit at {strength:P0} of the read: {result}; next ball moved: {moved}");
        }
        var before = d.BuildState().puttDistance;
        NavInput.Push(NavKey.Down);
        bool stepped = d.BuildState().puttDistance != before;
        ok &= stepped;
        var final = d.BuildState();
        bool counted = final.practice == "puttingGreen" && final.attempts == 9 && final.made == d.Facility.Made;
        ok &= counted;
        log.AppendLine($"{(stepped ? "PASS" : "FAIL")} Down: next putt; {(counted ? "PASS" : "FAIL")} made {final.made} of {final.attempts}; state {final.ToJson()}");
        return (ok ? "ALL PASS\n" : "SOME FAILED\n") + log;
    }

    /// <summary>The aim whose predicted line passes closest to the cup (what a player reading the break would do).</summary>
    static float BestAim(RoundDirector d, GolfBall ball, PuttPreview preview)
    {
        var hole = Object.FindAnyObjectByType<HoleInfo>();
        float best = 0f, closest = float.MaxValue;
        for (float a = -10f; a <= 10f; a += 0.5f)
        {
            ball.aimOffset = a;
            preview.Refresh();
            float miss = preview.Path.Min(p => Vector3.ProjectOnPlane(p - hole.PinWorld, Vector3.up).magnitude);
            if (miss < closest) { closest = miss; best = a; }
        }
        ball.aimOffset = 0f;
        d.Aim(best);
        preview.Refresh();
        return best;
    }

    // ---- pictures ----

    /// <summary>Hits the selected club's (the range: a 7 iron's) typical shot through the remote path and lets the game run it (for a screenshot in flight).</summary>
    public static string Swing()
    {
        var d = RoundDirector.Instance;
        if (!d || !d.Ball) return "needs Play mode on a hole";
        if (d.Facility is DrivingRange) d.SetClub("7 Iron");
        var s = Clubs.Find(d.Club).shot;
        var msg = new RemoteShotMessage { type = "shot", id = Random.Range(1, 1 << 30), club = d.Club, speed = s.ballSpeed, launch = s.launchAngle, back = s.backspin };
        return Shots.Submit(msg, "PracticeModesCheck", out _).ToJson();
    }

    /// <summary>Lines the camera up (the shot panel's line-up: behind the ball, or low behind a putt) and ticks the views.</summary>
    public static string Look()
    {
        var d = RoundDirector.Instance;
        if (!d || !d.Ball) return "needs Play mode on a hole";
        var panel = d.Ball.GetComponent<ShotPanel>();
        if (panel) panel.LineUp();
        d.SetClub(d.Club); // publishes: the HUD and the putting view follow
        Tick(d.Ball);
        return d.BuildState().ToJson();
    }

    /// <summary>The fly camera high behind the range's tee (or over the green), for a screenshot of the whole facility.</summary>
    public static string Overview() => Fly(range: (new Vector3(0f, 45f, -45f), new Vector3(0f, 0f, 170f)), green: (new Vector3(0f, 26f, -30f), new Vector3(0f, -1.5f, 0f)));

    /// <summary>The fly camera in front of the 150 yd target (range) or low across the green, for a close look.</summary>
    public static string CloseUp() => Fly(range: (new Vector3(-6f, 4f, 112f), new Vector3(0f, 1f, 140f)), green: (new Vector3(-12f, 3f, -12f), new Vector3(0f, -1.5f, 0f)));

    static string Fly((Vector3 from, Vector3 to) range, (Vector3 from, Vector3 to) green)
    {
        var d = RoundDirector.Instance;
        if (d?.Facility == null) return "needs Play mode on a facility";
        var hole = Object.FindAnyObjectByType<HoleInfo>();
        var origin = d.Facility is DrivingRange ? hole.TeeWorld : hole.transform.TransformPoint(new Vector3(64f, 1.5f, 64f));
        var (from, to) = d.Facility is DrivingRange ? range : green;
        var cam = Camera.main.GetComponent<HoleFlyCamera>();
        cam.StopFollowing();
        cam.JumpTo(new Pose(origin + from, Quaternion.LookRotation(to - from)));
        return "camera placed";
    }

    public static string Banner()
    {
        var d = RoundDirector.Instance;
        if (d?.Facility == null) return "needs Play mode on a facility";
        var hole = Object.FindAnyObjectByType<HoleInfo>();
        string file = d.Facility is DrivingRange ? "DrivingRange" : "PuttingGreen";
        Vector3 pos, look;
        if (d.Facility is DrivingRange)
        {
            pos = hole.TeeWorld + new Vector3(-14f, 9f, -22f);
            look = hole.TeeWorld + new Vector3(4f, 0f, 150f);
        }
        else
        {
            var green = hole.transform.TransformPoint(new Vector3(64f, 1.5f, 64f));
            pos = green + new Vector3(-16f, 7f, -22f);
            look = green + new Vector3(2f, -1f, 2f);
        }
        string path = $"{Game}/Art/{file}.png";
        Render(pos, Quaternion.LookRotation(look - pos), 50f, path);
        return $"saved {path} (run Modes in Edit mode to use it)";
    }

    static void Render(Vector3 pos, Quaternion rot, float fov, string path)
    {
        const int w = 1600, h = 900;
        var go = new GameObject("BannerCamera") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        if (Camera.main) cam.CopyFrom(Camera.main);
        go.transform.SetPositionAndRotation(pos, rot);
        cam.fieldOfView = fov;
        cam.farClipPlane = 4000f;
        go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt;
        try
        {
            for (int i = 0; i < 8; i++) cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
        }
        finally
        {
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.Destroy(rt);
            Object.Destroy(go);
        }
    }

    /// <summary>Runs one frame of the components that matter (the Editor doesn't tick in the background).</summary>
    static void Tick(GolfBall ball)
    {
        foreach (var c in ball.GetComponents<MonoBehaviour>())
            if (c.isActiveAndEnabled) Call(c, "LateUpdate");
        var cam = Camera.main ? Camera.main.GetComponent<HoleFlyCamera>() : null;
        if (cam) Call(cam, "Update");
    }

    static void Call(object target, string method) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(target, null);
}
