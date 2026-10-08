// Dev check (Play mode, against a LOCAL game server or none), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/FeelPlay.cs --entry FeelPlay.Start     (a one-hole parkland round)
//   unity command run_script --file Tools/unity_scripts/FeelPlay.cs --entry FeelPlay.Gale      (strong wind on the trees)
//   unity command run_script --file Tools/unity_scripts/FeelPlay.cs --entry FeelPlay.Splash    (a splash 35 m ahead)
//   unity command capture_game_view --save_path Assets/feel.png   ... twice, a moment apart, to see the trees move
using GolfSim.Course;
using GolfSim.Game;
using UnityEngine;

public static class FeelPlay
{
    public static string Start()
    {
        if (!Application.isPlaying) return "enter Play mode first";
        RoundDirector.PlayFromMenu(CourseCatalog.Find("parkland"), 1, SkyChoice.Day);
        return "round starting";
    }

    public static string Gale()
    {
        FoliageWind.Apply(70f, 22f);
        var zone = Object.FindAnyObjectByType<WindZone>();
        return $"wind zone {(zone ? zone.windMain.ToString("0.00") : "none")}, _GolfWind {Shader.GetGlobalVector("_GolfWind")}";
    }

    public static string Splash()
    {
        var cam = Camera.main.transform;
        var ahead = cam.position + Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized * 35f;
        ahead.y = Terrain.activeTerrain ? Terrain.activeTerrain.SampleHeight(ahead) + Terrain.activeTerrain.transform.position.y : ahead.y - 2f;
        WaterSplash.Play(ahead);
        return $"splash at {ahead}";
    }
}
