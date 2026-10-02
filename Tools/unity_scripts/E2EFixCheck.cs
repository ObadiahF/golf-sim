// Dev helper (Play mode), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/E2EFixCheck.cs --entry E2EFixCheck.Status
// Checks for the end-to-end QA round 4 fixes (E-1..E-5, E-11..E-15; Docs/bug-hunt.md). Games are started over REST
// by the caller (curl), with a fake phone on the WebSocket; these entries act on the sim and print PASS/FAIL lines.
// Arguments go in Temp/e2efix_arg.txt ('|' separated).
//   Setup / Restore   point the sim at a test server (arg: ws URL; arg 2 "builtin" plays the built-in holes), and back
//                     (always Restore before stopping Play: GolfServer.asset must not change)
//   Status / Expect   the scene, phase and "state"; Expect arg "key=value;..." (screen, phase, scene, canShoot)
//   Hit / HitHold     the current player hits (HitHold leaves the next turn pending, as between shots)
//   FinishHole        holes everyone out: each ball 2 m from the pin, putts read with the putting preview (real scores)
//   E1Banner          arg "turn|Name" or "win|Name": announces it (then capture the Game view)
//   E1Fit             checks the banner on screen now (after the layout pass): its title box holds the text
//   E2UpSelect        on a hole scorecard: Up then Select in one call keeps the scorecard, then a full replay
//   E2LeadIn          between shots: Select during an automatic replay's lead-in skips it
//   E2Lost            a replay dropped mid-play (a scene load) still gives the HUD back
//   E3Badge           between shots: the badge is back after an instant replay
//   E4PickUp          the current player picks up: the HUD shows the score, not "Stroke 10"
//   E5Prompt          on a scorecard: "Replay" is offered, then gone as soon as the next hole loads
//   E11Putting        putting mode on the green and short grass only; the read matches the real ball
//   E12Pause / E13Sound / E14Volume   the pause menu's subtitle, the Sound panel over it, volume steps
//   E15Scores         (main menu) Scores opens on the first tab that ranks somebody
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

public static class E2EFixCheck
{
    const float Frame = 0.02f;
    const string ArgFile = "Temp/e2efix_arg.txt", RestoreKey = "E2EFixCheck.restore";
    const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    static RoundDirector D => RoundDirector.Instance;
    static ReplayDirector R => ReplayDirector.Instance;
    static string[] Args => File.Exists(ArgFile) && File.ReadAllText(ArgFile).Trim().Length > 0 ? File.ReadAllText(ArgFile).Trim().Split('|') : new string[0];
    static T Get<T>(object o, string field) => (T)o.GetType().GetField(field, Any).GetValue(o);
    static string Phase => Get<object>(D, "phase").ToString();
    static RoundHud Hud => Get<RoundHud>(D, "hud");
    static VisualElement HudRoot => D.GetComponentInChildren<UIDocument>().rootVisualElement;
    static string Stage => Get<object>(R, "stage").ToString();
    static string Line(bool ok, string id, string text) => $"{(ok ? "PASS" : "FAIL")} {id} {text}";

    // ---- setup ----

    public static string Setup()
    {
        var c = ServerConfig.Load();
        var a = Args;
        if (!EditorPrefs.HasKey(RestoreKey)) EditorPrefs.SetString(RestoreKey, JsonUtility.ToJson(c)); // the original values only
        c.useLocalServer = true;
        c.localServerUrl = a.Length > 0 ? a[0] : "ws://localhost:18091";
        if (a.Length > 1 && a[1] == "builtin") c.useTopHoles = false;
        D.Connection.Disconnect();
        D.Connection.Connect();
        return $"server {c.ActiveUrl} top holes {c.useTopHoles}";
    }

    public static string Restore()
    {
        var c = ServerConfig.Load();
        string saved = EditorPrefs.GetString(RestoreKey, "");
        if (saved.Length == 0) return "nothing to restore";
        JsonUtility.FromJsonOverwrite(saved, c);
        EditorPrefs.DeleteKey(RestoreKey);
        return $"restored local {c.useLocalServer} {c.localServerUrl} top holes {c.useTopHoles}";
    }

    public static string Status()
    {
        if (!D) return "no RoundDirector (enter Play mode)";
        D.Connection.Pump();
        var r = D.Round;
        string round = r == null ? "none" : $"game {r.gameId} hole {r.HoleNumber}/{r.holeCount} cur {r.CurrentBall?.player ?? "-"} strokes {r.CurrentBall?.strokes}";
        return $"scene={SceneManager.GetActiveScene().name};phase={Phase};conn={D.Connection.State};replay={Stage}\n" +
               $"state {D.BuildState().ToJson()}\nround {round}";
    }

    public static string Expect()
    {
        string status = Status();
        var state = D.BuildState();
        var sb = new StringBuilder();
        bool ok = true;
        foreach (var kv in Args[0].Split(';').Where(p => p.Contains('=')).Select(p => p.Split('=')))
        {
            string actual = kv[0] switch
            {
                "screen" => state.screen,
                "canShoot" => state.canShoot.ToString(),
                _ => status.Split('\n')[0].Split(';').FirstOrDefault(f => f.StartsWith(kv[0] + "="))?.Substring(kv[0].Length + 1) ?? "?",
            };
            ok &= actual == kv[1];
            sb.Append($"{kv[0]}={actual}{(actual == kv[1] ? "" : $" (want {kv[1]})")} ");
        }
        return $"{(ok ? "PASS" : "WAIT")} {sb}\n{status}";
    }

    // ---- shots ----

    public static string Hit() => HitShot(runPending: true);
    public static string HitHold() => HitShot(runPending: false);

    /// <summary>The current player hits the club's stock shot, or a putt at putterSpeed (m/s; 0: the flat-green strength to the pin).</summary>
    static string HitShot(bool runPending, float putterSpeed = 0f)
    {
        D.Connection.Pump();
        var ball = D.Ball;
        if (!ball) return "no ball";
        var hole = Object.FindAnyObjectByType<HoleInfo>();
        var club = Clubs.Find(D.Club);
        var shot = club.shot;
        if (club.IsPutter)
            shot = PuttModel.PuttAt(putterSpeed > 0f ? putterSpeed
                : PuttModel.SpeedFor(Round.FlatDistance(ball.transform.position, hole.PinWorld) + PuttModel.Overshoot, PuttModel.GreenStimp(ball.Settings)));
        var msg = new RemoteShotMessage
        {
            type = "shot", id = Random.Range(1, int.MaxValue), club = club.name, speed = shot.ballSpeed * (club.IsPutter ? 1f : 0.9f),
            launch = shot.launchAngle, azimuth = 0f, back = shot.backspin, side = 0f,
        };
        var ack = Shots.Submit(msg, "E2EFixCheck", out _);
        if (ack.status != "ok") return $"not hit: {ack.message}\n{Status()}";
        for (int i = 0; i < 8000 && ball.InMotion; i++) ball.Advance(Frame);
        if (runPending) D.RunPending();
        D.Connection.Pump();
        return $"{club.name}: {ball.Status} {ball.Result.restingSurface}\n{Status()}";
    }

    /// <summary>
    /// Holes everyone out so the hole's scorecard shows: each player's ball goes on the green 2 m from the pin, then
    /// they putt it in, each putt read like a player would (ReadPutt). The scores (strokes so far + putts) are real.
    /// </summary>
    public static string FinishHole()
    {
        var r = D.Round;
        var hole = Object.FindAnyObjectByType<HoleInfo>();
        var back = Vector3.ProjectOnPlane(hole.TeeWorld - hole.PinWorld, Vector3.up).normalized;
        var putts = new StringBuilder();
        string placed = null;
        for (int guard = 0; guard < 40 && Phase is "Playing" or "BetweenShots"; guard++)
        {
            if (Phase == "BetweenShots") { D.RunPending(); continue; }
            var player = r.CurrentBall;
            if (placed != player.player) Place(hole.PinWorld + back * 2f, "green");
            placed = player.player;
            D.SetClub("Putter");
            float from = Round.FlatDistance(D.Ball.transform.position, hole.PinWorld);
            float speed = ReadPutt(D.Ball, hole, out float aim);
            HitShot(runPending: false, speed);
            putts.Append($"{player.player} {from:0.0} m aim {aim:+0.0;-0.0}° -> {(player.holed ? "holed" : $"{Round.FlatDistance(D.Ball.transform.position, hole.PinWorld):0.00} m")}; ");
        }
        return $"putts: {putts}\n{Status()}";
    }

    /// <summary>
    /// The read: the aim (degrees off the line to the pin, set on the ball) that the putting preview says holes the
    /// putt at its solved strength, else the one that finishes closest; returns that strength (m/s).
    /// </summary>
    static float ReadPutt(GolfBall ball, HoleInfo hole, out float aim)
    {
        var preview = ball.GetComponent<PuttPreview>();
        float bestGap = float.MaxValue, speed = 0f;
        aim = 0f;
        for (int k = 0; k <= 48 && bestGap > 0f; k++)
        {
            ball.aimOffset = (k + 1) / 2 * 0.5f * (k % 2 == 1 ? -1f : 1f); // 0, -0.5, +0.5, -1 ... ±12°
            preview.Refresh();
            float gap = preview.Prediction.holed ? 0f : Round.FlatDistance(preview.Prediction.end, hole.PinWorld);
            if (gap >= bestGap) continue;
            bestGap = gap;
            aim = ball.aimOffset;
            speed = preview.SolvedSpeed;
        }
        ball.aimOffset = aim;
        return speed;
    }

    static void TickReplay(float seconds)
    {
        for (float t = 0f; t < seconds; t += Frame) R.Tick(Frame);
        D.Connection.Pump();
    }

    /// <summary>Ticks through a replay's lead-in (a shot into trouble queues one by itself) until it plays, then 0.4 s in.</summary>
    static void TickIntoReplay()
    {
        for (int i = 0; i < 300 && R.Busy && !R.IsPlaying; i++) TickReplay(Frame);
        TickReplay(0.4f);
    }

    static bool HudShown => !HudRoot.Q("hud-root").ClassListContains("hud--hidden"); // RoundHud.Render hides it during a replay

    // ---- E-1 banners ----

    public static string E1Banner()
    {
        var a = Args;
        string name = a.Length > 1 ? a[1] : "White Hayden";
        var banner = Hud.Banner;
        if (a[0] == "win") banner.Celebrate($"{name.ToUpperInvariant()} WINS!", $"{name} wins with 68 (+34)", TurnBanner.AccentFor(2), null);
        else banner.AnnounceTurn(name, 2, RoundHud.TurnInfo(1, 4, 0));
        return "announced; capture it, then run E1Fit (the layout updates on the next frame)";
    }

    /// <summary>The title's box holds the text with its letter-spacing and stays on screen.</summary>
    public static string E1Fit()
    {
        var title = HudRoot.Q<Label>("turn-banner-title");
        var card = HudRoot.Q("turn-banner-card");
        if (!HudRoot.Q("turn-banner").ClassListContains("turn-banner--center")) return "WAIT no banner up (a turn banner shows for 1.5 s: run E1Fit right after E1Banner)";
        float glyphs = title.MeasureTextSize(title.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x;
        float need = glyphs + title.resolvedStyle.letterSpacing * title.text.Length;
        bool wraps = title.resolvedStyle.whiteSpace == WhiteSpace.Normal;
        float screen = HudRoot.layout.width;
        bool fits = wraps || title.layout.width + 0.5f >= need;
        bool onScreen = card.worldBound.xMin >= -1f && card.worldBound.xMax <= screen + 1f;
        bool noEllipsis = title.resolvedStyle.textOverflow != TextOverflow.Ellipsis;
        return Line(fits && onScreen && noEllipsis, "E-1", $"'{title.text}' box {title.layout.width:0} for text {glyphs:0} + spacing = {need:0}, " +
                    $"font {title.resolvedStyle.fontSize:0}, wrap {wraps}, card {card.worldBound.xMin:0}..{card.worldBound.xMax:0} of {screen:0}");
    }

    // ---- E-2 / E-3 / E-5 replays and the HUD ----

    public static string E2UpSelect()
    {
        if (Phase != "HoleSummary") return "WAIT not on a hole scorecard\n" + Status();
        bool offered = R.CanReplay;
        NavInput.Push(NavKey.Up);
        bool selectTaken = NavInput.Push(NavKey.Select);
        string stage = Stage, phase = Phase;
        bool kept = phase == "HoleSummary" && !ScreenFade.Loading && stage == "Idle" && Hud.ScorecardOpen;
        // A real replay from the scorecard: the HUD hides while it plays and comes back after it.
        NavInput.Push(NavKey.Up);
        TickIntoReplay();
        bool hidden = R.IsPlaying && D.BuildState().screen == StateMessage.Replay && !HudShown;
        TickReplay(30f);
        bool back = Stage == "Idle" && D.BuildState().screen == StateMessage.HoleComplete && HudShown && Hud.ScorecardOpen;
        return $"{Line(offered && selectTaken && kept, "E-2", $"Up+Select on the scorecard: Select taken {selectTaken}, replay {stage}, phase {phase}, fading {ScreenFade.Loading}")}\n" +
               $"{Line(hidden && back, "E-2", $"replay from the scorecard: HUD hidden while playing {hidden}, back after {back}")}\n{Status()}";
    }

    public static string E2LeadIn()
    {
        if (Phase == "Playing") HitShot(runPending: false); // the next turn waits 2.5 s: hit in this same call
        if (Phase != "BetweenShots") return "WAIT not between shots\n" + Status();
        R.Queue(R.Recording ?? Get<ShotRecorder>(R, "recorder").Last, R.settings.leadIn); // as an automatic replay
        bool waiting = Stage == "Waiting";
        bool taken = NavInput.Push(NavKey.Select);
        bool skipped = Stage == "Idle" && Phase == "BetweenShots" && !HomeMenu.IsOpen;
        return Line(waiting && taken && skipped, "E-2", $"Select in an automatic replay's lead-in: waiting {waiting}, taken {taken}, skipped {skipped}") + "\n" + Status();
    }

    public static string E2Lost()
    {
        if (Phase == "Playing") HitShot(runPending: false);
        var recorder = Get<ShotRecorder>(R, "recorder");
        if (recorder.Last == null || !recorder.LastIsCurrent) return "WAIT need a current shot (HitHold)\n" + Status();
        if (!R.Busy) R.Queue(recorder.Last); // a shot into trouble has queued its own replay, after a lead-in
        TickIntoReplay();
        bool playing = R.IsPlaying && !HudShown;
        R.Forget(); // what a scene load does to a replay on screen
        bool back = Stage == "Idle" && HudShown && D.BuildState().screen != StateMessage.Replay && !R.CanReplay;
        return Line(playing && back, "E-2", $"replay dropped mid-play: was playing with the HUD hidden {playing}; HUD back {back} " +
                    $"(replay {Stage}, HUD shown {HudShown}, screen {D.BuildState().screen}, replay offered {R.CanReplay})") + "\n" + Status();
    }

    public static string E3Badge()
    {
        var banner = Hud.Banner;
        var badge = HudRoot.Q("turn-badge");
        if (Phase == "Playing") HitShot(runPending: false);
        if (Phase != "BetweenShots") return "WAIT not between shots\n" + Status();
        if (!Get<bool>(banner, "badgeLanded")) return "WAIT the announcement hasn't landed in the badge yet\n" + Status();
        bool before = badge.ClassListContains("turn-badge--shown");
        NavInput.Push(NavKey.Up);
        TickIntoReplay();
        bool during = R.IsPlaying && !badge.ClassListContains("turn-badge--shown");
        TickReplay(30f);
        bool after = Stage == "Idle" && badge.ClassListContains("turn-badge--shown") && HudShown;
        return Line(before && during && after, "E-3", $"badge before {before}, hidden in the replay {during}, back after {after} ('{HudRoot.Q<Label>("turn-badge-detail").text}')") + "\n" + Status();
    }

    public static string E5Prompt()
    {
        if (Phase != "HoleSummary") return "WAIT not on a hole scorecard\n" + Status();
        bool offered = R.CanReplay;
        D.Continue(); // Select: the next hole loads
        var recorder = Get<ShotRecorder>(R, "recorder");
        bool gone = !R.CanReplay && recorder.Last == null && Stage == "Idle";
        return Line(offered && gone, "E-5", $"offered on the scorecard {offered}; after Select: CanReplay {R.CanReplay}, last shot {(recorder.Last == null ? "dropped" : "kept")}") + "\n" + Status();
    }

    // ---- E-4 pick-up ----

    public static string E4PickUp()
    {
        var r = D.Round;
        if (Phase == "BetweenShots") D.RunPending();
        if (Phase != "Playing" || r == null) return "WAIT not playing\n" + Status();
        string player = r.CurrentBall.player;
        D.PickUp();
        var caption = HudRoot.Q<Label>("hud-strokes-caption").text;
        var value = HudRoot.Q<Label>("hud-strokes").text;
        var badge = HudRoot.Q<Label>("turn-badge-detail").text;
        bool ok = caption == "SCORE" && value == r.StrokeCap.ToString() && badge.Contains($"Picked up: {r.StrokeCap}");
        return Line(ok, "E-4", $"{player} picked up: HUD {caption} {value}, badge '{badge}'") + "\n" + Status();
    }

    // ---- E-11 putting off the green ----

    public static string E11Putting()
    {
        var r = D.Round;
        var ball = D.Ball;
        var preview = ball ? ball.GetComponent<PuttPreview>() : null;
        if (r == null || Phase != "Playing" || !preview || preview.Map == null) return "WAIT not playing a hole\n" + Status();
        var hole = Object.FindAnyObjectByType<HoleInfo>();
        var map = preview.Map;
        D.SetClub("Putter");
        var sb = new StringBuilder();
        foreach (var surface in new[] { "green", "fairway", "rough" })
        {
            var spot = Spot(map, hole, preview, surface);
            if (spot == null)
            {
                sb.AppendLine($"INFO E-11 no {surface} spot within 3 m of the green on this hole");
                continue;
            }
            Place(spot.Value, surface);
            var s = D.BuildState();
            bool wantPutting = surface != "rough";
            sb.AppendLine(Line(s.putting == wantPutting && (s.puttPlaysAs > 0) == wantPutting, "E-11",
                $"{surface} {Round.FlatDistance(spot.Value, hole.PinWorld):0.0} m from the pin: putting {s.putting}, playsAs {s.puttPlaysAs}"));
            // The read against the real ball: the putt at the meter's target (playsAs) should finish ~40 cm past.
            float speed = wantPutting ? PuttModel.SpeedFor((float)s.puttPlaysAs, PuttModel.GreenStimp(ball.Settings)) : PuttModel.SpeedFor(6.4f, PuttModel.GreenStimp(ball.Settings));
            var from = ball.transform.position;
            var predicted = PuttPredictor.Simulate(map, ball.Settings, from, ball.AimDirection, PuttModel.PuttAt(speed), hole.PinWorld, step: GolfBall.Step);
            ball.Hit(PuttModel.PuttAt(speed), 1);
            for (int i = 0; i < 8000 && ball.InMotion; i++) ball.Advance(Frame);
            var end = ball.Status == BallStatus.Holed ? hole.PinWorld : ball.transform.position;
            float gap = Round.FlatDistance(end, predicted.end), rolled = Round.FlatDistance(from, end);
            float toPin = Round.FlatDistance(from, hole.PinWorld);
            D.Mulligan(); // takes the test putt back (score and ball)
            bool closeToTarget = !wantPutting || ball.Status == BallStatus.Holed || Mathf.Abs(rolled - (toPin + PuttModel.Overshoot)) < 0.6f;
            sb.AppendLine(Line(gap < 0.3f && closeToTarget, "E-11",
                $"{surface}: {(wantPutting ? $"meter at the read {s.puttPlaysAs:0.0} m" : "meter at 6.4 m")} -> rolled {rolled:0.00} m of {toPin:0.0} ({ball.Status}), " +
                $"preview said {Round.FlatDistance(from, predicted.end):0.00} m (off by {gap:0.00} m)"));
        }
        return sb + Status();
    }

    static void Place(Vector3 at, string lie)
    {
        D.Ball.PlaceOnGround(at);
        D.Ball.aimOffset = 0f;
        D.Round.CurrentBall.position = D.Ball.transform.position;
        D.Round.CurrentBall.lie = lie;
        D.SetClub("Putter");
    }

    /// <summary>A spot of this surface 4-20 m from the pin with the green within 3 m toward it (or on the green).</summary>
    static Vector3? Spot(TerrainSurfaceMap map, HoleInfo hole, PuttPreview preview, string surface)
    {
        var pin = hole.PinWorld;
        for (float d = 4f; d <= 30f; d += 0.5f)
            for (int k = 0; k < 48; k++)
            {
                float a = k * Mathf.PI * 2f / 48f;
                var p = pin + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d;
                if (!map.Contains(p) || map.SurfaceAt(p) != surface) continue;
                p.y = map.HeightAt(p);
                if (surface == "green") return p;
                var toPin = Vector3.ProjectOnPlane(pin - p, Vector3.up).normalized;
                bool near = false, clear = true;
                for (float s = 0.25f; s <= 3f && !near; s += 0.25f)
                {
                    string at = map.SurfaceAt(p + toPin * s);
                    near = at == "green";
                    clear &= at == surface || at == "green";
                }
                if (near && clear) return p;
            }
        return null;
    }

    // ---- E-12 .. E-14 pause menu and Sound ----

    static VisualElement PauseRoot => Object.FindAnyObjectByType<HomeMenu>()?.GetComponent<UIDocument>().rootVisualElement;

    public static string E12Pause()
    {
        if (PauseRoot == null) return "WAIT not in a hole";
        if (!HomeMenu.IsOpen) NavInput.Push(NavKey.Back);
        return "paused; run E12Check after a frame";
    }

    public static string E12Check()
    {
        var panel = PauseRoot.Q("home").Q(className: "home__panel");
        var subtitle = panel.Q<Label>(className: "home__subtitle");
        bool inside = subtitle.worldBound.xMax <= panel.worldBound.xMax - 30f && subtitle.layout.width > 0f;
        return Line(inside, "E-12", $"subtitle '{subtitle.text}' ends at {subtitle.worldBound.xMax:0}, card content ends at {panel.worldBound.xMax - 40f:0} " +
                    $"(text {subtitle.MeasureTextSize(subtitle.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x:0} wide, wraps {subtitle.resolvedStyle.whiteSpace})");
    }

    public static string E13Sound()
    {
        var root = PauseRoot;
        if (root == null) return "WAIT not in a hole";
        if (!HomeMenu.IsOpen) NavInput.Push(NavKey.Back);
        string paused = D.BuildState().screen;
        for (int i = 0; i < 4 && root.Query<Button>(className: "btn--selected").ToList().FirstOrDefault()?.name != "home-sound"; i++) NavInput.Push(NavKey.Down);
        NavInput.Push(NavKey.Select);
        var s = D.BuildState();
        var card = root.Q(className: "sound__panel");
        bool opaque = card.resolvedStyle.backgroundColor.a >= 0.999f;
        bool hints = HudRoot.Q("hud-hint").ClassListContains("hud--hidden") && HudRoot.Q("hud-info").ClassListContains("hud--hidden");
        string sent = Get<string>(D, "lastState");
        NavInput.Push(NavKey.Back); // closes Sound
        string afterClose = D.BuildState().screen;
        NavInput.Push(NavKey.Back); // resumes
        string afterResume = D.BuildState().screen;
        bool hintBack = !HudRoot.Q("hud-hint").ClassListContains("hud--hidden");
        bool ok = paused == "paused" && s.screen == "settings" && sent.Contains("\"screen\":\"settings\"") && opaque && hints &&
                  afterClose == "paused" && afterResume == "game" && hintBack;
        return Line(ok, "E-13", $"pause {paused} -> Sound {s.screen} (sent {sent.Contains("\"settings\"")}), card alpha {card.resolvedStyle.backgroundColor.a:0.00}, " +
                    $"HUD hints hidden {hints}; Back -> {afterClose}, Back -> {afterResume}, hint back {hintBack}");
    }

    public static string E14Volume()
    {
        var root = PauseRoot;
        if (root == null) return "WAIT not in a hole";
        float crowd = GameAudio.GetVolume(SoundBus.Crowd);
        GameAudio.SetVolume(SoundBus.Crowd, 0.75f);
        if (!HomeMenu.IsOpen) NavInput.Push(NavKey.Back);
        for (int i = 0; i < 4 && root.Query<Button>(className: "btn--selected").ToList().FirstOrDefault()?.name != "home-sound"; i++) NavInput.Push(NavKey.Down);
        NavInput.Push(NavKey.Select);
        NavInput.Push(NavKey.Down);
        NavInput.Push(NavKey.Down); // Crowd
        var seen = new System.Collections.Generic.List<int> { Pct() };
        foreach (var key in new[] { NavKey.Left, NavKey.Left, NavKey.Right, NavKey.Right, NavKey.Right })
        {
            NavInput.Push(key);
            seen.Add(Pct());
        }
        NavInput.Push(NavKey.Back);
        NavInput.Push(NavKey.Back);
        GameAudio.SetVolume(SoundBus.Crowd, crowd);
        bool ok = seen.SequenceEqual(new[] { 75, 70, 60, 70, 80, 90 });
        return Line(ok, "E-14", $"Crowd {string.Join(" -> ", seen)} % (restored to {crowd:0.00})");
        int Pct() => Mathf.RoundToInt(GameAudio.GetVolume(SoundBus.Crowd) * 100f);
    }

    // ---- E-15 Scores ----

    public static string E15Scores()
    {
        var menu = Object.FindAnyObjectByType<MainMenu>();
        if (!menu) return "WAIT not on the main menu";
        var scores = Get<ScoresScreen>(menu, "scores");
        var root = menu.GetComponent<UIDocument>().rootVisualElement;
        if (!scores.IsOpen)
        {
            scores.Show();
            return "opened; run E15Scores again once the stats have loaded";
        }
        var players = Get<PlayerStats[]>(scores, "players");
        string tab = root.Q("scores-tabs").Query<Button>(className: "tab--selected").ToList().FirstOrDefault()?.text;
        string status = root.Q<Label>("scores-status").text;
        bool anyHandicap = players != null && players.Any(p => GameApi.Has(p.handicap));
        bool ok = players != null && (anyHandicap ? tab == "Handicap" : tab != "Handicap") && !status.StartsWith("Nobody");
        scores.Close();
        return Line(ok, "E-15", $"{players?.Length} players, any handicap {anyHandicap}: opened on '{tab}' ('{status}')");
    }
}
