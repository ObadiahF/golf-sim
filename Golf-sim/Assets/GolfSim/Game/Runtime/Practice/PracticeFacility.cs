using GolfSim.Ball;
using GolfSim.Course;
using GolfSim.Net;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>What a practice scene plays: its own hole, or a facility built in its place.</summary>
    public enum PracticeMode { Hole, DrivingRange, PuttingGreen }

    /// <summary>
    /// A practice facility (driving range, putting green): built at runtime in place of the practice scene's hole
    /// (FacilityGround), then played with the practice hole's machinery (RoundDirector.Practice.cs): every shot is
    /// recorded here, and after a short look at the ball the next one is set up (Advance, then Setup) instead of
    /// playing on from where it stopped. Nothing goes to the server's games. Plain C#: the director owns the timing.
    /// </summary>
    public abstract class PracticeFacility
    {
        /// <summary>state.practice for the phones: which facility is on the TV.</summary>
        public const string RangeState = "range", PuttingGreenState = "puttingGreen";

        /// <summary>The ball and the facility's hole once bound (null before).</summary>
        protected GolfBall Ball { get; private set; }
        protected HoleInfo Hole { get; private set; }

        /// <summary>Shots (or putts) hit this session, and how many were holed.</summary>
        public int Attempts { get; protected set; }
        public int Made { get; protected set; }

        public abstract string StateName { get; }
        /// <summary>"DRIVING RANGE": the HUD's eyebrow.</summary>
        public abstract string Title { get; }
        /// <summary>Real seconds to watch the ball at rest before the next one is set up.</summary>
        public virtual float NextDelay(GolfBall finished) => 2f;
        /// <summary>Shown on the phone (waitReason) while the next ball is being set up.</summary>
        public virtual string Waiting => "Teeing up the next ball";
        /// <summary>True when each new ball starts aimed straight at its target (otherwise the aim carries over).</summary>
        public virtual bool ResetsAim => false;

        public static PracticeFacility Create(PracticeMode mode) => mode switch
        {
            PracticeMode.DrivingRange => new DrivingRange(),
            PracticeMode.PuttingGreen => new PuttingGreen(),
            _ => null,
        };

        /// <summary>The HUD eyebrow for a state's practice field, or null outside a facility.</summary>
        public static string TitleFor(string stateName) => stateName switch
        {
            RangeState => "DRIVING RANGE",
            PuttingGreenState => "PUTTING GREEN",
            _ => null,
        };

        /// <summary>Builds the facility in place of the scene's hole and returns its HoleInfo.</summary>
        public abstract HoleInfo Build(RuntimeThemeLibrary themes);

        /// <summary>The scene's ball is bound: the shot panel stays out of the way (Tab still shows it).</summary>
        public virtual void Bind(GolfBall ball, HoleInfo hole)
        {
            Ball = ball;
            Hole = hole;
            var panel = ball.GetComponent<ShotPanel>();
            if (!panel) return;
            panel.showPanel = false;
            // The next ball is set up before the panel's own line-up would glide off to the one that just stopped.
            panel.lineUpDelay = Mathf.Max(panel.lineUpDelay, NextDelay(ball) + 1f);
        }

        public virtual void Unbind()
        {
            Ball = null;
            Hole = null;
        }

        /// <summary>The club to play when `club` is picked (the putting green keeps the putter; the range aims at its flag).</summary>
        public virtual string Club(string club) => club;

        /// <summary>A shot finished: record it. Returns the toast for the TV, or null to keep the putting HUD's.</summary>
        public abstract string ShotFinished(GolfBall finished, string club);

        /// <summary>Moves on `step` shots in the facility's cycle (the range has only one: the tee).</summary>
        public virtual void Advance(int step) { }

        /// <summary>A mulligan: the last shot's setup comes back (call Setup next).</summary>
        public virtual void Again() { }

        /// <summary>The remote's Up/Down: true when the facility uses them (the putting green: previous / next putt).</summary>
        public virtual int StepFor(NavKey key) => 0;

        /// <summary>The key hints along the bottom of the TV, when they differ from a round's; null for the usual ones.</summary>
        public virtual string KeyHint => null;

        /// <summary>Puts the ball where the current shot is played from.</summary>
        public abstract void Setup();

        /// <summary>The facility's fields of "state" (called by BuildState).</summary>
        public void Fill(StateMessage s)
        {
            s.practice = StateName;
            s.attempts = Attempts;
            s.made = Made;
        }

        /// <summary>Shows this session's numbers on the practice card.</summary>
        public abstract void Render(PracticeHud hud);

        protected static float Yards(float metres) => metres * ShotData.YardsPerMeter;

        /// <summary>"4 R", "2 L" or "0" (yards right or left of the aim line).</summary>
        protected static string Side(float yards) =>
            Mathf.Abs(yards) < 0.5f ? "0" : $"{Mathf.Abs(yards):0} {(yards > 0 ? "R" : "L")}";
    }
}
