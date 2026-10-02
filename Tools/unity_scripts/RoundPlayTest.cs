// Dev helper (Play mode), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/RoundPlayTest.cs --entry RoundPlayTest.Status
// Drives a multiplayer round the way the phone would, without needing the Editor to tick: pumps the game
// server connection, hits shots through the same Shots path as WebSocket shots, runs the ball to rest with
// GolfBall.Advance, and runs the director's pending "next turn" step immediately.
//   Status        connection, scene, the "state" the phones get, and the round
//   Shot          the current player hits the suggested club (a computed putt on the green)
//   ShotWild      aims 60° right and hits a driver (out of bounds / penalty path)
//   PlayHole      shots until everyone has holed out or picked up (par + 5), then the scorecard
//   Settle        delivers queued server messages (e.g. a phone's shot), runs the ball to rest, next turn
//   Continue      Select on the scorecard (next hole / back to the menu)
//   Nav           one remote key, e.g. --entry RoundPlayTest.NavBack
//   Solo          starts a solo round as "Play a Round" on the menu does
using System.Text;
using GolfSim.Ball;
using GolfSim.Course;
using GolfSim.Game;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RoundPlayTest
{
    const float Frame = 0.02f;
    const float GreenDecel = 0.55f; // m/s² a putt slows by on a Stimp-11 green (BallPlayTest.Putt)
    static int nextId = 9_000_000;

    static RoundDirector D => RoundDirector.Instance;

    public static string Status()
    {
        if (!D) return "no RoundDirector (enter Play mode)";
        D.Connection.Pump();
        var r = D.Round;
        string round = r == null ? "none" : $"game {r.gameId} hole {r.HoleNumber}/{r.holeCount} par {(r.HoleIndex >= 0 ? r.Par : 0)} " +
                                            $"current {r.CurrentBall?.player ?? "-"} scores {Scores(r)}";
        return $"frame {Time.frameCount} connection {D.Connection.State} scene {SceneManager.GetActiveScene().name}\n" +
               $"state {D.BuildState().ToJson()}\nround {round}";
    }

    public static string Solo()
    {
        D.PlayRound();
        return Status();
    }

    public static string Shot()
    {
        D.Connection.Pump();
        var ball = D.Ball;
        if (!ball) return "no ball in the scene";
        var hole = Object.FindAnyObjectByType<HoleInfo>();
        var club = Clubs.Find(D.Club);
        var shot = club.shot;
        if (club.IsPutter)
        {
            float d = Vector3.ProjectOnPlane(hole.PinWorld - ball.transform.position, Vector3.up).magnitude;
            shot = ShotData.FromMph(0, 1f, 0, 0, 0);
            shot.ballSpeed = Mathf.Sqrt(2f * GreenDecel * d) * 1.05f;
        }
        var msg = new RemoteShotMessage
        {
            type = "shot", id = ++nextId, club = club.name, speed = shot.ballSpeed, launch = shot.launchAngle,
            azimuth = 0f, back = shot.backspin, side = shot.sidespin,
        };
        var ack = Shots.Submit(msg, "RoundPlayTest", out _);
        if (ack.status != "ok") return $"not hit: {ack.status} {ack.message}";
        for (int i = 0; i < 6000 && ball.InMotion; i++) ball.Advance(Frame);
        string outcome = $"{D.Round?.CurrentBall?.player ?? "practice"} {club.name}: {ball.Status}, {ball.Result.restingSurface}, " +
                         $"carry {ball.Result.carry * ShotData.YardsPerMeter:0} yd";
        D.RunPending();
        D.Connection.Pump();
        return outcome;
    }

    public static string ShotWild()
    {
        D.Aim(60f);
        D.SetClub("Driver");
        return Shot();
    }

    public static string Settle()
    {
        D.Connection.Pump();
        var ball = D.Ball;
        if (ball) for (int i = 0; i < 6000 && ball.InMotion; i++) ball.Advance(Frame);
        string outcome = ball ? $"ball {ball.Status}, {ball.Result.restingSurface}, carry {ball.Result.carry * ShotData.YardsPerMeter:0} yd\n" : "";
        D.RunPending();
        D.Connection.Pump();
        return outcome + Status();
    }

    public static string PlayHole()
    {
        var r = D.Round;
        if (r == null) return "no round";
        var log = new StringBuilder();
        for (int i = 0; i < 100 && D.ScreenName == "game"; i++) log.AppendLine(Shot());
        return log + Status();
    }

    public static string Continue()
    {
        D.Continue();
        return Status();
    }

    public static string NavBack() => Nav(NavKey.Back);
    public static string NavSelect() => Nav(NavKey.Select);
    public static string NavLeft() => Nav(NavKey.Left);
    public static string NavDown() => Nav(NavKey.Down);
    public static string NavUp() => Nav(NavKey.Up);
    public static string NavRight() => Nav(NavKey.Right);

    static string Nav(NavKey key) => $"{key} handled {NavInput.Push(key)}\n{Status()}";

    static string Scores(Round r)
    {
        var sb = new StringBuilder();
        for (int p = 0; p < r.players.Length; p++) sb.Append($"{r.players[p]}=[{string.Join(",", r.scores[p])}] ");
        return sb.ToString();
    }
}
