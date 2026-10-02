// Dev helper, run from the shell with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/AimCheck.cs --entry AimCheck.Run
// Flies shots over flat ground at several aim offsets (as GolfBall.Hit launches them: the pin line turned by
// aimOffset) and prints where each lands relative to the aim, so "the ball goes where you aim" can be checked.
using System.Text;
using GolfSim.Ball;
using UnityEngine;

public static class AimCheck
{
    public static string Run()
    {
        var p = BallPhysicsSettings.Defaults;
        var sb = new StringBuilder("shot                 aim°  landed°  off-aim°  carry yd\n");
        var driver = Clubs.Bag[0].shot;
        var shots = new (string name, float dir, float side)[]
        {
            ("straight", 0f, 0f),
            ("phone face +15 max", 11.25f, 2700f), // what the Windows log showed for every drive
        };
        foreach (var (name, dir, side) in shots)
            foreach (float aimOffset in new[] { -30f, -10f, 0f, 10f, 30f })
            {
                var shot = driver;
                shot.launchDirection = dir;
                shot.sidespin = side;
                var aim = Quaternion.AngleAxis(aimOffset, Vector3.up) * Vector3.forward; // pin straight ahead
                var s = BallPhysics.Launch(Vector3.zero, aim, shot);
                var prev = s;
                for (float t = 0f; t < 20f; t += 0.002f)
                {
                    prev = s;
                    BallPhysics.Fly(ref s, 0.002f, p, Vector3.zero);
                    if (s.position.y < 0f && s.velocity.y < 0f) break;
                }
                var land = Vector3.Lerp(prev.position, s.position, prev.position.y / (prev.position.y - s.position.y));
                float bearing = Mathf.Atan2(land.x, land.z) * Mathf.Rad2Deg; // + right of the pin line
                float carry = new Vector2(land.x, land.z).magnitude * 1.0936f;
                sb.AppendLine($"{name,-20} {aimOffset,5:0} {bearing,8:0.0} {bearing - aimOffset,9:0.0} {carry,9:0}");
            }
        return sb.ToString();
    }
}
