// Dev checks for the wind (not compiled into the project), run with the Unity CLI:
//   unity command run_script --file Tools/unity_scripts/WindCheck.cs --entry WindCheck.<Entry>
//   Physics  (Edit or Play mode) carries of a driver and a 7 iron into, with and across a 10 and a 20 mph wind on a flat
//            plane: into it is shorter, helping longer, a crosswind blows it sideways the way it blows
//   Odds     (Edit or Play mode) 10000 holes' winds: the bands (calm, light, moderate, strong) and the strongest
//   Round    (Play mode, a round or the practice hole) the wind the shot panel, the ball and the flags use is the
//            director's, "state" carries it against the aim, and a shot through the remote path flies in it
using System.Linq;
using System.Reflection;
using System.Text;
using GolfSim.Ball;
using GolfSim.Course;
using GolfSim.Game;
using UnityEngine;

public static class WindCheck
{
    const float Step = 0.002f;

    public static string Physics()
    {
        var settings = ScriptableObject.CreateInstance<BallPhysicsSettings>();
        var sb = new StringBuilder();
        bool ok = true;
        foreach (var club in new[] { "Driver", "7 Iron" })
        {
            var shot = Clubs.Find(club).shot;
            var calm = Carry(settings, shot, Wind.Calm);
            sb.AppendLine($"{club}: calm {Yd(calm.z)} yd");
            foreach (float mph in new[] { 10f, 20f })
            {
                // Hit north (+z): a north wind is in the face, a south wind helping, a west wind blows it right (+x).
                var into = Carry(settings, shot, new Wind(mph, 0f));
                var helping = Carry(settings, shot, new Wind(mph, 180f));
                var cross = Carry(settings, shot, new Wind(mph, 270f));
                bool good = into.z < calm.z && helping.z > calm.z && cross.x > 1f;
                ok &= good;
                sb.AppendLine($"  {(good ? "PASS" : "FAIL")} {mph:0} mph: into {Yd(into.z)} yd ({Yd(into.z - calm.z):+0;-0}), " +
                              $"helping {Yd(helping.z)} yd ({Yd(helping.z - calm.z):+0;-0}), from the left {Yd(cross.x):+0;-0} yd right");
            }
        }
        var relative = new Wind(10f, 270f).RelativeTo(Vector3.forward); // a west wind on a shot north: left to right
        ok &= Mathf.Abs(relative - 90f) < 0.01f;
        sb.AppendLine($"{(Mathf.Abs(relative - 90f) < 0.01f ? "PASS" : "FAIL")} a west wind on a shot north is {relative:0}° (left to right)");
        return (ok ? "ALL PASS\n" : "SOME FAILED\n") + sb;
    }

    public static string Odds()
    {
        var rng = new System.Random(1);
        var winds = Enumerable.Range(0, 10000).Select(_ => Wind.Random(rng)).ToArray();
        int calm = winds.Count(w => w.IsCalm), light = winds.Count(w => !w.IsCalm && w.mph < 8f),
            moderate = winds.Count(w => w.mph >= 8f && w.mph < 15f), strong = winds.Count(w => w.mph >= 15f);
        return $"calm {calm / 100f:0}%, light {light / 100f:0}%, 8-15 mph {moderate / 100f:0}%, 15+ mph {strong / 100f:0}%, " +
               $"strongest {winds.Max(w => w.mph):0} mph, scale 0: {Wind.Random(rng, 0f).mph} mph";
    }

    public static string Round()
    {
        var d = RoundDirector.Instance;
        if (!d || !d.Ball) return "needs Play mode on a hole";
        var ball = d.Ball;
        var panel = ball.GetComponent<ShotPanel>();
        var wind = (Wind)typeof(RoundDirector).GetField("wind", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(d);
        var s = d.BuildState();
        var sb = new StringBuilder();
        bool ok = true;
        void Check(bool good, string what) { ok &= good; sb.AppendLine($"{(good ? "PASS" : "FAIL")} {what}"); }

        Check(s.wind == Mathf.RoundToInt(wind.mph), $"state wind {s.wind} mph, angle {s.windAngle}° (director: {wind.mph:0} mph from {wind.from:0}°)");
        Check(!panel || Mathf.Approximately(panel.Wind.mph, wind.mph) && Mathf.Approximately(panel.Wind.from, wind.from), $"shot panel wind {panel?.Wind.mph:0} mph from {panel?.Wind.from:0}°");
        var flags = Object.FindObjectsByType<FlagWave>();
        Check(flags.Length > 0 && flags.All(f => Mathf.Approximately(f.windHeading, wind.Heading)), $"{flags.Length} flags fly toward {wind.Heading:0}°");

        float aim = ball.aimOffset;
        d.Aim(20f);
        int turned = d.BuildState().windAngle;
        d.Aim(aim - ball.aimOffset);
        Check(wind.IsCalm || Mathf.Abs(Mathf.DeltaAngle(s.windAngle, turned) + 20f) < 1.5f, $"aiming 20° right turns the wind 20° left against the aim: {s.windAngle}° -> {turned}°");

        if (!ball.InMotion && Shots.Blocked == null)
        {
            var shot = Clubs.Find(d.Club).shot;
            var msg = new RemoteShotMessage { type = "shot", id = Random.Range(1, 1 << 30), club = d.Club, speed = shot.ballSpeed, launch = shot.launchAngle, back = shot.backspin };
            var ack = Shots.Submit(msg, "WindCheck", out _);
            Check(ack.status == "ok" && Mathf.Approximately(ball.windSpeed, wind.MetersPerSecond) && Mathf.Approximately(ball.windHeading, wind.Heading),
                  $"a remote shot ({ack.status}) flies in {ball.windSpeed:0.0} m/s toward {ball.windHeading:0}°");
            for (int i = 0; i < 50000 && ball.InMotion; i++) ball.Advance(0.02f);
        }
        return (ok ? "ALL PASS\n" : "SOME FAILED\n") + sb;
    }

    /// <summary>Where a shot north from the origin first comes down on flat ground at y = 0 in this wind.</summary>
    static Vector3 Carry(BallPhysicsSettings settings, ShotData shot, Wind wind)
    {
        var s = BallPhysics.Launch(Vector3.zero, Vector3.forward, shot);
        for (float t = 0f; t < 20f; t += Step)
        {
            BallPhysics.Fly(ref s, Step, settings, wind.Velocity);
            if (s.position.y <= 0f && s.velocity.y < 0f) break;
        }
        return s.position;
    }

    static float Yd(float metres) => metres * ShotData.YardsPerMeter;
}
