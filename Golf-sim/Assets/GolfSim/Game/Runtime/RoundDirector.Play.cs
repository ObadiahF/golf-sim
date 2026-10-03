using GolfSim.Ball;
using GolfSim.Course;
using GolfSim.Net;
using UnityEngine;

namespace GolfSim.Game
{
    // Hole play: binding to the hole scene's ball, turns, shots, penalties, pick-ups, mulligans, aim and
    // club, and the scorecard between holes. One GolfBall is moved to whoever's turn it is.
    public partial class RoundDirector
    {
        const float AimStep = 1f;   // degrees per Left/Right press
        const float MaxAim = 60f;
        const string PracticePlayer = "Practice";

        GolfBall ball;
        ShotPanel panel;
        HoleInfo hole;
        AimLine aimLine;
        string club = Clubs.Bag[0].name;
        string practiceLie = "tee";
        int turnPlayer = -1; // whose turn was last announced on this hole

        public GolfBall Ball => ball;
        public string Club => club;
        float AimOffset => ball ? ball.aimOffset : 0f;
        float YardsToPin => ball && hole ? Round.FlatDistance(ball.transform.position, hole.PinWorld) * ShotData.YardsPerMeter : 0f;

        void Bind(HoleInfo holeInfo, GolfBall golfBall)
        {
            hole = holeInfo;
            ball = golfBall;
            panel = ball.GetComponent<ShotPanel>();
            aimLine = ball.GetComponent<AimLine>();
            if (!aimLine) aimLine = ball.gameObject.AddComponent<AimLine>();
            // Rounds are played from the phone and the TV: hide the developer overlays (Tab / H bring them back).
            // The fly camera's help sits where the HUD panel is, so it stays hidden whenever there is a HUD.
            if (panel) panel.showPanel = round == null;
            var flyCam = Camera.main ? Camera.main.GetComponent<HoleFlyCamera>() : null;
            if (flyCam) flyCam.showHelp = round == null && hud == null;
            ball.ShotFinished += OnShotFinished;
            ball.Placed += OnBallPlaced;
            ball.ShotStarted += OnShotStarted;
            BindPutting();
        }

        void Unbind()
        {
            UnbindPutting();
            if (ball)
            {
                ball.ShotFinished -= OnShotFinished;
                ball.Placed -= OnBallPlaced;
                ball.ShotStarted -= OnShotStarted;
            }
            ball = null;
            panel = null;
            hole = null;
            aimLine = null;
        }

        void StartPractice()
        {
            phase = Phase.Playing;
            practiceLie = "tee";
            ball.aimOffset = 0f;
            SetClub(Clubs.Suggest(YardsToPin, practiceLie), publish: false);
            StartFacility();
        }

        /// <summary>Practice: the ball was put down (Reset, a mulligan), so the lie is wherever it is now.</summary>
        void OnBallPlaced(GolfBall placed)
        {
            if (round != null) return; // a round's lie is the player's (StartTurn publishes it)
            practiceLie = placed.Lie;
            PublishState();
        }

        /// <summary>The HUD's lie with what it costs the selected club, e.g. "Rough −12%".</summary>
        string LieLabel(string lie) => ball ? ball.Settings.LieFor(lie, Clubs.Find(club).shot.ballSpeed).Label : lie;

        void StartHole(int index)
        {
            pending = null;
            hud?.HideScorecard();
            round.StartHole(index, course.ParFor(hole.par), hole.TeeWorld);
            if (index > 0 && round.players.Length > 1) hud?.Toast($"{round.players[round.HoleOrder[0]]} has the honor");
            turnPlayer = -1;
            BeginNextTurn();
        }

        void BeginNextTurn()
        {
            if (!ball) return;
            if (round.NextTurn(hole.PinWorld)) StartTurn();
            else HoleComplete();
        }

        /// <summary>
        /// Puts the ball at the current player's lie, aims at the pin, suggests a club and tells the phones. When the
        /// player changes (or the hole starts) it is a new turn: "turn" is sent and TurnStarted fires (the banner).
        /// </summary>
        void StartTurn()
        {
            var player = round.CurrentBall;
            if (player.OnTee) ball.ResetToTee();
            else ball.PlaceOnGround(player.position);
            ball.aimOffset = 0f;
            phase = Phase.Playing;
            SetClub(Clubs.Suggest(YardsToPin, player.lie), publish: false);
            if (panel) panel.LineUp();
            PublishState();
            if (round.Current == turnPlayer) return;
            turnPlayer = round.Current;
            var turn = new TurnMessage { player = player.player, hole = round.HoleNumber, strokes = player.strokes };
            connection.Send(turn);
            TurnStarted?.Invoke(turn);
        }

        void OnShotFinished(GolfBall finished)
        {
            var r = finished.Result;
            float carry = r.carry * ShotData.YardsPerMeter, total = r.total * ShotData.YardsPerMeter;
            bool holed = finished.Status == BallStatus.Holed;
            bool penalty = finished.Status is BallStatus.InWater or BallStatus.OutOfBounds;
            string penaltyLie = finished.Status == BallStatus.InWater ? "water" : "ob";
            string penaltyText = finished.Status == BallStatus.InWater ? "Water" : "Out of bounds";

            if (round == null || phase != Phase.Playing)
            {
                // Practice: the next hit starts from the tee after holing out or a penalty (GolfBall.Hit).
                practiceLie = holed || penalty ? "tee" : r.restingSurface;
                SendShotResult(PracticePlayer, carry, total, holed ? "holed" : penalty ? penaltyLie : r.restingSurface, holed, 0);
                if (facility != null && phase == Phase.Playing)
                {
                    FacilityShotFinished(finished);
                    return;
                }
                hud?.Toast(holed ? "In the hole!" : penalty ? penaltyText : $"{carry:0} yd carry");
                PublishState();
                return;
            }

            var end = holed ? ShotEnd.Holed : penalty ? ShotEnd.Penalty : ShotEnd.Stopped;
            var player = round.RecordShot(end, finished.transform.position, r.restingSurface);
            SendShotResult(player.player, carry, total, penalty ? penaltyLie : player.lie, player.holed, player.strokes);
            hud?.Toast(ShotToast(player, end, penaltyText, carry));
            if (player.Done) SendHoleScore(round.Current);
            phase = Phase.BetweenShots;
            PublishState();
            Later(course.turnDelay + (player.holed ? 1f : 0f), BeginNextTurn);
        }

        void HoleComplete()
        {
            phase = round.IsLastHole ? Phase.Finished : Phase.HoleSummary;
            if (phase == Phase.Finished)
            {
                // Winner celebration, then the final scorecard.
                var leaders = round.Leaders();
                string headline = round.players.Length == 1 ? "ROUND COMPLETE" : leaders.Length == 1 ? $"{leaders[0].ToUpperInvariant()} WINS!" : "IT'S A TIE!";
                var color = TurnBanner.AccentFor(System.Array.IndexOf(round.players, leaders[0]));
                var finished = round;
                hud?.Banner.Celebrate(headline, Winner(), color, () =>
                {
                    if (round == finished && phase == Phase.Finished)
                        hud.ShowScorecard(round, "Final scorecard", Winner(), "Press Select to return to the menu");
                });
            }
            else
                hud?.ShowScorecard(round, $"Hole {round.HoleNumber} complete", $"Next: hole {round.HoleNumber + 1} of {round.holeCount}", "Press Select for the next hole");
            PublishState();
        }

        /// <summary>Select on the scorecard: the next hole, or back to the menu after the last one.</summary>
        public void Continue()
        {
            if (phase == Phase.HoleSummary) LoadHole(round.HoleIndex + 1);
            else if (phase == Phase.Finished)
            {
                EndRound();
                LoadScene(course.menuScene, -1);
            }
        }

        /// <summary>Remote "mulligan": takes back the last shot (in practice: back to where it was hit).</summary>
        public void Mulligan()
        {
            if (!ball || ball.InMotion || HomeMenu.IsOpen) return;
            if (round == null)
            {
                if (FacilityAgain()) return;
                ball.PlaceOnGround(ball.LaunchPoint);
                ball.aimOffset = 0f;
                if (panel) panel.LineUp();
                PublishState();
                return;
            }
            if (phase is not (Phase.Playing or Phase.BetweenShots)) return;
            if (!round.Mulligan())
            {
                hud?.Toast("No shot to take back");
                return;
            }
            pending = null;
            hud?.Toast($"Mulligan: {round.CurrentBall.player} replays");
            StartTurn();
        }

        /// <summary>Remote "skip": the current player picks up on this hole.</summary>
        public void PickUp()
        {
            if (round == null || phase != Phase.Playing || !ball || ball.InMotion || HomeMenu.IsOpen) return;
            var player = round.PickUpCurrent();
            SendHoleScore(round.Current);
            hud?.Toast($"{player.player} picks up: {player.strokes}");
            phase = Phase.BetweenShots;
            PublishState();
            Later(1.5f, BeginNextTurn);
        }

        public void Aim(float delta)
        {
            if (!ball || ball.InMotion) return;
            ball.aimOffset = Mathf.Clamp(ball.aimOffset + delta, -MaxAim, MaxAim);
            PublishState();
        }

        public void SetClub(string name, bool publish = true)
        {
            string normalized = Clubs.Normalize(name);
            if (normalized == null)
            {
                Debug.LogWarning($"[RoundDirector] Unknown club '{name}'");
                return;
            }
            club = facility?.Club(normalized) ?? normalized;
            if (panel) panel.SelectClub(club);
            if (aimLine) aimLine.length = AimLength();
            if (publish) PublishState();
        }

        /// <summary>The aim arrow shows the club's carry, but not much past the pin.</summary>
        float AimLength()
        {
            float toPin = YardsToPin / ShotData.YardsPerMeter;
            var c = Clubs.Find(club);
            return c.IsPutter ? toPin : Mathf.Min(c.carryYards / ShotData.YardsPerMeter, toPin + 15f);
        }

        void SendShotResult(string player, float carry, float total, string lie, bool holed, int strokes) =>
            connection.Send(new ShotResultMessage
            {
                player = player, carry = System.Math.Round(carry, 1), total = System.Math.Round(total, 1),
                lie = lie ?? "", holed = holed, strokes = strokes,
            });

        void SendHoleScore(int player)
        {
            int score = round.scores[player][round.HoleIndex];
            Debug.Log($"[RoundDirector] Hole {round.HoleNumber}: {round.players[player]} {score} (par {round.Par})");
            if (!round.InServerGame || !ServerTakesScores(round.gameId)) return;
            connection.Send(new HoleScoreMessage
            {
                gameId = round.gameId, player = round.players[player], hole = round.HoleNumber, par = round.Par, strokes = score,
            }, reliable: true);
        }

        string ShotToast(PlayerBall player, ShotEnd end, string penaltyText, float carry)
        {
            if (player.holed)
                return player.strokes == 1 ? $"{player.player}: HOLE IN ONE!" : $"{player.player} holes out: {player.strokes} ({ScoreName(player.strokes - round.Par)})";
            if (player.pickedUp) return $"{player.player} picks up: {player.strokes}";
            if (end == ShotEnd.Penalty) return $"{penaltyText}: penalty stroke, replay the shot";
            return $"{carry:0} yd carry · {Capitalized(player.lie)}";
        }

        string Winner()
        {
            var leaders = round.Leaders();
            int p = System.Array.IndexOf(round.players, leaders[0]);
            string score = $"{round.Total(p)} ({RoundHud.ToPar(round.ToPar(p))})";
            if (round.players.Length == 1) return $"{leaders[0]}: {score}";
            return leaders.Length == 1 ? $"{leaders[0]} wins with {score}" : $"Tie at {score}: {string.Join(", ", leaders)}";
        }

        static string ScoreName(int toPar) => toPar switch
        {
            <= -3 => "albatross",
            -2 => "eagle",
            -1 => "birdie",
            0 => "par",
            1 => "bogey",
            2 => "double bogey",
            _ => $"+{toPar}",
        };

        static string Capitalized(string s) => string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
