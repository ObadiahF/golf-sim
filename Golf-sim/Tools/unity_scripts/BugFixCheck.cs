// Dev check (Play mode, Hole Simulator in practice), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/BugFixCheck.cs --entry BugFixCheck.All
// Re-runs the repros of Docs/bug-hunt.md U-1 .. U-5, U-9 and M-1 and reports PASS / FAIL for each. Drives the ball
// with GolfBall.Advance and calls ShotPanel's Reset (R) directly, as the QA script did.
//   Menu   (run last; loads a scene) M-1: two Restarts during the fade start one scene load, and a second
//          "Play a Round" doesn't replace the round
using System.Reflection;
using System.Text;
using GolfSim.Ball;
using GolfSim.Course;
using GolfSim.Game;
using UnityEngine;
using UnityEngine.UIElements;

public static class BugFixCheck
{
    const float Frame = 0.02f;
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    static GolfBall Ball => Object.FindAnyObjectByType<GolfBall>();
    static ShotPanel Panel => Ball.GetComponent<ShotPanel>();
    static HoleFlyCamera FlyCam => Camera.main.GetComponent<HoleFlyCamera>();

    public static string All()
    {
        var sb = new StringBuilder();
        foreach (var test in new System.Func<string>[] { TracerReset, CameraReset, WindMidFlight, PracticeLie, OffMapInAir, HelpOverlay })
        {
            try { sb.AppendLine(test()); }
            catch (System.Exception e) { sb.AppendLine($"{test.Method.Name}: EXCEPTION {e}"); }
        }
        SetWind(0f, 0f);
        Ball.aimOffset = 0f;
        Reset();
        return sb.ToString();
    }

    /// <summary>U-1: R clears the trace, mid-flight and at rest.</summary>
    public static string TracerReset()
    {
        var line = (TracerLine)typeof(BallTracer).GetField("line", Private).GetValue(Ball.GetComponent<BallTracer>());
        Reset();
        Hit("Driver");
        Run(1.5f);
        int flying = line.Count;
        Reset();
        Tick();
        int afterMid = line.Count;
        Hit("Driver");
        Run(30f);
        int rest = line.Count;
        Reset();
        Tick();
        int afterRest = line.Count;
        bool pass = flying > 1 && rest > 1 && afterMid == 0 && afterRest == 0;
        return $"U-1 {(pass ? "PASS" : "FAIL")}: trace points mid-flight {flying} -> after R {afterMid}; at rest {rest} -> after R {afterRest}";
    }

    /// <summary>U-2: R mid-flight leaves the camera at the line-up pose, not chasing.</summary>
    public static string CameraReset()
    {
        Panel.followBall = true;
        Reset();
        Hit("Driver");
        Run(1.5f);
        bool chasing = FlyCam.trackTarget == Ball.transform && FlyCam.followOffset != Vector3.zero;
        Reset();
        var pose = FlyCam.transform.position;
        Tick(3f);
        bool pass = chasing && FlyCam.trackTarget == null && FlyCam.followOffset == Vector3.zero && (FlyCam.transform.position - pose).magnitude < 0.01f;
        return $"U-2 {(pass ? "PASS" : "FAIL")}: chasing in flight {chasing}; after R trackTarget {(FlyCam.trackTarget ? FlyCam.trackTarget.name : "none")}, " +
               $"followOffset {FlyCam.followOffset}, camera moved {(FlyCam.transform.position - pose).magnitude:0.00} m in 3 s";
    }

    /// <summary>U-3: Space mid-flight (with new wind on the sliders) changes nothing.</summary>
    public static string WindMidFlight()
    {
        SetWind(0f, 0f);
        Reset();
        Hit("Driver");
        Run(30f);
        float baseline = Ball.Result.offline;
        Reset();
        Hit("Driver");
        Run(1f);
        SetWind(30f, 90f);
        Panel.Hit();
        float wind = Ball.windSpeed;
        Run(30f);
        float offline = Ball.Result.offline;
        SetWind(0f, 0f);
        bool pass = wind == 0f && Mathf.Abs(offline - baseline) < 0.01f;
        return $"U-3 {(pass ? "PASS" : "FAIL")}: wind after the mid-flight press {wind:0.0} m/s; offline {offline * ShotData.YardsPerMeter:0.0} yd vs {baseline * ShotData.YardsPerMeter:0.0} yd without the press";
    }

    /// <summary>U-4: after a shot into the trees, R shows LIE Tee in practice.</summary>
    public static string PracticeLie()
    {
        Reset();
        Ball.aimOffset = 35f; // into the trees right of the line
        Hit("Driver");
        Run(30f);
        string after = RoundDirector.Instance.BuildState().lie;
        Ball.aimOffset = 0f;
        Reset();
        string reset = RoundDirector.Instance.BuildState().lie;
        string hud = Hud("hud-lie");
        bool pass = reset == "tee" && hud == "Tee";
        return $"U-4 {(pass ? "PASS" : "FAIL")}: lie after the shot '{after}' (hit tree {Ball.Result.hitTree}), after R '{reset}', HUD shows '{hud}'";
    }

    /// <summary>U-5: a ball leaving the map in the air has its hang time and no land angle.</summary>
    public static string OffMapInAir()
    {
        Reset();
        Ball.aimOffset = 180f;
        Ball.collideWithObstacles = false; // the trees behind the tee would stop it now
        Ball.Hit(ShotData.FromMph(200f, 15f, 0f, 3000f, 0f));
        Run(30f);
        var r = Ball.Result;
        Ball.aimOffset = 0f;
        Ball.collideWithObstacles = true;
        bool pass = Ball.Status == BallStatus.OutOfBounds && !r.landed && r.flightTime > 0.5f && r.flightTime < 2f;
        return $"U-5 {(pass ? "PASS" : "FAIL")}: {Ball.Status}, carry {r.carry * ShotData.YardsPerMeter:0.0} yd, hang {r.flightTime:0.00} s, landed {r.landed} (panel: land angle n/a)";
    }

    /// <summary>U-9: in practice with the HUD, the fly camera's help box is off.</summary>
    public static string HelpOverlay()
    {
        bool pass = !FlyCam.showHelp && Hud("hud-lie") != null;
        return $"U-9 {(pass ? "PASS" : "FAIL")}: fly-camera help shown {FlyCam.showHelp} (H toggles it), HUD present {Hud("hud-lie") != null}";
    }

    /// <summary>M-1: two Restarts in the pause menu during the fade, then "Play a Round" twice.</summary>
    public static string Menu()
    {
        int loads = 0;
        UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.LoadSceneMode> count = (_, _) => loads++;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += count;
        var home = Object.FindAnyObjectByType<HomeMenu>();
        var actions = (System.Action[])typeof(HomeMenu).GetField("actions", Private).GetValue(home);
        actions[1]();
        bool loading = ScreenFade.Loading;
        actions[1]();
        RoundDirector.Instance.PlayRound(1); // also ignored while leaving
        bool roundStarted = RoundDirector.Instance.Round != null;
        // The Editor ticks the fade (autotick), then the scene loads; count the loads 2.5 s later.
        var watch = new System.Diagnostics.Stopwatch();
        string report = $"M-1: fade loading after the first Restart {loading}; round started by Play during the fade {roundStarted}";
        UnityEditor.EditorApplication.CallbackFunction check = null;
        watch.Start();
        check = () =>
        {
            if (watch.ElapsedMilliseconds < 2500) return;
            UnityEditor.EditorApplication.update -= check;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= count;
            Debug.Log($"[BugFixCheck] {report}; scene loads {loads} -> {(loads == 1 && loading && !roundStarted ? "PASS" : "FAIL")}");
        };
        UnityEditor.EditorApplication.update += check;
        return report + " (scene load count is logged as [BugFixCheck] in 2.5 s)";
    }

    // ---- helpers ----

    static void Hit(string club)
    {
        Panel.SelectClub(club);
        Panel.Hit();
    }

    static void Reset() => typeof(ShotPanel).GetMethod("ResetBall", Private).Invoke(Panel, null);

    static void SetWind(float mph, float from)
    {
        typeof(ShotPanel).GetField("windMph", Private).SetValue(Panel, mph);
        typeof(ShotPanel).GetField("windFrom", Private).SetValue(Panel, from);
    }

    /// <summary>Advances the ball (and the tracer and camera, as a frame would) for this long or until it stops.</summary>
    static void Run(float seconds)
    {
        for (float t = 0f; t < seconds && Ball.InMotion; t += Frame)
        {
            Ball.Advance(Frame);
            Tick();
        }
    }

    static void Tick(float seconds = Frame)
    {
        for (float t = 0f; t < seconds; t += Frame)
        {
            Call(FlyCam, "Update");
            Call(Ball.GetComponent<BallTracer>(), "LateUpdate");
        }
    }

    static void Call(Object target, string method) => target.GetType().GetMethod(method, Private)?.Invoke(target, null);

    static string Hud(string name)
    {
        var doc = RoundDirector.Instance.GetComponentInChildren<UIDocument>();
        return doc ? doc.rootVisualElement.Q<Label>(name)?.text : null;
    }
}
