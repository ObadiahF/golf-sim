// Dev helper, run from the shell with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/LipCheck.cs --entry LipCheck.Run
// Slow putts dying at the hole on a flat green, passing the cup centre at several sideways offsets: which drop
// (centre over the cup, or hanging over the lip and pulled in by BallPhysics.LipPull) and which stay out.
using System.Text;
using GolfSim.Ball;
using UnityEngine;

public static class LipCheck
{
    const float Step = 0.002f;

    public static string Run()
    {
        var green = BallPhysicsSettings.CreateInstance<BallPhysicsSettings>().For("green");
        var cup = new Vector3(0f, 0f, 2f);
        var sb = new StringBuilder("side cm   would stop      result\n");
        foreach (float side in new[] { 0f, 3f, 5f, 6f, 7f, 8f })
            foreach (float past in new[] { -0.08f, -0.03f, 0f, 0.05f })
            {
                // Speed that rolls (2 m + past) on this green, from 2 m short of the cup.
                float speed = Mathf.Sqrt(2f * green.rolling * BallPhysicsSettings.Gravity * (2f + past));
                var s = new BallState { position = new Vector3(side / 100f, 0f, 0f), velocity = Vector3.forward * speed };
                string result = "stopped";
                for (float t = 0f; t < 30f; t += Step)
                {
                    if (InCup(s, cup)) { result = "HOLED"; break; }
                    bool moving = BallPhysics.Roll(ref s, Step, Vector3.up, green, BallPhysics.LipPull(s.position, cup));
                    if (!moving) { result = InCup(s, cup) ? "HOLED" : $"stopped {Offset(s, cup) * 100f:0.0} cm from the centre"; break; }
                }
                sb.AppendLine($"{side,6:0}   {past * 100f,+5:0} cm past   {result}");
            }
        return sb.ToString();
    }

    static float Offset(BallState s, Vector3 cup) => Vector3.ProjectOnPlane(s.position - cup, Vector3.up).magnitude;

    static bool InCup(BallState s, Vector3 cup) =>
        Offset(s, cup) <= GolfBall.CupRadius && Vector3.ProjectOnPlane(s.velocity, Vector3.up).magnitude <= GolfBall.CupCaptureSpeed;
}
