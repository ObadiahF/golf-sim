// Dev check (Play mode, against a LOCAL game server or none), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/CelebrationPlay.cs --entry CelebrationPlay.Start   (day; StartNight for night)
//   unity command run_script --file Tools/unity_scripts/CelebrationPlay.cs --entry CelebrationPlay.Ace     (or Birdie)
//   unity command capture_game_view --save_path Assets/celebration.png   ... a few times while it plays
// Frames the green from 70 m out (the hole camera is switched off meanwhile) and puts on a celebration there.
using GolfSim.Course;
using GolfSim.Game;
using UnityEngine;

public static class CelebrationPlay
{
    public static string Start() => Begin(SkyChoice.Day);
    public static string StartNight() => Begin(SkyChoice.Night);

    static string Begin(SkyChoice sky)
    {
        if (!Application.isPlaying) return "enter Play mode first";
        RoundDirector.PlayFromMenu(CourseCatalog.Find("parkland"), 1, sky);
        return "round starting: run Ace or Birdie once the hole is up";
    }

    public static string Ace() => Fire(RoundDirector.CelebrationFor("Obi", 1, 3, "tee", 152f, TurnBanner.AccentFor(0)));
    public static string Birdie() => Fire(RoundDirector.CelebrationFor("Sis", 3, 4, "rough", 18f, TurnBanner.AccentFor(1)));

    static string Fire(RoundDirector.Celebration c)
    {
        var d = RoundDirector.Instance;
        var hole = Object.FindAnyObjectByType<HoleInfo>();
        if (!d || !hole) return "no hole up yet";
        var cam = Camera.main;
        foreach (var b in cam.GetComponents<MonoBehaviour>()) b.enabled = false; // the hole camera would move it back
        var pin = hole.PinWorld;
        var back = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
        cam.transform.position = pin - back * 70f + Vector3.up * 12f;
        cam.transform.LookAt(pin + Vector3.up * 18f);
        d.Play(c);
        return $"{c.headline} at {pin} (celebrating {RoundDirector.Celebrating})";
    }
}
