// Dev check (Play mode on a hole: a round or practice), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/MiniMapCheck.cs --entry MiniMapCheck.Pair
// Checks the mini map in the HUD's corner:
//   Pair    starts a two-player round (one shot each), wait for the hole to load, then the rest
//   Shown   the mini map is up with the hole's picture, the aim line and the player up
//   Fly     hits the suggested club and flies the ball frame by frame: the map follows it, no aim line, a trail
//   Next    the next turn: the other player is up, the trail is gone, the aim line is back
//   MapOn   the course map opens: the mini map steps aside; MapOff brings it back
// Screenshots of the TV: `unity command capture_game_view save_path=<file>` between the steps (the Editor in front).
using System.Reflection;
using GolfSim.Ball;
using GolfSim.Game;
using GolfSim.Net;
using UnityEngine;

public static class MiniMapCheck
{
    const float Frame = 0.02f;

    static RoundDirector D => RoundDirector.Instance;
    static MiniMap M => D.MiniMap;

    public static string Pair()
    {
        var round = new Round(0, new[] { "Ann", "Bob" }, 2, TurnOrder.Alternate, D.course.maxOverPar);
        Private("StartRound", round);
        return "two-player round starting: run Shown once the hole is up";
    }

    public static string Shown()
    {
        if (!D || !D.Ball || M == null) return "FAIL no hole with a ball and a HUD";
        var s = M.Scene;
        bool ok = M.IsShown && s != null && s.aiming && s.hole;
        var picture = typeof(MiniMap).GetField("picture", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(M) as HolePicture;
        return $"{(ok ? "PASS" : "FAIL")} shown {M.IsShown}: player '{s?.ball.name}', aiming {s?.aiming}, " +
               $"picture {(picture?.Texture ? $"{picture.Texture.width}x{picture.Texture.height}" : "none")}, to pin {s?.ToPin * ShotData.YardsPerMeter:0} yd";
    }

    public static string Fly()
    {
        var ball = D.Ball;
        var c = Clubs.Find(D.Club);
        string player = M.Scene.ball.name;
        var start = ball.transform.position;
        var msg = new RemoteShotMessage
        {
            type = "shot", id = Random.Range(1, int.MaxValue), club = c.name, speed = c.shot.ballSpeed, launch = c.shot.launchAngle,
            azimuth = 0f, back = c.shot.backspin, side = c.shot.sidespin,
        };
        var ack = Shots.Submit(msg, "MiniMapCheck", out _);
        if (ack.status != "ok") return $"FAIL not hit: {ack.status} {ack.message}";
        int frames = 0, midTrail = 0;
        bool aimedInFlight = false;
        for (; frames < 6000 && ball.InMotion; frames++)
        {
            ball.Advance(Frame);
            Private("TrackMiniMap");
            if (frames == 100) midTrail = M.Trail.Count;
            aimedInFlight |= M.Scene.aiming;
        }
        Private("PublishState", false);
        float followed = Vector3.Distance(M.Scene.ball.position, ball.transform.position);
        float flown = Round.FlatDistance(start, ball.transform.position);
        bool ok = M.IsShown && !aimedInFlight && midTrail > 3 && M.Trail.Count > midTrail && followed < 0.5f && M.Scene.ball.name == player;
        return $"{(ok ? "PASS" : "FAIL")} fly {c.name} {flown * ShotData.YardsPerMeter:0} yd in {frames} frames: trail {midTrail} points at frame 100, " +
               $"{M.Trail.Count} at rest, aim line in flight {aimedInFlight}, map ball {followed:0.00} m from the ball";
    }

    public static string Next()
    {
        string before = M.Scene.ball.name;
        D.RunPending();
        var s = M.Scene;
        bool ok = s.ball.name != before && M.Trail.Count == 0 && s.aiming && M.IsShown;
        return $"{(ok ? "PASS" : "FAIL")} next: {before} -> {s.ball.name}, trail {M.Trail.Count}, aiming {s.aiming}";
    }

    public static string MapOn()
    {
        D.ShowMap(true);
        bool ok = D.MapOpen && !M.IsShown;
        return $"{(ok ? "PASS" : "FAIL")} course map open {D.MapOpen}, mini map shown {M.IsShown}";
    }

    public static string MapOff()
    {
        D.ShowMap(false);
        bool ok = !D.MapOpen && M.IsShown;
        return $"{(ok ? "PASS" : "FAIL")} course map open {D.MapOpen}, mini map shown {M.IsShown}";
    }

    static object Private(string method, params object[] args) =>
        typeof(RoundDirector).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(D, args);
}
