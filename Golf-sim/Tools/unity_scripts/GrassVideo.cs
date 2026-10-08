// Dev helper (Play mode, against a LOCAL game server or none), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/FeelPlay.cs --entry FeelPlay.Start      (a hole up first)
//   unity command run_script --file Tools/unity_scripts/GrassVideo.cs --entry GrassVideo.Record (or RecordNative; then wait ~Seconds)
//   ffmpeg -framerate 30 -i Temp/GrassVideo/f%04d.png -pix_fmt yuv420p grass.mp4
// Puts the camera low in the rough beside the fairway, looking down the hole with a slow pan, sets a moderate wind
// and saves every frame (at a fixed 30 fps game clock) to Temp/GrassVideo, so the grass and trees can be watched.
using System.Collections;
using System.IO;
using GolfSim.Course;
using GolfSim.Game;
using UnityEngine;

public static class GrassVideo
{
    const float Seconds = 8f;
    const int Fps = 30;
    const string Folder = "Temp/GrassVideo";

    public static string RecordNative() => Record(12f, "native");

    public static string Record(float mph = 12f, string surface = "rough")
    {
        var hole = Object.FindAnyObjectByType<HoleInfo>();
        var cam = Camera.main;
        if (!hole || !cam) return "no hole up yet";
        foreach (var b in cam.GetComponents<MonoBehaviour>()) b.enabled = false; // the hole camera would move it
        var hud = GameObject.Find("Round HUD");
        if (hud) hud.SetActive(false);

        var tee = hole.TeeWorld;
        var down = Vector3.ProjectOnPlane(hole.PinWorld - tee, Vector3.up).normalized;
        var side = Vector3.Cross(Vector3.up, down);
        // The first spot of that surface (rough, native...) 60 m down the hole, to the right or the left.
        Vector3 spot = tee + down * 60f;
        foreach (float s in new[] { 1f, -1f })
            for (float d = 8f; d < 150f; d += 2f)
            {
                var p = tee + down * 60f + side * s * d;
                if (CourseSurface.At(p) == surface && CourseSurface.At(p + side * s * 6f) == surface) { spot = p + side * s * 3f; goto found; }
            }
        found:
        FoliageWind.Apply(Quaternion.LookRotation(side).eulerAngles.y, mph); // across the view: the sway shows best

        if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
        Directory.CreateDirectory(Folder);
        var runner = new GameObject("Grass Video").AddComponent<Runner>();
        runner.StartCoroutine(Shoot(runner, cam, spot, down));
        return $"recording {Seconds} s from {spot} ({CourseSurface.At(spot)}) into {Folder}";
    }

    class Runner : MonoBehaviour { }

    static IEnumerator Shoot(Runner runner, Camera cam, Vector3 spot, Vector3 down)
    {
        Time.captureFramerate = Fps;
        int frames = Mathf.RoundToInt(Seconds * Fps);
        for (int i = 0; i < frames; i++)
        {
            float u = i / (float)(frames - 1);
            var p = spot + Vector3.up * 0.9f + down * Mathf.Lerp(0f, 4f, u);
            p.y = Terrain.activeTerrain ? Terrain.activeTerrain.SampleHeight(p) + Terrain.activeTerrain.transform.position.y + 0.9f : p.y;
            cam.transform.position = p;
            var look = Quaternion.AngleAxis(Mathf.Lerp(-25f, 15f, u), Vector3.up) * down;
            cam.transform.rotation = Quaternion.LookRotation(look + Vector3.down * 0.12f);
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes($"{Folder}/f{i:0000}.png", tex.EncodeToPNG());
            Object.Destroy(tex);
        }
        Time.captureFramerate = 0;
        File.WriteAllText($"{Folder}/done.txt", frames.ToString());
        Object.Destroy(runner.gameObject);
    }
}
