// Dev check (Edit mode, no server), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/RoughCheck.cs --entry RoughCheck.Shots
// Builds one cached hole (Temp/roughcheck_args.txt: "<package folder name>,<theme>,<output folder>") and renders the
// rough the same way every time, for before/after comparisons of the ground and its grass:
//   knee     knee height in the rough beside the fairway, looking down the hole
//   edge     2 m up on the fairway edge, looking across into the rough (the fairway-to-rough transition)
//   wide     a broadcast view from 6 m up over the fairway, the rough on both sides
//   native_knee / native_wide   the same in the nearest native or scrub area (the out-of-play ground beyond the rough)
using System.IO;
using GolfSim.Course;
using GolfSim.Game;
using GolfSim.Net;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RoughCheck
{
    const string ArgsPath = "Temp/roughcheck_args.txt";
    const int Width = 1600, Height = 900;

    public static string Shots()
    {
        var a = File.ReadAllText(ArgsPath).Trim().Split(',');
        string package = a[0], theme = a[1], output = a[2];
        EditorSceneManager.OpenScene(EditorSceneManager.GetActiveScene().path, OpenSceneMode.Single); // as saved
        var pkg = HolePackage.Load(Path.Combine(TrainerHoles.CacheFolder, package, HolePackage.FileName));
        pkg.theme = theme;
        var hole = RuntimeHoleBuilder.Build(pkg, CourseRound.Load().themes);
        Scenery.Apply(hole, TimeOfDay.Day);

        var tee = hole.TeeWorld;
        var down = Vector3.ProjectOnPlane(hole.PinWorld - tee, Vector3.up).normalized;
        var side = Vector3.Cross(Vector3.up, down);
        // Along the line 150 m out: where the fairway ends and the rough starts (right side first).
        Vector3 edge = tee + down * 150f, rough = edge;
        float dir = 1f;
        foreach (float s in new[] { 1f, -1f })
        {
            bool sawFairway = false;
            for (float d = -20f; d < 60f; d += 0.5f)
            {
                var p = tee + down * 150f + side * s * d;
                string surface = CourseSurface.At(p);
                if (surface == "fairway") sawFairway = true;
                else if (sawFairway && surface == "rough") { edge = p; rough = p + side * s * 6f; dir = s; goto found; }
            }
        }
        found:
        Directory.CreateDirectory(output);
        Render(At(rough, 0.6f), At(rough + down * 40f, 0.2f), Path.Combine(output, "knee.png"));
        Render(At(edge - side * dir * 3f, 2f), At(edge + side * dir * 10f, 0f), Path.Combine(output, "edge.png"));
        var over = edge - side * dir * 12f - down * 25f;
        Render(At(over, 6f), At(over + down * 60f, 0f), Path.Combine(output, "wide.png"));
        string native = "none";
        if (Nearest(tee + down * 150f, s => s is "native" or "scrub", out var wild))
        {
            var toward = Vector3.ProjectOnPlane(wild - (tee + down * 150f), Vector3.up).normalized;
            Render(At(wild + toward * 4f, 0.6f), At(wild + toward * 40f, 0.3f), Path.Combine(output, "native_knee.png"));
            Render(At(wild - toward * 25f, 6f), At(wild + toward * 30f, 0f), Path.Combine(output, "native_wide.png"));
            native = $"{wild} ({CourseSurface.At(wild)})";
        }
        return $"edge at {edge} ({CourseSurface.At(edge)}), rough at {rough} ({CourseSurface.At(rough)}), native at {native}: saved to {output}";
    }

    /// <summary>The nearest point (rings of 2 m out to 150 m) whose surface matches, with 8 m of it beyond.</summary>
    static bool Nearest(Vector3 from, System.Func<string, bool> match, out Vector3 found)
    {
        for (float r = 2f; r < 150f; r += 2f)
            for (int i = 0; i < 64; i++)
            {
                var dir = Quaternion.Euler(0f, i * 360f / 64f, 0f) * Vector3.forward;
                var p = from + dir * r;
                if (match(CourseSurface.At(p)) && match(CourseSurface.At(p + dir * 8f))) { found = p; return true; }
            }
        found = from;
        return false;
    }

    static Vector3 At(Vector3 p, float up)
    {
        var terrain = Terrain.activeTerrain;
        p.y = (terrain ? terrain.SampleHeight(p) + terrain.transform.position.y : p.y) + up;
        return p;
    }

    static void Render(Vector3 from, Vector3 lookAt, string path)
    {
        var cam = Camera.main;
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(lookAt - from));
        cam.aspect = (float)Width / Height;
        var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
        var previous = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = previous;
        cam.ResetAspect();
        var active = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        tex.Apply();
        RenderTexture.active = active;
        RenderTexture.ReleaseTemporary(rt);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}
