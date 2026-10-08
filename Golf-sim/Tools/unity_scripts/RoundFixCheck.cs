// Dev helper (Play mode), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/RoundFixCheck.cs --entry RoundFixCheck.Status
// Checks for the QA round 3 fixes to rounds, the server link, the menus, top holes and sound (R-1..R-9, TH-1..TH-4,
// S-1, S-2, M-3). Games are started and ended over REST by the caller (curl), with a fake phone on the WebSocket;
// these entries act on the sim and print PASS/FAIL lines. Arguments go in Temp/roundfix_arg.txt.
//   Setup / Restore   point the sim at a test server (arg: ws URL) and the trainer (arg 2), and back (always Restore
//                     before stopping Play: GolfServer.asset must not change)
//   Status            scene, phase, the "state" the phones get, the round, the download
//   Hit / HitHold     the current player hits (HitHold leaves the next turn pending, as between shots)
//   Expect            arg "key=value;key=value": checks Status fields (gameId, screen, phase, scene, canShoot, fetching)
//   R7Holed           plays the current player's shots until holed or done, then checks the putting fields
//   R3Restart         on the final scorecard: Restart Hole is disabled and Down skips it
//   R8Names           names in the HUD, scorecard and menu are plain text and ellipsised
//   Trainer / HttpOption   the trainer URL (arg), and HTTP allowed or refused (arg: AlwaysAllowed / NotAllowed)
//   TH1Setting / TH2Cancel / TH3Double + TH3Result / TH4Saved   top-hole download checks
//   Nav / Solo / Continue / PlayOut / Disconnect / Connect / Aim   remote keys (arg "Back,Up,Select"), rounds, link
//   S1Menu / S1Pause / S2Pause    sound settings from both menus, and the pause menu pausing sound
//   M3Description     the round card follows the 9/18 choice
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using GolfSim.Ball;
using GolfSim.Course;
using GolfSim.Game;
using GolfSim.Net;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public static class RoundFixCheck
{
    const float Frame = 0.02f;
    const string ArgFile = "Temp/roundfix_arg.txt";
    static RoundDirector D => RoundDirector.Instance;
    static readonly FieldInfo PhaseField = typeof(RoundDirector).GetField("phase", BindingFlags.NonPublic | BindingFlags.Instance);
    static string Phase => PhaseField.GetValue(D).ToString();
    static string[] Args => File.Exists(ArgFile) && File.ReadAllText(ArgFile).Trim().Length > 0 ? File.ReadAllText(ArgFile).Trim().Split('|') : new string[0];

    // ---- setup ----

    public static string Setup()
    {
        var c = ServerConfig.Load();
        var a = Args;
        if (!EditorPrefs.HasKey("RoundFixCheck.restore")) EditorPrefs.SetString("RoundFixCheck.restore", JsonUtility.ToJson(c)); // the original values only
        c.useLocalServer = true;
        c.localServerUrl = a.Length > 0 ? a[0] : "ws://localhost:18084";
        if (a.Length > 1) c.trainerUrl = a[1];
        if (a.Length > 2) c.pingInterval = float.Parse(a[2]);
        D.Connection.Disconnect();
        D.Connection.Connect();
        return $"server {c.ActiveUrl} trainer {c.trainerUrl} ping {c.pingInterval}";
    }

    public static string Restore()
    {
        var c = ServerConfig.Load();
        string saved = EditorPrefs.GetString("RoundFixCheck.restore", "");
        if (saved.Length == 0) return "nothing to restore";
        JsonUtility.FromJsonOverwrite(saved, c);
        EditorPrefs.DeleteKey("RoundFixCheck.restore");
        return $"restored local {c.useLocalServer} {c.localServerUrl} trainer {c.trainerUrl} ping {c.pingInterval}";
    }

    /// <summary>Changes the trainer URL only (arg), e.g. to an unreachable one.</summary>
    public static string Trainer()
    {
        ServerConfig.Load().trainerUrl = Args[0];
        return "trainer " + ServerConfig.Load().trainerUrl;
    }

    public static string Status()
    {
        if (!D) return "no RoundDirector (enter Play mode)";
        D.Connection.Pump();
        var r = D.Round;
        string round = r == null ? "none" : $"game {r.gameId} hole {r.HoleNumber}/{r.holeCount} cur {r.CurrentBall?.player ?? "-"} scores " +
            string.Join(" ", Enumerable.Range(0, r.players.Length).Select(p => $"{r.players[p]}=[{string.Join(",", r.scores[p])}]"));
        return $"scene={SceneManager.GetActiveScene().name};phase={Phase};fetching={D.IsFetching};conn={D.Connection.State};" +
               $"paused={HomeMenu.IsOpen};fade={ScreenFade.Loading};gameId={r?.gameId ?? 0}\n" +
               $"state {D.BuildState().ToJson()}\nround {round}";
    }

    /// <summary>arg "key=value;..." against Status's first line and the state JSON ("screen", "canShoot"...).</summary>
    public static string Expect()
    {
        string status = Status();
        var state = D.BuildState();
        var sb = new StringBuilder();
        bool ok = true;
        foreach (var pair in Args[0].Split(';').Where(p => p.Contains('=')))
        {
            var kv = pair.Split('=');
            string actual = kv[0] switch
            {
                "screen" => state.screen,
                "canShoot" => state.canShoot.ToString(),
                "putting" => state.putting.ToString(),
                _ => Field(status, kv[0]),
            };
            bool match = actual == kv[1];
            ok &= match;
            sb.Append($"{kv[0]}={actual}{(match ? "" : $" (want {kv[1]})")} ");
        }
        return $"{(ok ? "PASS" : "WAIT")} {sb}\n{status}";
    }

    static string Field(string status, string key)
    {
        foreach (var part in status.Split('\n')[0].Split(';'))
            if (part.StartsWith(key + "=")) return part.Substring(key.Length + 1);
        return "?";
    }

    // ---- shots ----

    public static string Hit() => HitShot(runPending: true);
    public static string HitHold() => HitShot(runPending: false);

    static string HitShot(bool runPending)
    {
        D.Connection.Pump();
        var ball = D.Ball;
        if (!ball) return "no ball";
        var hole = Object.FindAnyObjectByType<HoleInfo>();
        var club = Clubs.Find(D.Club);
        var shot = club.shot;
        if (club.IsPutter)
        {
            float d = Vector3.ProjectOnPlane(hole.PinWorld - ball.transform.position, Vector3.up).magnitude;
            shot = PuttModel.PuttAt(PuttModel.SpeedFor(d + PuttModel.Overshoot, PuttModel.GreenStimp(ball.Settings)));
        }
        var msg = new RemoteShotMessage
        {
            type = "shot", id = Random.Range(1, int.MaxValue), club = club.name, speed = shot.ballSpeed * (club.IsPutter ? 1f : 0.9f),
            launch = shot.launchAngle, azimuth = 0f, back = shot.backspin, side = 0f,
        };
        var ack = Shots.Submit(msg, "RoundFixCheck", out _);
        if (ack.status != "ok") return $"not hit: {ack.message}\n{Status()}";
        bool movingSaysNo = !D.BuildState().canShoot;
        for (int i = 0; i < 8000 && ball.InMotion; i++) ball.Advance(Frame);
        var after = D.BuildState();
        string r6 = $"{(movingSaysNo && !after.canShoot && after.waitReason.Length > 0 ? "PASS" : "FAIL")} R-6 canShoot false in flight and after the shot ('{after.waitReason}')";
        if (runPending) D.RunPending();
        D.Connection.Pump();
        return $"{club.name}: {ball.Status} {ball.Result.restingSurface}\n{r6}\n{Status()}";
    }

    /// <summary>R-7: shots until the current player is done; then state must not say putting.</summary>
    public static string R7Holed()
    {
        var r = D.Round;
        if (r == null || !D.Ball) return "no round";
        string player = r.CurrentBall.player;
        // A 2 m putt: the ball is put on the green short of the pin, toward the tee.
        var hole = Object.FindAnyObjectByType<HoleInfo>();
        var back = Vector3.ProjectOnPlane(hole.TeeWorld - hole.PinWorld, Vector3.up).normalized;
        D.Ball.PlaceOnGround(hole.PinWorld + back * 2f);
        D.SetClub("Putter");
        bool puttingBefore = D.BuildState().putting;
        for (int i = 0; i < 10 && !r.CurrentBall.Done; i++)
        {
            HitShot(runPending: false);
            if (r.CurrentBall.Done) break;
            D.RunPending();
        }
        var s = D.BuildState();
        bool ok = r.CurrentBall.holed && !s.putting && s.puttPlaysAs == 0 && s.puttDistance == 0;
        return $"{(ok ? "PASS" : "FAIL")} R-7 {player}: putting before {puttingBefore}; holed {r.CurrentBall.holed} ({r.CurrentBall.lie}): " +
               $"putting {s.putting} playsAs {s.puttPlaysAs}\n{Status()}";
    }

    /// <summary>Plays the whole round (every hole) to the final scorecard.</summary>
    public static string PlayOut()
    {
        for (int i = 0; i < 200 && Phase is "Playing" or "BetweenShots"; i++)
        {
            HitShot(runPending: false);
            D.RunPending();
        }
        return Status();
    }

    public static string Continue()
    {
        D.Continue();
        return Status();
    }

    /// <summary>Remote keys (arg: e.g. "Right,Right,Select").</summary>
    public static string Nav()
    {
        foreach (var key in Args[0].Split(',')) NavInput.Push((NavKey)System.Enum.Parse(typeof(NavKey), key));
        return Status();
    }

    public static string Disconnect() { D.Connection.Disconnect(); return "disconnected"; }
    public static string Connect() { D.Connection.Connect(); return "connecting"; }
    public static string Aim() { D.Aim(Time.frameCount % 2 == 0 ? 0.5f : -0.5f); return $"conn {D.Connection.State}"; }

    public static string Solo()
    {
        D.PlayRound(Args.Length > 0 ? int.Parse(Args[0]) : 1);
        return Status();
    }

    // ---- R-3: Restart Hole on the final scorecard ----

    public static string R3Restart()
    {
        if (Phase is not ("Finished" or "HoleSummary")) return "WAIT not on a scorecard\n" + Status();
        var home = Object.FindAnyObjectByType<HomeMenu>();
        NavInput.Push(NavKey.Back); // open the pause menu
        var restart = home.GetComponent<UIDocument>().rootVisualElement.Q<Button>("home-restart");
        NavInput.Push(NavKey.Down); // Resume -> (Restart skipped) -> Sound
        var root = home.GetComponent<UIDocument>().rootVisualElement;
        string selected = root.Query<Button>(className: "btn--selected").ToList().FirstOrDefault()?.name;
        NavInput.Push(NavKey.Back); // close
        bool ok = !restart.enabledSelf && selected == "home-settings";
        return $"{(ok ? "PASS" : "FAIL")} R-3 restart enabled {restart.enabledSelf}, Down selects {selected}\n{Status()}";
    }

    // ---- R-8 / R-9: names ----

    public static string R8Names()
    {
        var hud = D.GetComponentInChildren<UIDocument>().rootVisualElement;
        var badge = hud.Q<Label>("turn-badge-name");
        var names = hud.Query<Label>(className: "sc-cell--name").ToList();
        bool plain = !badge.enableRichText && names.All(l => !l.enableRichText) && hud.Q<Label>("hud-toast").enableRichText == false;
        var longest = names.OrderByDescending(l => l.text.Length).FirstOrDefault();
        bool ellipsis = longest == null || longest.resolvedStyle.textOverflow == TextOverflow.Ellipsis;
        bool fits = longest == null || longest.layout.width <= 200.5f;
        return $"{(plain ? "PASS" : "FAIL")} R-8 badge '{badge.text}' rich {badge.enableRichText}, {names.Count} name cells plain\n" +
               $"{(ellipsis && fits ? "PASS" : "FAIL")} R-9 longest name '{longest?.text}' width {longest?.layout.width:0} {longest?.resolvedStyle.textOverflow}";
    }

    // ---- top holes ----

    /// <summary>TH-1: a URL that throws on send must still end the fetch (fallback) and start the round.</summary>
    public static string TH1Setting() =>
        $"{(PlayerSettings.insecureHttpOption == InsecureHttpOption.AlwaysAllowed ? "PASS" : "FAIL")} TH-1 Allow downloads over HTTP: {PlayerSettings.insecureHttpOption}";

    /// <summary>TH-1 with HTTP refused again (arg: NotAllowed or AlwaysAllowed): SendWebRequest then throws, as in the bug.</summary>
    public static string HttpOption()
    {
        PlayerSettings.insecureHttpOption = (InsecureHttpOption)System.Enum.Parse(typeof(InsecureHttpOption), Args[0]);
        return "insecure HTTP " + PlayerSettings.insecureHttpOption;
    }

    /// <summary>TH-2: while fetching, the overlay is up, Select is swallowed and Back cancels.</summary>
    public static string TH2Cancel()
    {
        if (!D.IsFetching) return "WAIT not fetching\n" + Status();
        var hud = D.GetComponentInChildren<UIDocument>().rootVisualElement;
        bool overlay = hud.Q("loading").ClassListContains("loading--open");
        string detail = hud.Q<Label>("loading-detail").text;
        D.PlayRound(); // a second Play (TH-3) is ignored
        bool swallowed = NavInput.Push(NavKey.Select) && D.IsFetching && !ScreenFade.Loading;
        NavInput.Push(NavKey.Back);
        bool cancelled = !D.IsFetching && D.Round == null && !hud.Q("loading").ClassListContains("loading--open");
        return $"{(overlay && detail.Length > 0 ? "PASS" : "FAIL")} TH-2 overlay '{detail}'\n" +
               $"{(swallowed ? "PASS" : "FAIL")} TH-2/3 Select and Play ignored while fetching\n" +
               $"{(cancelled ? "PASS" : "FAIL")} TH-2 Back cancelled: round {D.Round?.gameId.ToString() ?? "none"}\n{Status()}";
    }

    /// <summary>TH-3: two fetches at once on a cold hole: one waits for the other, the package is whole, no partials left.</summary>
    public static string TH3Double()
    {
        for (int i = 0; i < 2; i++)
        {
            int n = i;
            D.StartCoroutine(TrainerHoles.Fetch(ServerConfig.Load(), 9, null, null, null, () => false, r =>
                File.WriteAllText($"Temp/roundfix_th3_{n}.txt", r.holes != null ? $"{r.holes.Count} holes{(r.fromCache ? " (cache)" : "")}" : r.error)));
        }
        return "started 2 fetches; then run TH3Result";
    }

    public static string TH3Result()
    {
        string a = File.Exists("Temp/roundfix_th3_0.txt") ? File.ReadAllText("Temp/roundfix_th3_0.txt") : null;
        string b = File.Exists("Temp/roundfix_th3_1.txt") ? File.ReadAllText("Temp/roundfix_th3_1.txt") : null;
        if (a == null || b == null) return $"WAIT {a} / {b}";
        var partials = Directory.GetDirectories(TrainerHoles.CacheFolder, "*.partial*");
        bool ok = a.StartsWith("9 holes") && b.StartsWith("9 holes") && partials.Length == 0;
        File.Delete("Temp/roundfix_th3_0.txt");
        File.Delete("Temp/roundfix_th3_1.txt");
        return $"{(ok ? "PASS" : "FAIL")} TH-3 fetch 1: {a}; fetch 2: {b}; partial folders left {partials.Length}";
    }

    /// <summary>TH-4: the saved hole list for the round's game exists.</summary>
    public static string TH4Saved()
    {
        var files = Directory.Exists(Path.Combine(TrainerHoles.CacheFolder, "rounds"))
            ? Directory.GetFiles(Path.Combine(TrainerHoles.CacheFolder, "rounds")).Select(Path.GetFileName).ToArray() : new string[0];
        string game = D.Round != null ? $"-game-{D.Round.gameId}.json" : "?";
        bool ok = files.Any(f => f.EndsWith(game));
        return $"{(ok ? "PASS" : "FAIL")} TH-4 saved lists: {string.Join(", ", files)}";
    }

    // ---- sound and menus ----

    /// <summary>S-1 (main menu): the Sound card opens the panel; Right raises the master volume; Back closes.</summary>
    public static string S1Menu()
    {
        var menu = MainMenuReady();
        if (!menu) return "WAIT not on the main menu, or it is loading";
        if (!menu.modes.Any(m => m.kind == GameMode.ModeKind.Settings)) return "FAIL S-1 no Settings card";
        return SoundPanel(menu.GetComponent<UIDocument>().rootVisualElement, () =>
        {
            SelectCard(menu, GameMode.ModeKind.Settings);
            NavInput.Push(NavKey.Select);
        }, "S-1 main menu");
    }

    /// <summary>The main menu with nothing over it: an open overlay (Scores, Sound) takes every key, so it is closed with Back first, as from the remote.</summary>
    static MainMenu MainMenuReady()
    {
        var menu = Object.FindAnyObjectByType<MainMenu>();
        if (!menu || ScreenFade.Loading || D && D.IsFetching) return null;
        var scores = (ScoresScreen)typeof(MainMenu).GetField("scores", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(menu);
        for (int i = 0; i < 3 && (scores.IsOpen || SettingsScreen.AnyOpen); i++) NavInput.Push(NavKey.Back);
        return menu;
    }

    /// <summary>Right until the card of this kind is selected (the title shows the selected card's).</summary>
    static void SelectCard(MainMenu menu, GameMode.ModeKind kind)
    {
        var title = menu.GetComponent<UIDocument>().rootVisualElement.Q<Label>("title");
        for (int i = 0; i < menu.modes.Length && menu.modes.FirstOrDefault(m => m.title == title.text)?.kind != kind; i++)
            NavInput.Push(NavKey.Right);
    }

    /// <summary>S-1 (pause menu): Sound in the pause menu opens the same panel.</summary>
    public static string S1Pause()
    {
        var home = Object.FindAnyObjectByType<HomeMenu>();
        if (!home || !D.Ball) return "WAIT not in a hole";
        var root = home.GetComponent<UIDocument>().rootVisualElement;
        string result = SoundPanel(root, () =>
        {
            NavInput.Push(NavKey.Back); // pause
            for (int i = 0; i < 4 && root.Query<Button>(className: "btn--selected").ToList().FirstOrDefault()?.name != "home-settings"; i++)
                NavInput.Push(NavKey.Down);
            NavInput.Push(NavKey.Select);
        }, "S-1 pause menu");
        NavInput.Push(NavKey.Back); // resume
        return result + $"\npaused {HomeMenu.IsOpen}";
    }

    static string SoundPanel(VisualElement root, System.Action open, string label)
    {
        float before = GameAudio.MasterVolume;
        open();
        var overlay = root.Q(className: "settings");
        bool shown = overlay != null && overlay.ClassListContains("settings--open");
        NavInput.Push(before >= 0.95f ? NavKey.Left : NavKey.Right);
        float changed = GameAudio.MasterVolume;
        NavInput.Push(NavKey.Back);
        bool closed = overlay != null && !overlay.ClassListContains("settings--open");
        GameAudio.MasterVolume = before;
        bool ok = shown && Mathf.Abs(changed - before) > 0.05f && closed;
        return $"{(ok ? "PASS" : "FAIL")} {label}: shown {shown}, master {before:0.00} -> {changed:0.00}, closed {closed}";
    }

    /// <summary>S-2: the pause menu pauses the game's sound (UI voices keep playing).</summary>
    public static string S2Pause()
    {
        if (!Object.FindAnyObjectByType<HomeMenu>()) return "WAIT not in a hole";
        NavInput.Push(NavKey.Back);
        bool paused = AudioListener.pause && HomeMenu.IsOpen;
        NavInput.Push(NavKey.Back);
        bool resumed = !AudioListener.pause && !HomeMenu.IsOpen;
        return $"{(paused && resumed ? "PASS" : "FAIL")} S-2 listener paused {paused}, resumed {resumed}";
    }

    /// <summary>M-3: the Play a Round card opens "Choose a course" (unless it resumes the server's game); Back comes back.</summary>
    public static string M3Description()
    {
        var menu = MainMenuReady();
        if (!menu) return "WAIT not on the main menu, or it is loading";
        if (RoundDirector.CanResume) return "PASS M-3 (resume shown: Play resumes the server's game)";
        SelectCard(menu, GameMode.ModeKind.Round);
        NavInput.Push(NavKey.Select);
        string opened = menu.PageName;
        NavInput.Push(NavKey.Back);
        bool ok = opened == "courses" && menu.PageName == "home";
        return $"{(ok ? "PASS" : "FAIL")} M-3 Play opened '{opened}', Back -> '{menu.PageName}'";
    }
}
