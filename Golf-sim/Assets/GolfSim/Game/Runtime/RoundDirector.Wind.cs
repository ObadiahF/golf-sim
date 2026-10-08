using GolfSim.Ball;
using GolfSim.Course;
using GolfSim.Net;
using UnityEngine;

namespace GolfSim.Game
{
    // Wind: each hole of a round has its own (from the round's seed, so restarting a hole brings the same wind back and a
    // resumed server game plays the same weather), the practice hole a fresh one each time, and the facilities none (the
    // range shows each club's true distance; a putt never leaves the ground). Every shot is
    // hit into it (the shot panel's wind), the flags fly with it, and the HUD and "state" show it against the aim. The
    // Wind setting scales it (GameSettings.WindScale: off, light, normal, strong), on the hole in play too when it changes.
    public partial class RoundDirector
    {
        Wind wind = Wind.Calm; // without a shot panel; with one, its wind is the one shots fly in (Tab's sliders can change it)
        int windSeed;

        Wind CurrentWind => panel ? panel.Wind : wind;

        /// <summary>A round starts: its holes' winds (a server game's id keeps them across a resume).</summary>
        void SeedWind(Round newRound) => windSeed = newRound.gameId != 0 ? (int)newRound.gameId : System.Environment.TickCount;

        /// <summary>This hole of the round's wind.</summary>
        Wind HoleWind(int index) => Wind.Random(new System.Random(unchecked(windSeed * 7919 + index)), WindScale);

        /// <summary>Practice: a new wind each time on the practice hole, calm on the range and the putting green.</summary>
        Wind PracticeWind() => facility != null ? Wind.Calm : Wind.Random(new System.Random(), WindScale);

        void SetWind(Wind value)
        {
            wind = value;
            if (panel) panel.Wind = value; // a shot through the panel (every remote shot) sets the ball's from this
            if (ball)
            {
                ball.windSpeed = value.MetersPerSecond;
                ball.windHeading = value.Heading;
            }
            foreach (var flag in FindObjectsByType<FlagWave>())
            {
                flag.windHeading = value.Heading;
                flag.windStrength = value.FlagStrength;
            }
        }

        /// <summary>The wind fields of "state" (called by BuildState): its speed and where it blows against the aim.</summary>
        void FillWind(StateMessage s)
        {
            var now = CurrentWind;
            s.wind = Mathf.RoundToInt(now.mph);
            s.windAngle = now.IsCalm ? 0 : Mathf.RoundToInt(now.RelativeTo(ball.AimDirection)) % 360;
        }
    }
}
