using System;
using GolfSim.Ball;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// Records every shot of the scene's ball: who hit it with what when it starts, and the full path and events
    /// (ShotRecording) when it stops. The replay and the crowd listen to Recorded.
    /// </summary>
    public class ShotRecorder : BallWatcher
    {
        /// <summary>A shot finished and its recording is ready.</summary>
        public event Action<ShotRecording> Recorded;

        /// <summary>The last shot's recording, or null.</summary>
        public ShotRecording Last { get; private set; }
        /// <summary>True while the last shot's ball still lies where it finished (nothing was placed since).</summary>
        public bool LastIsCurrent { get; private set; }

        ShotSetup setup;

        protected override void Bind(GolfBall ball)
        {
            ball.ShotStarted += OnShotStarted;
            ball.ShotFinished += OnShotFinished;
            ball.Placed += OnPlaced;
        }

        protected override void Unbind(GolfBall ball)
        {
            ball.ShotStarted -= OnShotStarted;
            ball.ShotFinished -= OnShotFinished;
            ball.Placed -= OnPlaced;
            Last = null;
            LastIsCurrent = false;
        }

        void OnShotStarted(GolfBall ball)
        {
            var director = RoundDirector.Instance;
            string player = director && director.Round?.CurrentBall is { } current ? current.player : "";
            setup = ShotSetup.From(ball, player, director ? director.Club : "");
            LastIsCurrent = false;
        }

        void OnShotFinished(GolfBall ball)
        {
            if (setup == null || !Hole) return;
            try
            {
                Last = ShotRecording.Simulate(setup, ball, Hole.PinWorld);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return;
            }
            setup = null;
            LastIsCurrent = true;
            Recorded?.Invoke(Last);
        }

        void OnPlaced(GolfBall ball) => LastIsCurrent = false;
    }
}
