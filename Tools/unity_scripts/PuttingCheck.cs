// Dev checks for putting (not compiled into the project), run with the Unity CLI:
//   unity command run_script --file Tools/unity_scripts/PuttingCheck.cs --entry PuttingCheck.Flat
//   Flat     (Edit or Play mode) a preview at PuttModel's speed for D metres rolls D ± 10 % on a flat green
//   Green    (Play mode, HoleSimulator) on the hole's green, the real ball hit with the preview's aim and speed
//            follows the predicted line and stops within 5 cm of the predicted end
//   Setup    (Play mode) a 6 m breaking putt in putting mode, camera lined up and the preview drawn; then
//            Render saves the camera view (the break line, no HUD) to OutDir
//   Stroke   (after Setup) hits the putt with 90 % of the read and shows the HUD result bar
// The Editor doesn't tick Play mode in the background, so these call GolfBall.Advance and the preview's
// LateUpdate themselves.
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using GolfSim.Ball;
using GolfSim.Course;
using GolfSim.Game;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class PuttingCheck
{
    const string OutDir = "/Users/obadiah/.claude/jobs/5fd17b43/tmp/putting";
    const float Frame = 0.02f;

    // ---- flat green ----

    public static string Flat()
    {
        var settings = BallPhysicsSettings.Defaults;
        float stimp = PuttModel.GreenStimp(settings);
        var go = FlatGreen(out var map);
        var log = new StringBuilder($"Stimp {stimp:0.0} ft (green rolling {settings.For("green").rolling})\n");
        bool ok = true;
        try
        {
            var origin = go.transform.position + new Vector3(20f, 0f, 5f);
            origin.y = map.HeightAt(origin) + BallPhysicsSettings.Radius;
            foreach (float d in new[] { 0.5f, 1f, 2f, 3f, 5f, 8f, 12f, 20f })
            {
                float v = PuttModel.SpeedFor(d, stimp);
                float coarse = Rolled(map, settings, origin, v, PuttPredictor.CoarseStep);
                float fine = Rolled(map, settings, origin, v, GolfBall.Step);
                bool pass = Mathf.Abs(coarse / d - 1f) <= 0.1f && Mathf.Abs(fine / d - 1f) <= 0.1f;
                ok &= pass;
                log.AppendLine($"{(pass ? "PASS" : "FAIL")} {d,5:0.0} m: speed {v:0.00} m/s ({v / ShotData.MetersPerSecondPerMph:0.0} mph) " +
                               $"rolled {coarse:0.00} m coarse, {fine:0.00} m at the ball's step ({(fine / d - 1f) * 100f:+0.0;-0.0} %)");
            }
        }
        finally { Object.DestroyImmediate(go); }
        return (ok ? "ALL PASS\n" : "SOME FAILED\n") + log;
    }

    static float Rolled(TerrainSurfaceMap map, BallPhysicsSettings settings, Vector3 origin, float speed, float step) =>
        Vector3.ProjectOnPlane(PuttPredictor.Simulate(map, settings, origin, Vector3.forward, PuttModel.PuttAt(speed), step: step).end - origin,
                               Vector3.up).magnitude;

    /// <summary>A temporary 40 x 40 m flat terrain painted with one "green" layer (not saved).</summary>
    static GameObject FlatGreen(out TerrainSurfaceMap map)
    {
        var data = new TerrainData { heightmapResolution = 33, alphamapResolution = 16 };
        data.size = new Vector3(40f, 10f, 40f);
        data.terrainLayers = new[] { new TerrainLayer { name = "green" } };
        var go = Terrain.CreateTerrainGameObject(data);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.transform.position = new Vector3(5000f, -500f, 5000f); // far from any hole
        map = new TerrainSurfaceMap(go.GetComponent<Terrain>(), null);
        return go;
    }

    // ---- the hole's green ----

    public static string Green()
    {
        var ball = Object.FindAnyObjectByType<GolfBall>();
        var hole = Object.FindAnyObjectByType<HoleInfo>();
        if (!ball || !hole || !Application.isPlaying) return "needs Play mode with a hole scene";
        var preview = ball.GetComponent<PuttPreview>();
        if (!preview) preview = ball.gameObject.AddComponent<PuttPreview>();
        var map = preview.Map;
        var log = new StringBuilder();
        bool ok = true;
        int tested = 0;
        foreach (float distance in new[] { 3f, 6f, 9f })
            foreach (float heading in new[] { 0f, 90f, 180f, 270f })
            {
                if (!SpotOnGreen(map, hole, distance, heading, out var spot)) continue;
                foreach (float aim in new[] { -4f, 3f })
                {
                    ball.PlaceOnGround(spot);
                    ball.aimOffset = aim;
                    preview.Refresh();
                    var predictedPath = new List<Vector3>(preview.Path);
                    var predicted = preview.Prediction;
                    var exact = PuttPredictor.Simulate(map, ball.Settings, ball.transform.position, ball.AimDirection,
                                                       PuttModel.PuttAt(preview.SolvedSpeed), hole.PinWorld, step: GolfBall.Step);
                    var realPath = Hit(ball, PuttModel.PuttAt(preview.SolvedSpeed));
                    bool holed = ball.Status == BallStatus.Holed;
                    float endError = Horizontal(ball.transform.position - predicted.end).magnitude;
                    float exactError = Horizontal(ball.transform.position - exact.end).magnitude;
                    float pathError = MaxDeviation(realPath, predictedPath);
                    // A line that grazes the cup edge can drop for one and lip past for the other: a lip, not a miss.
                    bool lip = holed != predicted.holed && Mathf.Abs(MinDistance(predictedPath, hole.PinWorld) - GolfBall.CupRadius) < 0.02f;
                    bool pass = lip || (holed == predicted.holed && endError <= 0.05f && pathError <= 0.05f);
                    ok &= pass;
                    tested++;
                    log.AppendLine($"{(lip ? "LIP " : pass ? "PASS" : "FAIL")} {distance:0} m @ {heading:0}°, aim {aim:+0;-0}°: speed {preview.SolvedSpeed:0.00} m/s " +
                                   $"(plays {preview.PlaysAs:0.00} m, rise {preview.ElevationToPin * 100f:+0;-0} cm) | end off by {endError * 100f:0.0} cm " +
                                   $"(exact-step preview {exactError * 100f:0.0} cm), path within {pathError * 100f:0.0} cm, " +
                                   $"{(holed ? "holed" : $"{Horizontal(ball.transform.position - hole.PinWorld).magnitude:0.00} m from pin")}" +
                                   $"{(predicted.holed != holed ? " [holed mismatch]" : "")}");
                }
            }
        ball.aimOffset = 0f;
        if (tested == 0) return "no green spots found around the pin";
        return (ok ? $"ALL PASS ({tested})\n" : "SOME FAILED\n") + log;
    }

    /// <summary>Hits the real ball and runs it to rest; returns its path sampled every frame.</summary>
    static List<Vector3> Hit(GolfBall ball, ShotData shot)
    {
        var path = new List<Vector3> { ball.transform.position };
        ball.Hit(shot);
        for (int i = 0; i < 3000 && ball.InMotion; i++)
        {
            ball.Advance(Frame);
            path.Add(ball.transform.position);
        }
        return path;
    }

    static bool SpotOnGreen(TerrainSurfaceMap map, HoleInfo hole, float distance, float heading, out Vector3 spot)
    {
        var dir = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
        spot = hole.PinWorld + dir * distance;
        for (float d = 0f; d <= distance; d += 0.25f)
        {
            var p = hole.PinWorld + dir * d;
            if (!map.Contains(p) || map.SurfaceAt(p) != "green") return false;
        }
        return true;
    }

    /// <summary>Largest flat distance from a point of `a` to the polyline `b`.</summary>
    static float MaxDeviation(List<Vector3> a, List<Vector3> b)
    {
        float worst = 0f;
        foreach (var p in a)
        {
            float best = float.MaxValue;
            for (int i = 1; i < b.Count; i++) best = Mathf.Min(best, SegmentDistance(Horizontal(p), Horizontal(b[i - 1]), Horizontal(b[i])));
            worst = Mathf.Max(worst, best);
        }
        return worst;
    }

    static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float t = ab.sqrMagnitude > 1e-9f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector3.Distance(p, a + ab * t);
    }

    static Vector3 Horizontal(Vector3 v) => new Vector3(v.x, 0f, v.z);

    // ---- screenshots ----

    /// <summary>Puts the ball 6 m from the pin across the slope, in putting mode, with the preview drawn.</summary>
    public static string Setup() => SetupPutt(6f, PuttingAssist.Partial);
    public static string SetupFull() => SetupPutt(6f, PuttingAssist.Full);

    static string SetupPutt(float distance, PuttingAssist assist)
    {
        var d = RoundDirector.Instance;
        var ball = Object.FindAnyObjectByType<GolfBall>();
        var hole = Object.FindAnyObjectByType<HoleInfo>();
        if (!d || !ball || !hole) return "needs Play mode with a hole scene";
        var preview = ball.GetComponent<PuttPreview>();
        var map = preview.Map;
        // The heading whose line to the pin breaks the most (steepest slope across it).
        float best = -1f, bestHeading = 0f;
        for (float h = 0f; h < 360f; h += 15f)
        {
            if (!SpotOnGreen(map, hole, distance, h, out var p)) continue;
            var (downhill, percent) = GreenReading.SlopeAt(map, Vector3.Lerp(p, hole.PinWorld, 0.5f));
            float across = percent * Mathf.Abs(Vector3.Dot(downhill, Vector3.Cross(Vector3.up, Horizontal(hole.PinWorld - p).normalized)));
            if (across > best) { best = across; bestHeading = h; }
        }
        if (best < 0f) return "no green spot found";
        SpotOnGreen(map, hole, distance, bestHeading, out var spot);
        ball.PlaceOnGround(spot);
        ball.aimOffset = 0f;
        PuttPreview.Assist = assist;
        d.SetClub(Clubs.Putter);
        // Aim at the high side: the aim that the preview says gets closest to the hole.
        float bestAim = 0f, closest = float.MaxValue;
        for (float a = -8f; a <= 8f; a += 1f)
        {
            ball.aimOffset = a;
            preview.Refresh();
            float miss = MinDistance(preview.Path, hole.PinWorld);
            if (miss < closest) { closest = miss; bestAim = a; }
        }
        ball.aimOffset = 0f;
        d.Aim(bestAim);
        var panel = ball.GetComponent<ShotPanel>();
        if (panel) panel.LineUp(); // putting mode's camera: low behind the ball, looking at the cup
        Tick(ball);
        return $"6 m putt from heading {bestHeading:0}°, aim {bestAim:+0;-0}° (line passes {closest * 100f:0} cm from the cup), " +
               $"speed {preview.SolvedSpeed:0.00} m/s, plays {preview.PlaysAs:0.00} m, rise {preview.ElevationToPin * 100f:+0;-0} cm\n" +
               $"state {d.BuildState().ToJson()}";
    }

    static float MinDistance(IReadOnlyList<Vector3> path, Vector3 pin)
    {
        float best = float.MaxValue;
        foreach (var p in path) best = Mathf.Min(best, Horizontal(p - pin).magnitude);
        return best;
    }

    /// <summary>Hits the set-up putt at 90 % of the read through the remote shot path, then runs it to rest.</summary>
    public static string Stroke()
    {
        var d = RoundDirector.Instance;
        var ball = d.Ball;
        var preview = ball.GetComponent<PuttPreview>();
        float speed = PuttModel.SpeedFor(preview.PlaysAs * 0.9f, preview.Stimp);
        var msg = new RemoteShotMessage { type = "shot", id = Random.Range(1, 1 << 30), club = Clubs.Putter, speed = speed, launch = 1f };
        var ack = Shots.Submit(msg, "PuttingCheck", out _);
        for (int i = 0; i < 3000 && ball.InMotion; i++) ball.Advance(Frame);
        d.RunPending();
        Tick(ball);
        return $"ack {ack.status} {ack.message}: {ball.Status}, rolled {ball.Result.total:0.00} m\nstate {d.BuildState().ToJson()}";
    }

    /// <summary>Runs one frame of the components that matter (the Editor doesn't tick in the background).</summary>
    static void Tick(GolfBall ball)
    {
        foreach (var c in ball.GetComponents<MonoBehaviour>())
            if (c.isActiveAndEnabled) Call(c, "LateUpdate");
        var cam = Camera.main ? Camera.main.GetComponent<HoleFlyCamera>() : null;
        if (cam) Call(cam, "Update");
    }

    public static string Render() => RenderTo("break_line.png");
    public static string RenderFull() => RenderTo("break_line_full.png");

    static string RenderTo(string name)
    {
        Directory.CreateDirectory(OutDir);
        string path = Path.Combine(OutDir, name);
        var src = Camera.main;
        var go = new GameObject("TestCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.CopyFrom(src);
        go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        var rt = new RenderTexture(1600, 900, 24);
        cam.targetTexture = rt;
        for (int i = 0; i < 6; i++) cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
        return path;
    }

    static void Call(Object target, string method) =>
        target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)?.Invoke(target, null);
}
