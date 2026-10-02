// Dev helper (Play mode, a hole scene), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/ReplayCheck.cs --entry ReplayCheck.Drive
// Hits a shot, runs it to rest with GolfBall.Advance, then plays its instant replay with ReplayDirector.Tick (the
// Editor doesn't tick Play mode in the background) and renders frames from each camera shot to Out.
//   Drive / Approach / Tree / Putt / Chip / Water   one scenario each (Tree and Water search for a shot that does it)
//   Woods                                          a 5 iron sprayed 35° into the trees (dense woods on forest holes)
//   All                                            every scenario
//   Flow                                           in a round: shot, auto replay holding the turn, skip, next turn
using System.IO;
using System.Linq;
using System.Text;
using GolfSim.Ball;
using GolfSim.Course;
using GolfSim.Game;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class ReplayCheck
{
    const string Out = "/Users/obadiah/.claude/jobs/5fd17b43/tmp/replay/";
    const float Frame = 1f / 30f;
    const float GreenDecel = 0.55f; // m/s² a putt slows by on a Stimp-11 green (BallPlayTest.Putt)

    static GolfBall Ball => RoundDirector.Instance && RoundDirector.Instance.Ball ? RoundDirector.Instance.Ball : Object.FindAnyObjectByType<GolfBall>();
    static HoleInfo Hole => Object.FindAnyObjectByType<HoleInfo>();
    static ShotRecorder Recorder => RoundDirector.Instance.GetComponent<ShotRecorder>();

    public static string All() => string.Join("\n", Drive(), Approach(), Chip(), Tree(), Putt(), Water());

    public static string Drive() => Film("drive", Hit(Clubs.Find("Driver").shot, null, AimDownHole(240f)));

    public static string Approach() => Film("approach", Hit(ShotData.FromMph(86f, 27f, 0f, 9000f, 0f), FromPin(100f), 0f));

    public static string Chip() => Film("chip", Hit(ShotData.FromMph(38f, 30f, 0f, 5000f, 0f), FromPin(22f), 0f));

    public static string Putt()
    {
        // Search for a holed putt from 7 m (slightly different aims), like a player reading the break.
        for (float aim = 0f; aim <= 6f; aim += 0.5f)
            foreach (float sign in new[] { 1f, -1f })
                foreach (float extra in new[] { 1.05f, 1.12f, 1.0f, 1.2f })
                {
                    var from = FromPin(7f);
                    var shot = ShotData.FromMph(0, 1f, 0, 0, 0);
                    shot.ballSpeed = Mathf.Sqrt(2f * GreenDecel * 7f) * extra;
                    var rec = Hit(shot, from, aim * sign);
                    if (rec.Holed) return Film("putt", rec);
                }
        return Film("putt_missed", Recorder.Last);
    }

    public static string Tree()
    {
        for (float aim = 6f; aim <= 40f; aim += 2f)
            foreach (float sign in new[] { 1f, -1f })
            {
                var rec = Hit(Clubs.Find("Driver").shot, null, aim * sign);
                if (rec.ObstacleTime >= 0f && rec.ObstacleTime < (rec.LandTime < 0f ? rec.Duration : rec.LandTime)) return Film("tree", rec);
            }
        return "tree: no tree hit found";
    }

    public static string Woods()
    {
        foreach (float aim in new[] { -35f, 35f, -30f, 30f, -40f, 40f })
        {
            var rec = Hit(Clubs.Find("5 Iron").shot, null, aim);
            if (rec.ObstacleTime >= 0f) return Film("woods", rec);
        }
        return "woods: no tree hit found";
    }

    public static string Water()
    {
        for (float aim = 0f; aim <= 40f; aim += 2f)
            foreach (float sign in new[] { 1f, -1f })
                foreach (var club in new[] { "Driver", "5 Iron", "7 Iron" })
                {
                    var rec = Hit(Clubs.Find(club).shot, null, aim * sign);
                    if (rec.Water) return Film("water", rec);
                }
        return "water: no water found";
    }

    /// <summary>In a round: a shot that replays by itself holds the next turn; skipping lets the turn start.</summary>
    public static string Flow()
    {
        var d = RoundDirector.Instance;
        var r = ReplayDirector.Instance;
        var log = new StringBuilder();
        if (d.Round == null) return "start a round first (RoundPlayTest.Solo)";
        var ball = d.Ball;
        Shots.Submit(new RemoteShotMessage { type = "shot", id = Random.Range(1, 1 << 30), club = "Driver", speed = 74.6f, launch = 10.9f,
                                             back = 2686f, side = 0f }, "ReplayCheck", out _);
        for (int i = 0; i < 6000 && ball.InMotion; i++) ball.Advance(0.02f);
        var rec = Recorder.Last;
        log.AppendLine($"shot: {rec.end}, {rec.Total * ShotData.YardsPerMeter:0} yd, reason '{r.settings.Reason(rec)}', busy {r.Busy}, screen {d.ScreenName}");
        if (!r.Busy) r.Queue(rec);
        int strokesBefore = d.Round.CurrentBall.strokes;
        for (int i = 0; i < 200 && !r.IsPlaying; i++) r.Tick(Frame);
        log.AppendLine($"replay playing {r.IsPlaying}, camera '{r.CurrentShot}', hold {RoundDirector.Hold?.Invoke()}");
        for (int i = 0; i < 45; i++) r.Tick(Frame);
        log.AppendLine($"after 1.5 s: camera '{r.CurrentShot}', t {r.Time:0.00}");
        NavInput.Push(NavKey.Select); // skip
        for (int i = 0; i < 60 && r.Busy; i++) r.Tick(Frame);
        log.AppendLine($"after skip: busy {r.Busy}, hold {RoundDirector.Hold?.Invoke()}");
        d.RunPending();
        log.AppendLine($"next turn: screen {d.ScreenName}, player {d.Round.CurrentBall?.player}, strokes {d.Round.CurrentBall?.strokes} (was {strokesBefore})");
        log.AppendLine($"Up between turns -> manual replay possible {r.CanReplay}");
        return log.ToString();
    }

    // ---- helpers ----

    /// <summary>The aim offset (degrees from the pin line) that points the tee shot at the hole's centre line this far out.</summary>
    static float AimDownHole(float meters)
    {
        var hole = Hole;
        var tee = hole.TeeWorld;
        var target = hole.PinWorld;
        foreach (var local in hole.holePath)
        {
            var p = hole.transform.TransformPoint(local);
            if (Vector3.ProjectOnPlane(p - tee, Vector3.up).magnitude >= meters) { target = p; break; }
        }
        var toPin = Vector3.ProjectOnPlane(hole.PinWorld - tee, Vector3.up);
        var toTarget = Vector3.ProjectOnPlane(target - tee, Vector3.up);
        return Vector3.SignedAngle(toPin, toTarget, Vector3.up);
    }

    /// <summary>A spot on the hole's centre line this far from the pin (in the fairway), else straight back toward the tee.</summary>
    static Vector3 FromPin(float meters)
    {
        var hole = Hole;
        for (int i = hole.holePath.Length - 1; i >= 0; i--)
        {
            var p = hole.transform.TransformPoint(hole.holePath[i]);
            if (Vector3.ProjectOnPlane(p - hole.PinWorld, Vector3.up).magnitude >= meters)
                return hole.PinWorld + Vector3.ProjectOnPlane(p - hole.PinWorld, Vector3.up).normalized * meters;
        }
        var back = Vector3.ProjectOnPlane(hole.TeeWorld - hole.PinWorld, Vector3.up).normalized;
        return hole.PinWorld + back * meters;
    }

    static ShotRecording Hit(ShotData shot, Vector3? from, float aim)
    {
        var ball = Ball;
        if (from is Vector3 p) ball.PlaceOnGround(p);
        else ball.ResetToTee();
        ball.aimOffset = aim;
        ball.windSpeed = 0f;
        ball.Hit(shot);
        for (int i = 0; i < 6000 && ball.InMotion; i++) ball.Advance(0.02f);
        return Recorder.Last;
    }

    static string Film(string name, ShotRecording rec)
    {
        if (rec == null) return $"{name}: no recording";
        Directory.CreateDirectory(Out);
        foreach (var old in Directory.GetFiles(Out, name + "_*.png")) File.Delete(old);
        var r = ReplayDirector.Instance;
        r.Queue(rec);
        for (int i = 0; i < 100 && !r.IsPlaying; i++) r.Tick(Frame);
        if (!r.IsPlaying) return $"{name}: replay didn't start";
        var plan = r.Plan;
        var sb = new StringBuilder();
        sb.AppendLine($"{name}: {rec.end} on {rec.result.restingSurface}, carry {rec.Carry * ShotData.YardsPerMeter:0} yd, total {rec.Total * ShotData.YardsPerMeter:0} yd, " +
                      $"apex {rec.Apex:0.0} m, land {rec.LandTime:0.00} s, tree {rec.ObstacleTime:0.00} s, end {rec.Duration:0.00} s, " +
                      $"{rec.StartToPin:0.0} -> {rec.RestToPin:0.00} m to pin, reason '{r.settings.Reason(rec)}', replay {plan.RealLength():0.0} s");
        int index = 0;
        foreach (var shot in plan.shots)
        {
            var times = new[] { shot.start + 0.15f * shot.Length, shot.start + 0.5f * shot.Length, shot.end - 0.08f * shot.Length };
            foreach (float t in times)
            {
                for (int i = 0; i < 4000 && r.IsPlaying && r.Time < t; i++) r.Tick(Frame);
                if (!r.IsPlaying) break;
                string file = $"{name}_{index:00}_{shot.name.Replace(' ', '-')}_{r.Time:0.00}.png";
                Render(Out + file);
                var cam = Camera.main.transform;
                sb.AppendLine($"  {file}: cam {cam.position} fov {Camera.main.fieldOfView:0.0} ball {rec.PositionAt(r.Time)} " +
                              $"dist {Vector3.Distance(cam.position, rec.PositionAt(r.Time)):0} m, speed x{plan.SpeedAt(r.Time):0.00}");
                index++;
            }
        }
        r.Skip();
        for (int i = 0; i < 100 && r.Busy; i++) r.Tick(Frame);
        for (int i = 0; i < 30; i++) r.Tick(Frame);
        return sb.ToString();
    }

    static void Render(string path)
    {
        var src = Camera.main;
        var go = new GameObject("ReplayCheckCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.CopyFrom(src);
        go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        cam.aspect = 1280f / 720f;
        for (int i = 0; i < 4; i++) cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        // The broadcast letterbox (the overlay is UI, not in this render): 11% bars top and bottom.
        int bar = Mathf.RoundToInt(720 * 0.11f);
        var black = Enumerable.Repeat(Color.black, 1280 * bar).ToArray();
        tex.SetPixels(0, 0, 1280, bar, black);
        tex.SetPixels(0, 720 - bar, 1280, bar, black);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
    }
}
