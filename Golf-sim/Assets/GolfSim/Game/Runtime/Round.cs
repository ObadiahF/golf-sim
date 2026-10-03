using System;
using System.Linq;
using GolfSim.Net;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>How a shot ended, for scoring.</summary>
    public enum ShotEnd { Stopped, Holed, Penalty }

    /// <summary>One player's ball on the current hole.</summary>
    public class PlayerBall
    {
        public readonly string player;
        public Vector3 position;
        public string lie = "tee";
        public int strokes;
        public bool holed, pickedUp;

        public PlayerBall(string player, Vector3 tee)
        {
            this.player = player;
            position = tee;
        }

        public bool Done => holed || pickedUp;
        public bool OnTee => lie == "tee";
        public PlayerBall Clone() => (PlayerBall)MemberwiseClone();
    }

    /// <summary>
    /// The scorecard and turn rules of a round, with no scene or network code (the RoundDirector drives it).
    /// Strokes count when a shot ends; water and out of bounds add a penalty stroke and the ball is replayed
    /// from where it was hit (stroke and distance); reaching par + maxOverPar without holing out picks up.
    /// </summary>
    public class Round
    {
        public readonly long gameId;
        public readonly string[] players;
        public readonly int holeCount;
        public readonly TurnOrder order;
        public readonly int maxOverPar;
        /// <summary>Score per player per hole, [player][hole]; 0 = not played.</summary>
        public readonly int[][] scores;
        /// <summary>Par per hole; 0 until the hole is started (or known from the server).</summary>
        public readonly int[] pars;

        public int HoleIndex { get; private set; } = -1;
        public PlayerBall[] Balls { get; private set; } = new PlayerBall[0];
        public int Current { get; private set; } = -1;
        /// <summary>Player indexes in the order they tee off on the current hole (the honor first).</summary>
        public int[] HoleOrder { get; private set; } = new int[0];

        PlayerBall beforeLastShot;
        int lastShooter = -1;

        public Round(long gameId, string[] players, int holeCount, TurnOrder order, int maxOverPar)
        {
            if (players == null || players.Length == 0) throw new ArgumentException("A round needs at least one player");
            this.gameId = gameId;
            this.players = players;
            this.holeCount = Mathf.Max(1, holeCount);
            this.order = order;
            this.maxOverPar = maxOverPar;
            scores = players.Select(_ => new int[this.holeCount]).ToArray();
            pars = new int[this.holeCount];
        }

        /// <summary>A round for a server game, with the scores already recorded (to resume it).</summary>
        public static Round FromGame(GameView game, TurnOrder order, int maxOverPar)
        {
            var round = new Round(game.id, game.players.Select(p => p.name).ToArray(), game.holesCount, order, maxOverPar);
            for (int h = 0; h < round.holeCount; h++)
            {
                if (h < game.pars.Length) round.pars[h] = game.pars[h];
                for (int p = 0; p < round.players.Length; p++)
                    if (h < game.players[p].strokes.Length) round.scores[p][h] = game.players[p].strokes[h];
            }
            return round;
        }

        public bool InServerGame => gameId > 0;
        public int HoleNumber => HoleIndex + 1;
        public int Par => pars[HoleIndex];
        public int StrokeCap => Par + maxOverPar;
        public PlayerBall CurrentBall => Current >= 0 ? Balls[Current] : null;
        public bool HoleComplete => Balls.All(b => b.Done);
        public bool IsLastHole => HoleIndex >= holeCount - 1;
        public bool CanMulligan => beforeLastShot != null;

        /// <summary>First hole some player hasn't finished (holeCount when the round is complete).</summary>
        public int FirstUnfinishedHole()
        {
            for (int h = 0; h < holeCount; h++)
                if (scores.Any(s => s[h] == 0)) return h;
            return holeCount;
        }

        /// <summary>
        /// Who tees off first on a hole, then second...: the honor. Hole 1 goes in player order; after that the lowest
        /// score on the hole before goes first, ties keeping the order they teed off in (golf's rule).
        /// </summary>
        public int[] TeeOrder(int hole)
        {
            var order = Enumerable.Range(0, players.Length).ToArray();
            for (int h = 0; h < hole && h < holeCount; h++)
            {
                int played = h;
                if (scores.Any(s => s[played] == 0)) continue; // not finished by everyone: no new honor
                order = order.OrderBy(p => scores[p][played]).ToArray(); // stable: ties keep their order
            }
            return order;
        }

        /// <summary>Starts (or restarts) a hole: everyone on the tee, the hole's old scores cleared.</summary>
        public void StartHole(int index, int par, Vector3 tee)
        {
            HoleIndex = Mathf.Clamp(index, 0, holeCount - 1);
            pars[HoleIndex] = par;
            foreach (var s in scores) s[HoleIndex] = 0;
            HoleOrder = TeeOrder(HoleIndex);
            Balls = players.Select(p => new PlayerBall(p, tee)).ToArray();
            Current = -1;
            beforeLastShot = null;
        }

        /// <summary>Picks who plays next under the turn order; false when everyone has finished the hole.</summary>
        public bool NextTurn(Vector3 pin)
        {
            if (HoleComplete)
            {
                Current = -1;
                return false;
            }
            switch (order)
            {
                case TurnOrder.WholeHole:
                    if (Current < 0 || Balls[Current].Done) Current = NextInOrder(-1, b => !b.Done);
                    return true;
                case TurnOrder.Alternate:
                    Current = NextInOrder(Current, b => !b.Done);
                    return true;
            }
            int teeing = NextInOrder(-1, b => !b.Done && b.strokes == 0);
            if (teeing >= 0)
            {
                Current = teeing;
                return true;
            }
            float farthest = -1f;
            for (int i = 0; i < Balls.Length; i++)
            {
                if (Balls[i].Done) continue;
                float d = FlatDistance(Balls[i].position, pin);
                if (d > farthest)
                {
                    farthest = d;
                    Current = i;
                }
            }
            return true;
        }

        /// <summary>The first player after `player` in tee order (wrapping round; -1 = from the honor) whose ball matches; -1 if none.</summary>
        int NextInOrder(int player, Func<PlayerBall, bool> match)
        {
            int n = HoleOrder.Length, at = player < 0 ? -1 : Array.IndexOf(HoleOrder, player);
            for (int step = 1; step <= n; step++)
            {
                int next = HoleOrder[((at + step) % n + n) % n];
                if (match(Balls[next])) return next;
            }
            return -1;
        }

        /// <summary>Scores the current player's shot. Returns the player's ball afterwards.</summary>
        public PlayerBall RecordShot(ShotEnd end, Vector3 rest, string lie)
        {
            var ball = CurrentBall;
            beforeLastShot = ball.Clone();
            lastShooter = Current;
            ball.strokes++;
            switch (end)
            {
                case ShotEnd.Holed:
                    ball.holed = true;
                    ball.position = rest;
                    ball.lie = "holed";
                    break;
                case ShotEnd.Penalty:
                    ball.strokes++; // replay from where it was hit
                    break;
                default:
                    ball.position = rest;
                    ball.lie = string.IsNullOrEmpty(lie) ? "rough" : lie;
                    break;
            }
            if (!ball.holed && ball.strokes >= StrokeCap) PickUp(ball);
            if (ball.Done) scores[Current][HoleIndex] = ball.strokes;
            return ball;
        }

        /// <summary>The current player picks up: scores par + maxOverPar for the hole.</summary>
        public PlayerBall PickUpCurrent()
        {
            var ball = CurrentBall;
            beforeLastShot = null;
            PickUp(ball);
            scores[Current][HoleIndex] = ball.strokes;
            return ball;
        }

        void PickUp(PlayerBall ball)
        {
            ball.pickedUp = true;
            ball.strokes = StrokeCap;
        }

        /// <summary>Takes back the last shot: that player's ball and strokes as before it, and it's their turn.</summary>
        public bool Mulligan()
        {
            if (beforeLastShot == null) return false;
            Balls[lastShooter] = beforeLastShot;
            scores[lastShooter][HoleIndex] = 0;
            Current = lastShooter;
            beforeLastShot = null;
            return true;
        }

        public int Total(int player) => scores[player].Sum();

        /// <summary>Total minus par over the holes this player has finished.</summary>
        public int ToPar(int player)
        {
            int toPar = 0;
            for (int h = 0; h < holeCount; h++)
                if (scores[player][h] > 0) toPar += scores[player][h] - pars[h];
            return toPar;
        }

        /// <summary>Players with the lowest total.</summary>
        public string[] Leaders()
        {
            int best = Enumerable.Range(0, players.Length).Min(Total);
            return Enumerable.Range(0, players.Length).Where(p => Total(p) == best).Select(p => players[p]).ToArray();
        }

        public static float FlatDistance(Vector3 a, Vector3 b) => Vector3.ProjectOnPlane(b - a, Vector3.up).magnitude;
    }
}
