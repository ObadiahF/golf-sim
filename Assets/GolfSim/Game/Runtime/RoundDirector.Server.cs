using System.Linq;
using GolfSim.Ball;
using GolfSim.Net;
using UnityEngine;

namespace GolfSim.Game
{
    // The server's game and the sim's round kept in step: a new game replaces the round, a game ended from the app
    // (finished or abandoned) or while the sim was offline stops it, scores only go to a game still in progress, and
    // the phones are told whether a swing would be hit now (state.canShoot) or why a swing was refused (shotRejected).
    public partial class RoundDirector
    {
        /// <summary>From StartRound until the round's first hole has loaded (a menu scene loading meanwhile mustn't end it).</summary>
        bool starting;
        /// <summary>An instant replay is on screen (state screen "replay").</summary>
        bool replaying;

        void OnGameStarted(GameView game)
        {
            serverGame = game;
            Debug.Log($"[RoundDirector] Game {game.id} started: {string.Join(", ", game.players.Select(p => p.name))}, {game.holesCount} holes.");
            StartRound(Round.FromGame(game, course.turnOrder, course.maxOverPar));
        }

        void OnGameFinished(GameView game)
        {
            serverGame = game;
            if (round == null || round.gameId != game.id) return;
            if (game.status == GameView.Abandoned)
            {
                hud?.Toast("Game ended from the app");
                LeaveRound();
                return;
            }
            // FINISHED by the sim's own last score: its winner banner and final scorecard follow as usual.
            if (phase == Phase.Finished || round.FirstUnfinishedHole() >= round.holeCount) return;
            ShowEndedEarly(game);
        }

        /// <summary>Every (re)connect: the hello's game wins. Play it if it isn't the round's, or stop a round whose game is over.</summary>
        void OnHello(HelloMessage hello)
        {
            serverGame = hello.game;
            if (round == null || !round.InServerGame || phase == Phase.Finished) return;
            var game = hello.game;
            if (game != null && game.IsInProgress)
            {
                if (game.id == round.gameId) return; // still ours
                Debug.Log($"[RoundDirector] Game {round.gameId} was replaced by game {game.id} while the sim was offline.");
                hud?.Toast($"Switching to game {game.id}");
                OnGameStarted(game);
                return;
            }
            Debug.Log($"[RoundDirector] Game {round.gameId} ended while the sim was offline.");
            hud?.Toast("The game ended while the sim was offline");
            LeaveRound();
        }

        /// <summary>Back to the menu without the round (cancelling its download if it hasn't started).</summary>
        void LeaveRound()
        {
            if (IsFetching) StopFetching();
            EndRound();
            if (hole) LoadScene(course.menuScene, -1);
            else
            {
                phase = Phase.Menu;
                PublishState();
            }
        }

        /// <summary>Ended as FINISHED from the app mid-round: play stops and the server's card is the final scorecard.</summary>
        void ShowEndedEarly(GameView game)
        {
            Debug.Log($"[RoundDirector] Game {game.id} was finished from the app; stopping the round.");
            if (IsFetching) StopFetching();
            pending = null;
            starting = false;
            round = Round.FromGame(game, course.turnOrder, course.maxOverPar);
            phase = Phase.Finished;
            hud?.Banner.Clear();
            hud?.Toast("Game ended from the app");
            hud?.ShowScorecard(round, "Final scorecard", "The game was ended from the app", "Press Select to return to the menu");
            PublishState();
        }

        /// <summary>Scores only go to the server's game while it is in progress (never into a finished or abandoned one).</summary>
        bool ServerTakesScores(long gameId) => serverGame != null && serverGame.id == gameId && serverGame.IsInProgress;

        /// <summary>Restart Hole would replay a hole that is already on the scorecard (or a finished game): not allowed.</summary>
        bool CanRestartHole() => phase is not (Phase.HoleSummary or Phase.Finished);

        // ---- swings: can the phone shoot now? ----

        void OnReplayPlaying(bool on)
        {
            replaying = on;
            PublishState();
        }

        void OnShotStarted(GolfBall b) => PublishState(); // canShoot: false while the ball moves

        /// <summary>Sets state.canShoot / waitReason: what the phone shows instead of a swing that would be refused.</summary>
        void FillCanShoot(StateMessage s)
        {
            string reason = BlockedReason();
            if (reason == null && ball && ball.InMotion) reason = "Wait for the ball to stop";
            if (reason == null && s.screen != StateMessage.Game) reason = "The sim isn't ready for a shot";
            s.canShoot = reason == null;
            s.waitReason = reason ?? "";
        }

        /// <summary>A phone shot the sim can't hit: tell the phones why (shotRejected) as well as the TV.</summary>
        void RejectShot(RemoteShotMessage shot, string reason)
        {
            hud?.Toast(reason);
            connection.Send(new ShotRejectedMessage { reason = reason, id = shot.id });
        }
    }
}
