using System;
using GolfSim.Ball;
using GolfSim.Course;
using GolfSim.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    // Practice facilities (the driving range and the putting green): picked on the main menu, built into the practice
    // scene in place of its hole, and played as practice with one difference: the facility records each shot and,
    // after a short look at the ball, the next one is set up (teed up again, or the next putt) instead of playing on
    // from where it stopped. A round never plays on one, and nothing is sent to the server's games.
    public partial class RoundDirector
    {
        PracticeMode practiceMode;
        PracticeFacility facility;
        PracticeHud practiceHud;

        /// <summary>The facility on the TV, or null (a round, the practice hole, the menu).</summary>
        public PracticeFacility Facility => facility;

        /// <summary>A practice card on the menu: what the practice scene about to load plays (its own hole, or a facility).</summary>
        public static void SelectPractice(PracticeMode mode)
        {
            if (Instance) Instance.practiceMode = mode;
        }

        /// <summary>A scene loaded (called before binding its ball): builds the chosen facility in place of a practice scene's hole.</summary>
        void PrepareFacility()
        {
            if (facility != null) pending = null; // a next ball scheduled on the scene that just went
            facility?.Unbind();
            facility = null;
            hud?.KeyHint(null);
            if (!FindAnyObjectByType<HoleInfo>())
            {
                practiceMode = PracticeMode.Hole; // back on the menu: the next practice card chooses again
                return;
            }
            if (round != null || practiceMode == PracticeMode.Hole) return;
            if (!course.themes)
            {
                Debug.LogError("[RoundDirector] CourseRound has no runtime themes to build the practice facility; playing the scene's hole.");
                return;
            }
            var made = PracticeFacility.Create(practiceMode);
            try
            {
                var built = made.Build(course.themes);
                facility = made;
                Debug.Log($"[RoundDirector] Built the {made.Title.ToLowerInvariant()} ({built.name}).");
            }
            catch (Exception e)
            {
                Debug.LogError($"[RoundDirector] Couldn't build the {made.Title.ToLowerInvariant()}: {e}. Playing the scene's hole.");
            }
        }

        /// <summary>Practice started on a facility (StartPractice): binds it and sets up the first ball.</summary>
        void StartFacility()
        {
            if (facility == null) return;
            facility.Bind(ball, hole);
            hud?.KeyHint(facility.KeyHint);
            ReadyFacility();
        }

        /// <summary>Puts the ball where the facility's current shot is played from, lined up and ready to hit.</summary>
        void ReadyFacility()
        {
            if (facility == null || !ball) return;
            pending = null;
            facility.Setup();
            if (facility.ResetsAim) ball.aimOffset = 0f;
            phase = Phase.Playing;
            SetClub(club, publish: false); // the facility's club rule and target for the new spot
            if (panel) panel.LineUp();
            PublishState();
        }

        /// <summary>A shot on a facility finished (OnShotFinished): record it, then set up the next ball after a look.</summary>
        void FacilityShotFinished(GolfBall finished)
        {
            string toast = facility.ShotFinished(finished, club);
            if (toast != null) hud?.Toast(toast);
            phase = Phase.BetweenShots;
            PublishState();
            Later(facility.NextDelay(finished), () =>
            {
                facility?.Advance(1);
                ReadyFacility();
            });
        }

        /// <summary>A mulligan on a facility: the last shot again (true when there is a facility to do it).</summary>
        bool FacilityAgain()
        {
            if (facility == null) return false;
            facility.Again();
            ReadyFacility();
            return true;
        }

        /// <summary>Up/Down on a facility that steps through its shots (the putting green); true when used.</summary>
        bool FacilityNav(NavKey key)
        {
            int step = facility?.StepFor(key) ?? 0;
            if (step == 0) return false;
            if (ball.InMotion) return true; // the putter stays: no club to change meanwhile either
            facility.Advance(step);
            ReadyFacility();
            return true;
        }

        /// <summary>The practice card (called by PublishState): on a facility in game and paused, hidden otherwise.</summary>
        void ShowPractice(StateMessage s)
        {
            if (practiceHud == null && facility != null && hud != null)
                practiceHud = new PracticeHud(GetComponentInChildren<UIDocument>().rootVisualElement);
            if (practiceHud == null) return;
            if (facility != null && s.screen is StateMessage.Game or StateMessage.Paused) facility.Render(practiceHud);
            else practiceHud.Hide();
        }
    }
}
