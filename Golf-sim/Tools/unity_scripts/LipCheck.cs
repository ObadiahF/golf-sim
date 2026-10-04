// Dev helper, run from the shell with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/LipCheck.cs --entry LipCheck.Run
// Putts at the hole on a flat green (a throwaway terrain), passing the cup centre at several sideways offsets and
// speeds, simulated with PuttPredictor (the same BallPhysics.Cup steps as the ball): which drop, which lip out or
// hop the hole, and which stop short. Expected: dead centre drops up to ~1.9 m/s at the hole, 3 cm off ~1.1 m/s,
// 5 cm off (the edge) only dying putts, and slow putts hanging over the lip (6-7 cm off) are pulled in (LipPull).
using System.Text;
using GolfSim.Ball;
using UnityEngine;

public static class LipCheck
{
    const float Approach = 1f; // m from the start to the cup

    public static string Run()
    {
        var settings = BallPhysicsSettings.Defaults;
        var data = new TerrainData { heightmapResolution = 33, alphamapResolution = 16 };
        data.size = new Vector3(40f, 10f, 40f);
        data.terrainLayers = new[] { new TerrainLayer { name = "green" } };
        var go = Terrain.CreateTerrainGameObject(data);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.transform.position = new Vector3(5000f, -500f, 5000f); // far from any hole
        try
        {
            var map = new TerrainSurfaceMap(go.GetComponent<Terrain>(), null);
            var cup = go.transform.position + new Vector3(20f, 0f, 20f);
            cup.y = map.HeightAt(cup);
            float decel = settings.For("green").rolling * BallPhysicsSettings.Gravity;
            var sb = new StringBuilder("side cm   speed at the cup (m/s): result\n");
            foreach (float side in new[] { 0f, 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f })
            {
                sb.Append($"{side,6:0}  ");
                foreach (float atCup in new[] { 0.1f, 0.3f, 0.6f, 1f, 1.3f, 1.6f, 1.9f, 2.2f, 2.6f })
                {
                    var origin = cup + new Vector3(side / 100f, 0f, -Approach);
                    origin.y = map.HeightAt(origin) + BallPhysicsSettings.Radius;
                    // Rolls Approach metres losing speed to the green, plus a little for the launch hop.
                    var shot = ShotData.FromMph(0f, 0f, 0f, 0f, 0f);
                    shot.club = Clubs.Putter;
                    shot.ballSpeed = Mathf.Sqrt(atCup * atCup + 2f * decel * Approach) * 1.02f;
                    var p = PuttPredictor.Simulate(map, settings, origin, Vector3.forward, shot, cup, step: GolfBall.Step);
                    string end = p.holed ? "IN" : Vector3.Dot(p.end - cup, Vector3.forward) > 0f ? "out" : "short";
                    sb.Append($"  {atCup:0.0}:{end,-5}");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
