// Dev check (Edit or Play mode, no scene needed), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/TurnOrderCheck.cs --entry TurnOrderCheck.All
// Plays Round's turn rules with made-up shots and checks who is up:
//   Alternate   one shot each in tee order; a player who holes out is skipped
//   Honor       the next hole tees off lowest score first, ties keeping their order
//   Resume      a resumed game (scores already in) works out the honor from the holes played
//   WholeHole   the old order still plays each hole through, honor first
using System.Linq;
using System.Text;
using GolfSim.Game;
using UnityEngine;

public static class TurnOrderCheck
{
    static readonly Vector3 Tee = Vector3.zero, Pin = new Vector3(0f, 0f, 300f);

    public static string All()
    {
        var sb = new StringBuilder();
        foreach (var step in new System.Func<string>[] { Alternate, Honor, Resume, WholeHole }) sb.AppendLine(step());
        return sb.ToString();
    }

    public static string Alternate()
    {
        var round = new Round(0, new[] { "Ann", "Bob", "Cat" }, 2, TurnOrder.Alternate, 5);
        round.StartHole(0, 4, Tee);
        var who = new StringBuilder();
        // Ann, Bob, Cat tee off; Ann, then Bob holes out; Cat, Ann, Cat...
        string[] expected = { "Ann", "Bob", "Cat", "Ann", "Bob", "Cat", "Ann", "Cat" };
        foreach (var name in expected)
        {
            round.NextTurn(Pin);
            who.Append(round.CurrentBall.player).Append(' ');
            if (round.CurrentBall.player != name) return $"FAIL alternate: expected {name}, got {who}";
            bool bobHoles = name == "Bob" && round.CurrentBall.strokes == 1;
            round.RecordShot(bobHoles ? ShotEnd.Holed : ShotEnd.Stopped, Pin * 0.5f, "fairway");
        }
        return $"PASS alternate: {who}";
    }

    public static string Honor()
    {
        var round = new Round(0, new[] { "Ann", "Bob", "Cat", "Dan" }, 3, TurnOrder.Alternate, 5);
        PlayHole(round, 0, new[] { 5, 3, 4, 3 }); // Bob and Dan tie on 3: Bob teed off first, keeps it
        string hole2 = Names(round, round.TeeOrder(1));
        round.StartHole(1, 4, Tee);
        round.NextTurn(Pin);
        bool ok = hole2 == "Bob Dan Cat Ann" && round.CurrentBall.player == "Bob";
        PlayHole(round, 1, new[] { 4, 5, 2, 4 });
        string hole3 = Names(round, round.TeeOrder(2)); // Cat; then Ann and Dan on 4 in hole-2 order (Dan, Ann); Bob
        ok &= hole3 == "Cat Dan Ann Bob";
        return $"{(ok ? "PASS" : "FAIL")} honor: hole 2 {hole2}, hole 3 {hole3}";
    }

    public static string Resume()
    {
        var round = new Round(7, new[] { "Ann", "Bob" }, 3, TurnOrder.Alternate, 5);
        round.scores[0][0] = 6;
        round.scores[1][0] = 4;
        round.StartHole(round.FirstUnfinishedHole(), 4, Tee);
        round.NextTurn(Pin);
        bool ok = round.HoleIndex == 1 && round.CurrentBall.player == "Bob";
        return $"{(ok ? "PASS" : "FAIL")} resume on hole {round.HoleNumber}: {round.CurrentBall.player} tees off";
    }

    public static string WholeHole()
    {
        var round = new Round(0, new[] { "Ann", "Bob" }, 2, TurnOrder.WholeHole, 5);
        PlayHole(round, 0, new[] { 5, 4 });
        round.StartHole(1, 4, Tee);
        var who = new StringBuilder();
        for (int shot = 0; shot < 4; shot++)
        {
            round.NextTurn(Pin);
            who.Append(round.CurrentBall.player).Append(' ');
            round.RecordShot(round.CurrentBall.strokes == 1 ? ShotEnd.Holed : ShotEnd.Stopped, Pin * 0.5f, "fairway");
        }
        bool ok = who.ToString() == "Bob Bob Ann Ann ";
        return $"{(ok ? "PASS" : "FAIL")} whole hole: {who}";
    }

    /// <summary>Plays a hole to these scores (in player order), whoever is up hitting until they hole out on their score.</summary>
    static void PlayHole(Round round, int hole, int[] strokes)
    {
        round.StartHole(hole, 4, Tee);
        while (round.NextTurn(Pin))
        {
            var ball = round.CurrentBall;
            int target = strokes[System.Array.IndexOf(round.players, ball.player)];
            round.RecordShot(ball.strokes + 1 == target ? ShotEnd.Holed : ShotEnd.Stopped, Pin * 0.5f, "fairway");
        }
    }

    static string Names(Round round, int[] order) => string.Join(" ", order.Select(p => round.players[p]));
}
