// Dev helper, run from the shell with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/BallFlightCheck.cs --entry BallFlightCheck.Run
// Flies each club preset over flat ground with the C# model and prints carry/apex/landing angle,
// to compare against Tools/ball_physics/calibrate.py and TrackMan tour averages.
using System.Text;
using GolfSim.Ball;
using UnityEngine;

public static class BallFlightCheck
{
    const float Yards = 1.0936f;

    public static string Run()
    {
        var p = BallPhysicsSettings.Defaults;
        var sb = new StringBuilder("club      carry yd  apex yd  land deg  hang s\n");
        foreach (var (name, shot) in ShotData.Presets)
        {
            var s = BallPhysics.Launch(Vector3.zero, Vector3.forward, shot);
            float apex = 0f, t = 0f;
            var prev = s;
            while (t < 20f)
            {
                prev = s;
                BallPhysics.Fly(ref s, 0.002f, p, Vector3.zero);
                t += 0.002f;
                apex = Mathf.Max(apex, s.position.y);
                if (s.position.y < 0f && s.velocity.y < 0f) break;
            }
            float f = prev.position.y / (prev.position.y - s.position.y);
            float carry = Mathf.Lerp(prev.position.z, s.position.z, f);
            float land = Mathf.Atan2(-s.velocity.y, s.velocity.z) * Mathf.Rad2Deg;
            sb.AppendLine($"{name,-9} {carry * Yards,8:0.0} {apex * Yards,8:0.0} {land,9:0.0} {t,7:0.00}");
        }
        return sb.ToString();
    }
}
