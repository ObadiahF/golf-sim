// Dev helper, run from the shell with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/HoleShots.cs --entry HoleShots.Run
// Renders views of the current hole from a throwaway camera into Temp/shot_<view>.png,
// leaving the scene's cameras and the editor layout untouched.
using System.Collections.Generic;
using System.IO;
using GolfSim.Course;
using GolfSim.CourseEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class HoleShots
{
    const int Width = 1280, Height = 720;
    const float EyeHeight = 1.7f;

    public static string Run()
    {
        var hole = HoleNavigation.FindHole();
        if (!hole) return "no hole in scene";

        var go = new GameObject("TempShotCamera") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.farClipPlane = 4000f;
        go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        var rt = new RenderTexture(Width, Height, 24);
        cam.targetTexture = rt;
        try
        {
            foreach (var shot in Views(hole))
            {
                go.transform.SetPositionAndRotation(shot.Value.position, shot.Value.rotation);
                for (int i = 0; i < 8; i++) cam.Render(); // let exposure / temporal effects settle
                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                File.WriteAllBytes($"Temp/shot_{shot.Key}.png", tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
        }
        finally
        {
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
        }
        return "ok";
    }

    static Dictionary<string, Pose> Views(HoleInfo hole)
    {
        var fwd = hole.PinWorld - hole.TeeWorld;
        fwd.y = 0;
        fwd.Normalize();
        var right = Vector3.Cross(Vector3.up, fwd);
        return new Dictionary<string, Pose>
        {
            { "Tee", HoleViews.Get(hole, HoleView.Tee) },
            { "Green", HoleViews.Get(hole, HoleView.Green) },
            { "Overview", HoleViews.Get(hole, HoleView.Overview) },
            { "Approach", LookAt(OnGround(hole.PinWorld - fwd * 75f, 6f), hole.PinWorld) },
            { "Cup", LookAt(OnGround(hole.PinWorld - fwd * 0.9f + right * 0.3f, 0.45f), hole.PinWorld) },
            { "PinMid", LookAt(OnGround(hole.PinWorld - fwd * 6f + right * 2f, 1.5f), hole.PinWorld + Vector3.up * 1.2f) },
            { "GreenSide", LookAt(OnGround(hole.PinWorld - fwd * 18f + right * 14f, EyeHeight), hole.PinWorld) },
            { "Rough", LookAt(OnGround(Vector3.Lerp(hole.TeeWorld, hole.PinWorld, 0.15f) + right * 25f, 1.2f),
                              OnGround(Vector3.Lerp(hole.TeeWorld, hole.PinWorld, 0.3f) + right * 25f, 0f)) },
        };
    }

    static Vector3 OnGround(Vector3 p, float height)
    {
        var t = Terrain.activeTerrain;
        p.y = t.SampleHeight(p) + t.transform.position.y + height;
        return p;
    }

    static Pose LookAt(Vector3 from, Vector3 target) => new Pose(from, Quaternion.LookRotation(target - from));
}
