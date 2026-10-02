// Dev helper (Play mode, ideally paused), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/BallPlayTest.cs --entry BallPlayTest.Drive
// Hits a shot through the real ShotPanel, then advances the ball, tracer and camera by calling their
// own Update methods (the Editor doesn't tick Play mode while it's in the background). Saves camera
// renders mid-flight and at rest to Temp/play_*.png and returns the launch-monitor numbers.
using System.IO;
using System.Reflection;
using GolfSim.Ball;
using GolfSim.Course;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class BallPlayTest
{
    const float Frame = 0.02f; // Time.deltaTime while the Editor single-steps
    const float Yards = 1.0936f;

    public static string Drive() => Shoot("Driver", 3f);
    public static string Iron() => Shoot("7-iron", 3f);
    public static string Wedge() => Shoot("PW", 3f);

    /// <summary>Wedge into the green from 100 m short of the pin: should hop and check.</summary>
    public static string Approach() =>
        Shoot("Approach", 2.5f, ShotData.FromMph(86f, 27f, 0f, 9000f, 0f), fromPin: 100f);

    /// <summary>6 m putt at the speed for a Stimp-11 green (should finish at or in the hole).</summary>
    public static string Putt() =>
        Shoot("Putt", 1.5f, ShotData.FromMph(5.6f, 1f, 0f, 0f, 0f), fromPin: 6f);

    static string Shoot(string preset, float captureAt) =>
        Shoot(preset, captureAt, System.Array.Find(ShotData.Presets, p => p.name == preset).shot, 0f);

    static string Shoot(string preset, float captureAt, ShotData shot, float fromPin)
    {
        var ball = Object.FindAnyObjectByType<GolfBall>();
        var hole = Object.FindAnyObjectByType<HoleInfo>();
        if (fromPin > 0f)
        {
            var back = Vector3.ProjectOnPlane(hole.TeeWorld - hole.PinWorld, Vector3.up).normalized;
            ball.PlaceOnGround(hole.PinWorld + back * fromPin);
        }
        else ball.ResetToTee();
        var panel = ball.GetComponent<ShotPanel>();
        typeof(ShotPanel).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(panel, new object[] { shot });
        panel.Hit();

        var tracer = ball.GetComponent<BallTracer>();
        var cam = Camera.main.GetComponent<HoleFlyCamera>();
        float t = 0f;
        bool captured = false;
        while (ball.InMotion && t < 40f)
        {
            ball.Advance(Frame);
            Call(cam, "Update");
            Call(tracer, "LateUpdate");
            t += Frame;
            if (!captured && t >= captureAt) { Render($"Temp/play_{preset}_flight.png"); captured = true; }
        }
        Call(tracer, "LateUpdate");
        Render($"Temp/play_{preset}_rest.png");

        var r = ball.Result;
        float toPin = Vector3.ProjectOnPlane(hole.PinWorld - ball.transform.position, Vector3.up).magnitude;
        return $"{preset}: {ball.Status} on {r.restingSurface}, {toPin:0.00} m from pin | carry {r.carry * Yards:0.0} yd, total {r.total * Yards:0.0} yd, " +
               $"apex {r.apex * Yards:0.0} yd, offline {r.offline * Yards:0.0} yd, land {r.landAngle:0.0} deg, hang {r.flightTime:0.00} s, sim {t:0.0} s";
    }

    static void Call(Object target, string method) =>
        target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(target, null);

    static void Render(string path)
    {
        var src = Camera.main;
        var go = new GameObject("TestCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.CopyFrom(src);
        go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        for (int i = 0; i < 6; i++) cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
    }
}
