// Dev check (Play mode, a hole being played: a round or practice), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/CourseMapCheck.cs --entry CourseMapCheck.Open
// Drives the course map the way the phone does (a "map" message through SimConnection's own dispatch) and checks it:
//   Open        "map {show:true}": the map opens, state.mapOpen is true; saves the hole picture to Out
//   AimLeft     Left x10 (as the phone's aim buttons): the map's aim point follows the ball's aim
//   AimRight    Right x20 (ends 10° right)
//   Club        Down (the next club): the carry on the map follows the club
//   Hit         hits the suggested club: the map closes the moment the ball leaves and state.mapOpen is false
//   Close       "map {show:false}"
//   All         Open, AimLeft, AimRight, Club, Hit in one go
//   Pair        starts a two-player round (everyone tees off first), to see the other player's ball on the map
// Screenshots of the TV: `unity command capture_game_view save_path=<file>` between the steps (the Editor in front).
using System.IO;
using System.Reflection;
using System.Text;
using GolfSim.Ball;
using GolfSim.Game;
using GolfSim.Net;
using UnityEngine;

public static class CourseMapCheck
{
    const string Out = "/Users/obadiah/.claude/jobs/5fd17b43/tmp/map/";
    const float Frame = 0.02f;

    static RoundDirector D => RoundDirector.Instance;

    public static string All()
    {
        var sb = new StringBuilder();
        foreach (var step in new System.Func<string>[] { Open, AimLeft, AimRight, Club, Hit }) sb.AppendLine(step());
        return sb.ToString();
    }

    public static string Pair()
    {
        var round = new Round(0, new[] { "Ann", "Bob" }, 1, TurnOrder.FarthestFirst, D.course.maxOverPar);
        typeof(RoundDirector).GetMethod("StartRound", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(D, new object[] { round });
        return "two-player round starting: Hit (Ann tees off), then Open";
    }

    public static string Open()
    {
        if (!D || !D.Ball || D.Map == null) return "FAIL no hole with a ball and a HUD (enter Play mode on a hole)";
        Remote("{\"type\":\"map\",\"show\":true}");
        var map = D.Map;
        bool ok = D.MapOpen && map.IsShown && D.BuildState().mapOpen && map.Picture;
        if (map.Picture) SavePicture(map.Picture, Out + "picture.png");
        var s = map.Scene;
        return $"{(ok ? "PASS" : "FAIL")} open: mapOpen {D.BuildState().mapOpen}, picture {(map.Picture ? $"{map.Picture.width}x{map.Picture.height}" : "none")}, " +
               $"frame {map.Frame.size.x:0}x{map.Frame.size.y:0} m, to pin {s?.ToPin * ShotData.YardsPerMeter:0} yd, {Where("ball", s?.ball.position)}, {Where("pin", s?.hole.PinWorld)}";
    }

    public static string AimLeft() => Turn("left", 10, -10f);
    public static string AimRight() => Turn("right", 20, 20f);

    /// <summary>`presses` of Left or Right: the aim, and the map's aim point, turn by `expected` degrees.</summary>
    static string Turn(string key, int presses, float expected)
    {
        var before = D.Map.Scene;
        float aim0 = D.Ball.aimOffset;
        var point0 = before.AimPoint;
        for (int i = 0; i < presses; i++) Remote($"{{\"type\":\"nav\",\"key\":\"{key}\"}}");
        var after = D.Map.Scene;
        float turned = Vector3.SignedAngle(point0 - before.ball.position, after.AimPoint - after.ball.position, Vector3.up);
        bool ok = D.MapOpen && Mathf.Abs(turned - expected) < 0.5f && Mathf.Abs(after.aim - (aim0 + expected)) < 0.01f &&
                  Vector3.Angle(after.aimDirection, D.Ball.AimDirection) < 0.01f;
        return $"{(ok ? "PASS" : "FAIL")} aim {key} x{presses}: aim {aim0:0.#} -> {after.aim:0.#}°, the map's aim point turned {turned:0.#}° " +
               $"(expected {expected:0.#}), {Where("aim point", after.AimPoint)}";
    }

    public static string Club()
    {
        string club0 = D.Club;
        float carry0 = D.Map.Scene.carry;
        Remote("{\"type\":\"nav\",\"key\":\"down\"}");
        var s = D.Map.Scene;
        bool ok = D.MapOpen && s.club == D.Club && Mathf.Abs(s.carry - Clubs.Find(D.Club).carryYards / ShotData.YardsPerMeter) < 0.01f;
        return $"{(ok ? "PASS" : "FAIL")} club: {club0} {carry0 * ShotData.YardsPerMeter:0} yd -> {s.club} {s.carry * ShotData.YardsPerMeter:0} yd on the map";
    }

    public static string Hit()
    {
        var ball = D.Ball;
        var c = Clubs.Find(D.Club);
        var msg = new RemoteShotMessage
        {
            type = "shot", id = Random.Range(1, int.MaxValue), club = c.name, speed = c.shot.ballSpeed, launch = c.shot.launchAngle,
            azimuth = 0f, back = c.shot.backspin, side = c.shot.sidespin,
        };
        var ack = Shots.Submit(msg, "CourseMapCheck", out _);
        if (ack.status != "ok") return $"FAIL not hit: {ack.status} {ack.message}";
        bool closed = !D.MapOpen && !D.Map.IsShown && !D.BuildState().mapOpen;
        for (int i = 0; i < 6000 && ball.InMotion; i++) ball.Advance(Frame);
        D.RunPending();
        bool stays = !D.MapOpen;
        return $"{(closed && stays ? "PASS" : "FAIL")} hit {c.name}: closed when the ball left {closed}, still closed at rest {stays} " +
               $"({ball.Status}, carry {ball.Result.carry * ShotData.YardsPerMeter:0} yd)";
    }

    public static string Close()
    {
        Remote("{\"type\":\"map\",\"show\":false}");
        return $"{(!D.MapOpen && !D.Map.IsShown ? "PASS" : "FAIL")} close: mapOpen {D.BuildState().mapOpen}";
    }

    /// <summary>A message as if the server relayed it from a phone (SimConnection's own parsing and events).</summary>
    static void Remote(string json) =>
        typeof(SimConnection).GetMethod("Dispatch", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(D.Connection, new object[] { json });

    static string Where(string name, Vector3? world)
    {
        if (world == null) return $"{name} -";
        var m = D.Map.Frame.ToMap(world.Value);
        return $"{name} at ({m.x:0.00}, {m.y:0.00}) of the map";
    }

    static void SavePicture(Texture texture, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(texture, rt);
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
        copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        File.WriteAllBytes(path, copy.EncodeToPNG());
        Object.Destroy(copy);
    }
}
