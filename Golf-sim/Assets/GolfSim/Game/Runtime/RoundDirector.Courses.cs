using System;
using System.IO;
using GolfSim.Course;
using UnityEngine;

namespace GolfSim.Game
{
    // Which course a round is played on (CourseCatalog): the one picked on the "Choose a course" screen, or for a game
    // started from the phone the last one chosen on the TV. The course decides which course types the trainer draws the
    // holes from (RoundDirector.Holes.cs), may dress every hole as one theme (Coastal), and brings its time of day
    // (RoundDirector.Scenery.cs).
    public partial class RoundDirector
    {
        /// <summary>The course picked on the menu for the next round (consumed by that round), or null.</summary>
        CourseType menuCourse;
        CourseType roundCourse;
        /// <summary>Said on the round's first hole: "Not enough Winter holes yet..." (the download overlay hides toasts).</summary>
        string courseNote;

        /// <summary>The course of the round being played (or last played).</summary>
        public CourseType Course => roundCourse ?? CourseCatalog.Last;

        /// <summary>"Play" on a course card: a solo round of `holes` on this course at this time of day.</summary>
        public static void PlayFromMenu(CourseType course, int holes, SkyChoice sky)
        {
            if (!Instance) return;
            Instance.menuCourse = course;
            Instance.menuSky = sky;
            Instance.PlayRound(holes);
        }

        /// <summary>A round starts (StartRound): the menu's course and sky, else the TV's last choice (a game from the phone).</summary>
        void PickCourse()
        {
            if (menuCourse != null) roundCourse = menuCourse;
            else
            {
                roundCourse = CourseCatalog.Last;
                menuSky = CourseCatalog.SkyFor(roundCourse, CourseCatalog.LastSky);
            }
            menuCourse = null;
            courseNote = null;
        }

        /// <summary>A downloaded hole's package with the course's theme on it (Coastal dresses links holes for the coast).</summary>
        HolePackage LoadPackage(string folder)
        {
            var package = HolePackage.Load(Path.Combine(folder, HolePackage.FileName));
            if (!string.IsNullOrEmpty(roundCourse?.theme)) package.theme = roundCourse.theme;
            return package;
        }

        /// <summary>A setting changed (GameSettings): the wind on the hole being played follows the new strength.</summary>
        void OnSettingsChanged()
        {
            if (!ball || ball.InMotion || phase is not (Phase.Playing or Phase.BetweenShots)) return;
            SetWind(round != null && round.HoleIndex >= 0 ? HoleWind(round.HoleIndex) : PracticeWind());
            PublishState();
        }

        /// <summary>The wind scale rounds and practice play with: the course's, times the Wind setting.</summary>
        float WindScale => course.windScale * GameSettings.WindScale;

        /// <summary>For the "Getting the course" overlay: "Winter holes", "holes".</summary>
        string HoleKind => roundCourse != null && roundCourse.IsType ? $"{roundCourse.title} holes" : "holes";

        /// <summary>A round of a course type the trainer had too few holes of.</summary>
        string MixedToast => $"Not enough {roundCourse?.title ?? "of these"} holes yet: mixing in other courses";

        static string[] NoPresets => Array.Empty<string>();
    }
}
