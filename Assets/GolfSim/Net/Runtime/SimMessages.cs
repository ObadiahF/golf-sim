using System;
using UnityEngine;

namespace GolfSim.Net
{
    // Every game-server WebSocket message the sim sends or reads: JSON text frames {"type": ..., ...}.
    // The server's WsMessage.java / docs/PROTOCOL.md is the source of truth. Phone shots arrive as
    // "shot" with the same fields as the UDP datagram, so they are parsed as GolfSim.Ball.RemoteShotMessage.
    // JsonUtility writes every field and reads JSON null numbers as 0 (strokes are never 0, so 0 = not played).

    /// <summary>The "type" values.</summary>
    public static class MessageType
    {
        // any client
        public const string Ping = "ping", Pong = "pong";
        // remote -> sim (relayed by the server)
        public const string Nav = "nav", Club = "club", Aim = "aim", AimReset = "aimReset", Shot = "shot", Mulligan = "mulligan", Skip = "skip";
        // sim -> remotes
        public const string State = "state", ShotResult = "shotResult", Turn = "turn";
        // sim -> server
        public const string HoleScore = "holeScore";
        // server -> clients
        public const string Hello = "hello", SimStatus = "simStatus", GameStarted = "gameStarted", Scorecard = "scorecard",
            GameFinished = "gameFinished", Error = "error";
    }

    /// <summary>Base of every message; also used to peek at the type of an incoming frame.</summary>
    [Serializable]
    public class SimMessage
    {
        public string type;

        public SimMessage() { }
        public SimMessage(string type) => this.type = type;

        public string ToJson() => JsonUtility.ToJson(this);
    }

    // ---- remote -> sim ----

    /// <summary>key: up, down, left, right, select or back.</summary>
    [Serializable] public class NavMessage : SimMessage { public string key; }

    [Serializable] public class ClubMessage : SimMessage { public string club; }

    /// <summary>delta: degrees to turn the aim, + right.</summary>
    [Serializable] public class AimMessage : SimMessage { public float delta; }

    // ---- sim -> remotes ----

    /// <summary>What the sim shows, so the app can switch between remote (menu) and gameplay mode.</summary>
    [Serializable]
    public class StateMessage : SimMessage
    {
        public const string Menu = "menu", Loading = "loading", Game = "game", Paused = "paused",
            HoleComplete = "holeComplete", Results = "results";

        /// <summary>menu, loading, game, paused, holeComplete (scorecard between holes) or results (final scorecard).</summary>
        public string screen;
        /// <summary>The server game being played; 0 for practice.</summary>
        public long gameId;
        public string currentPlayer;
        public int hole;
        public int par;
        /// <summary>Strokes the current player has taken on this hole.</summary>
        public int strokes;
        public string club;
        /// <summary>Degrees right (+) or left (-) of the pin.</summary>
        public float aim;
        /// <summary>Yards.</summary>
        public float distanceToPin;
        public string lie;

        public StateMessage() : base(MessageType.State) { }
    }

    [Serializable]
    public class ShotResultMessage : SimMessage
    {
        public string player;
        /// <summary>Yards.</summary>
        public double carry, total;
        public string lie;
        public bool holed;
        /// <summary>The player's strokes on this hole after the shot, penalties included.</summary>
        public int strokes;

        public ShotResultMessage() : base(MessageType.ShotResult) { }
    }

    [Serializable]
    public class TurnMessage : SimMessage
    {
        public string player;
        public int hole;
        public int strokes;

        public TurnMessage() : base(MessageType.Turn) { }
    }

    // ---- sim -> server ----

    /// <summary>One player's score on one hole; the server stores it and broadcasts the scorecard.</summary>
    [Serializable]
    public class HoleScoreMessage : SimMessage
    {
        public long gameId;
        public string player;
        public int hole;
        public int par;
        public int strokes;

        public HoleScoreMessage() : base(MessageType.HoleScore) { }
    }

    // ---- server -> clients ----

    /// <summary>First message on every connection.</summary>
    [Serializable]
    public class HelloMessage : SimMessage
    {
        public string role;
        public bool simConnected;
        public string[] remotes = new string[0];
        /// <summary>The game in progress (id 0 when there is none).</summary>
        public GameView game;
    }

    /// <summary>gameStarted, scorecard and gameFinished.</summary>
    [Serializable] public class GameMessage : SimMessage { public GameView game; }

    [Serializable] public class ErrorMessage : SimMessage { public string message; }

    /// <summary>A game's scorecard, as the server sends it (REST and WebSocket).</summary>
    [Serializable]
    public class GameView
    {
        public const string InProgress = "IN_PROGRESS", Finished = "FINISHED", Abandoned = "ABANDONED";

        public long id;
        /// <summary>IN_PROGRESS, FINISHED or ABANDONED.</summary>
        public string status;
        public int holesCount;
        public string courseName;
        /// <summary>Par per hole (index 0 = hole 1); 0 until a score for that hole is in.</summary>
        public int[] pars = new int[0];
        /// <summary>In turn order.</summary>
        public PlayerCard[] players = new PlayerCard[0];
        public string[] winners = new string[0];

        public bool Exists => id > 0;
        public bool IsInProgress => Exists && status == InProgress;
    }

    [Serializable]
    public class PlayerCard
    {
        public string name;
        public int turnOrder;
        /// <summary>Strokes per hole (index 0 = hole 1); 0 where not played yet.</summary>
        public int[] strokes = new int[0];
        public int holesPlayed;
        public int total;
        public int par;
        public int toPar;
    }
}
