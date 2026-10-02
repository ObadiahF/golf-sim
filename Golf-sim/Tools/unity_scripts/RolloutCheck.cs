// Dev helper, run from the shell with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/RolloutCheck.cs --entry RolloutCheck.Run
// How far shots run out on each surface (flat ground, the project's BallPhysics.asset): club shots landing on a
// surface, a ball rolling into it at a given speed, and the green's Stimp, to tune bounce and rolling friction.
using System.Text;
using GolfSim.Ball;
using UnityEditor;
using UnityEngine;

public static class RolloutCheck
{
    const float Yards = 1.0936f, Step = 0.002f;
    static readonly string[] Surfaces = { "green", "fairway", "rough", "native", "bunker" };

    public static string Run()
    {
        const string path = "Assets/GolfSim/Ball/Settings/BallPhysics.asset";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); // pick up edits made outside Unity
        var settings = AssetDatabase.LoadAssetAtPath<BallPhysicsSettings>(path);
        var sb = new StringBuilder($"Green Stimp {PuttModel.GreenStimp(settings):0.0} ft\n\nRollout after landing (yd), by surface landed on\nclub      carry");
        foreach (var s in Surfaces) sb.Append($" {s,8}");
        sb.AppendLine();
        foreach (var club in Clubs.Bag)
        {
            if (club.IsPutter) continue;
            sb.Append($"{club.name,-9} {Carry(club.shot, settings) * Yards,5:0}");
            foreach (var s in Surfaces) sb.Append($" {Rollout(club.shot, settings.For(s), settings) * Yards,8:0.0}");
            sb.AppendLine();
        }
        sb.AppendLine("\nRoll (m) of a ball already rolling at this speed");
        sb.Append("speed m/s");
        foreach (var s in Surfaces) sb.Append($" {s,8}");
        sb.AppendLine();
        foreach (float v in new[] { 1.83f, 4f, 8f, 15f })
        {
            sb.Append($"{v,9:0.00}");
            foreach (var s in Surfaces) sb.Append($" {RollFrom(v, settings.For(s)),8:0.0}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    static float Carry(ShotData shot, BallPhysicsSettings p)
    {
        var s = BallPhysics.Launch(Vector3.zero, Vector3.forward, shot);
        FlyToGround(ref s, p);
        return s.position.z;
    }

    /// <summary>Lands on a flat surface, bounces and rolls to rest; returns the distance past the first landing.</summary>
    static float Rollout(ShotData shot, BallPhysicsSettings.SurfaceResponse surface, BallPhysicsSettings p)
    {
        var s = BallPhysics.Launch(Vector3.zero, Vector3.forward, shot);
        FlyToGround(ref s, p);
        float landing = s.position.z;
        for (int bounce = 0; bounce < 50; bounce++)
        {
            BallPhysics.Bounce(ref s, Vector3.up, surface);
            if (s.velocity.y < GolfBall.RollSpeed) break;
            FlyToGround(ref s, p);
        }
        s.velocity.y = 0f;
        for (float t = 0f; t < 60f && BallPhysics.Roll(ref s, Step, Vector3.up, surface); t += Step) { }
        return s.position.z - landing;
    }

    static float RollFrom(float speed, BallPhysicsSettings.SurfaceResponse surface)
    {
        var s = new BallState { position = Vector3.zero, velocity = Vector3.forward * speed };
        for (float t = 0f; t < 60f && BallPhysics.Roll(ref s, Step, Vector3.up, surface); t += Step) { }
        return s.position.z;
    }

    static void FlyToGround(ref BallState s, BallPhysicsSettings p)
    {
        var prev = s;
        for (float t = 0f; t < 20f; t += Step)
        {
            prev = s;
            BallPhysics.Fly(ref s, Step, p, Vector3.zero);
            if (s.position.y <= 0f && s.velocity.y < 0f) break;
        }
        float f = prev.position.y / Mathf.Max(1e-6f, prev.position.y - s.position.y);
        s.position = Vector3.Lerp(prev.position, s.position, f);
        s.position.y = 0f;
    }
}
