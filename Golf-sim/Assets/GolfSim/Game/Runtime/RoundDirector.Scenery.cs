using System;
using System.IO;
using GolfSim.Ball;
using GolfSim.Course;
using UnityEngine;

namespace GolfSim.Game
{
    // The time of day of a round's holes: a fixed one picked on the menu (the Night Golf card), or Auto, where the
    // round is an afternoon that may drift into golden hour, dusk and night (SkySchedule). Each hole scene is
    // relit when it loads (Scenery), and after dark the ball glows (BallGlow). Practice stays in the day it was built.
    public partial class RoundDirector
    {
        /// <summary>The choice for the next round started from the menu (consumed by that round).</summary>
        SkyChoice menuSky = SkyChoice.Auto;
        SkyChoice roundSky = SkyChoice.Auto;
        uint skySeed;
        Round skyRound;
        ThemeScenery skyFirstHole;
        TimeOfDay? lastSky;

        /// <summary>The time of day on screen (Day in practice and on the menu).</summary>
        public TimeOfDay Sky { get; private set; }

        /// <summary>"Play a Round" from a menu card with its time of day (GameMode.sky).</summary>
        public static void PlayFromMenu(int holes, SkyChoice sky)
        {
            if (!Instance) return;
            Instance.menuSky = sky;
            Instance.PlayRound(holes);
        }

        /// <summary>The time of day `index` of this round is played at (the same every time in this round).</summary>
        public TimeOfDay SkyFor(int index)
        {
            if (round == null) return TimeOfDay.Day;
            if (skyRound != round)
            {
                // A new round: a server game's sky follows its id (a resume sees the same skies), a solo round rolls one.
                skyRound = round;
                roundSky = menuSky;
                menuSky = SkyChoice.Auto;
                skySeed = round.gameId != 0 ? (uint)round.gameId * 2654435761u : (uint)UnityEngine.Random.Range(1, int.MaxValue);
                skyFirstHole = ThemeScenery.For(FirstHoleTheme());
                lastSky = null;
            }
            return SkySchedule.For(roundSky, skySeed, index, skyFirstHole);
        }

        /// <summary>A round's hole just loaded (OnSceneLoaded, after Bind): light it for its time of day.</summary>
        void ApplyScenery(int index)
        {
            Sky = round != null ? SkyFor(index) : TimeOfDay.Day;
            if (round == null || !hole)
            {
                Scenery.Clear(); // practice: the scene's own day
                return;
            }
            var rig = Scenery.Apply(hole, Sky);
            var preset = SkyPreset.For(Sky);
            var glow = ball.GetComponent<BallGlow>();
            if (preset.IsDark && !glow) glow = ball.gameObject.AddComponent<BallGlow>();
            if (glow) glow.Set(preset.darkness);
            if (Sky != lastSky && Sky != TimeOfDay.Day) hud?.Toast(SkyToast(Sky, ThemeScenery.For(rig.theme)), 4f);
            lastSky = Sky;
        }

        static string SkyToast(TimeOfDay time, ThemeScenery theme) => time switch
        {
            TimeOfDay.GoldenHour => $"Golden hour on the {theme.label.ToLowerInvariant()} course",
            TimeOfDay.Dusk => "Dusk: the lights are coming on and the ball glows",
            TimeOfDay.Night => "Night golf: follow the glowing ball",
            _ => "",
        };

        /// <summary>The round's first hole's theme (it picks when the afternoon starts): its package, else this hole's.</summary>
        string FirstHoleTheme()
        {
            if (courseHoles != null && courseHoles.Count > 0)
            {
                try
                {
                    return HolePackage.Load(Path.Combine(courseHoles[0], HolePackage.FileName)).theme;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[RoundDirector] Couldn't read the first hole's theme: {e.Message}");
                }
            }
            return hole ? hole.theme : null;
        }
    }
}
