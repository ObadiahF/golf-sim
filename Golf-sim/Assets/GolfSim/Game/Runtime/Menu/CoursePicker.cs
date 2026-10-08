using System;
using GolfSim.Course;
using GolfSim.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// The main menu's "Choose a course" page (Play): a card per course (CourseCatalog) and two options under them,
    /// Holes (9 / 18: the Round length setting) and Time of day (Course default, Day, Golden hour, Dusk, Night). Arrows
    /// move over the cards; Down from the last row reaches the options, where Left/Right change them and Up goes back.
    /// Select (or a click on the selected card) tees off on the selected course; the choice is remembered for next
    /// time and for games started from the phone. Each card says how many holes the trainer has of its course types
    /// (GET /api/game/presets, when it answers); a course that can't fill a round is dimmed, and plays topped up with
    /// other types.
    /// </summary>
    public class CoursePicker
    {
        const int Columns = 5;
        const string OptionsFocused = "grid--unfocused";

        readonly MonoBehaviour host;
        readonly Action<Texture2D> showArt;
        readonly VisualElement gridElement;
        readonly CardGrid grid;
        readonly RowList options;
        readonly Label title, description;

        public CoursePicker(VisualElement root, MonoBehaviour host, Action<Texture2D> showArt)
        {
            this.host = host;
            this.showArt = showArt;
            title = root.Q<Label>("course-title");
            description = root.Q<Label>("course-description");
            gridElement = root.Q("course-grid");
            grid = new CardGrid(gridElement, Columns);
            grid.SelectionChanged += OnSelected;
            grid.Activated += _ => TeeOff();

            var holes = new ChoiceSetting("Holes", Array.ConvertAll(GameSettings.RoundLengths, n => $"{n} holes"),
                                          () => Array.IndexOf(GameSettings.RoundLengths, GameSettings.RoundLength),
                                          i => { GameSettings.RoundLength = GameSettings.RoundLengths[i]; Refresh(); });
            var labels = Array.ConvertAll((SkyOption[])Enum.GetValues(typeof(SkyOption)), o => o.Label());
            var sky = ChoiceSetting.For("Time of day", labels, () => CourseCatalog.LastSky, v => { CourseCatalog.LastSky = v; Refresh(); },
                                        v => v == SkyOption.CourseDefault ? $"{Selected.title}: {SkyName(Selected.sky)}." : null);
            var optionsElement = root.Q("course-options");
            options = new RowList(new SettingRow[] { holes, sky });
            foreach (var row in options.Rows) optionsElement.Add(row.element);
            options.HighlightChanged += row => gridElement.EnableInClassList(OptionsFocused, row != null);
        }

        public CourseType Selected => CourseCatalog.All[Mathf.Max(0, grid.Selected)];

        /// <summary>The page opens on the last course chosen, the cards highlighted; asks the trainer for its hole counts.</summary>
        public void Show()
        {
            grid.Set(Array.ConvertAll(CourseCatalog.All, Card), Array.IndexOf(CourseCatalog.All, CourseCatalog.Last));
            options.Highlight(-1);
            options.RenderAll();
            host.StartCoroutine(TrainerHoles.FetchPresets(ServerConfig.Load(), found => { if (found != null) Refresh(); }));
        }

        /// <summary>Arrows, Select; Back is the menu's (back to the main page).</summary>
        public bool OnNav(NavKey key)
        {
            if (key == NavKey.Select)
            {
                TeeOff();
                return true;
            }
            if (options.Highlighted >= 0)
            {
                if (key == NavKey.Up && !options.Move(-1)) options.Highlight(-1); // back up to the cards
                else options.Handle(key);
                return true;
            }
            if (!grid.Move(key) && key == NavKey.Down) options.HighlightEnd(1);
            return true;
        }

        /// <summary>Starts the round on the selected course (ignored while a scene or a round's holes are on their way).</summary>
        public void TeeOff()
        {
            if (ScreenFade.Loading || RoundDirector.Instance?.IsFetching == true) return;
            var course = Selected;
            CourseCatalog.Last = course;
            RoundDirector.PlayFromMenu(course, GameSettings.RoundLength, CourseCatalog.SkyFor(course, CourseCatalog.LastSky));
        }

        void OnSelected(int index)
        {
            options.Highlight(-1); // a click on a card while the options were highlighted
            Describe();
            showArt(Selected.Banner);
            options.RenderAll(); // "Course default" names this course's time of day
        }

        /// <summary>Hole counts, the round length or the time of day changed: redraw the cards and the text.</summary>
        void Refresh()
        {
            for (int i = 0; i < CourseCatalog.All.Length; i++) grid.Show(i, Card(CourseCatalog.All[i]));
            Describe();
        }

        void Describe()
        {
            var course = Selected;
            title.text = course.title;
            int playable = TrainerHoles.Playable(course.presets);
            string sky = CourseCatalog.LastSky == SkyOption.CourseDefault ? "" : $" At {CourseCatalog.LastSky.Label().ToLowerInvariant()}.";
            string shortOf = playable >= 0 && playable < GameSettings.RoundLength
                ? $" Only {Holes(playable)} so far: other courses fill the round." : "";
            description.text = $"{course.description}{sky}{shortOf}";
        }

        static CardInfo Card(CourseType course)
        {
            int playable = TrainerHoles.Playable(course.presets);
            return new CardInfo
            {
                title = course.title,
                subtitle = course.IsType ? null : "Every course type",
                banner = course.Banner,
                badge = playable < 0 ? null : playable == 0 ? "No holes yet" : Holes(playable),
                dimmed = playable >= 0 && playable < GameSettings.RoundLength,
            };
        }

        static string Holes(int count) => count == 1 ? "1 hole" : $"{count} holes";

        /// <summary>A course's own time of day, for the Course default note.</summary>
        static string SkyName(SkyChoice sky) => sky switch
        {
            SkyChoice.Auto => "an afternoon that may run into dusk and night",
            SkyChoice.Sunset => "golden hour into dusk",
            _ => ((TimeOfDay)(sky - 1)).Label().ToLowerInvariant(),
        };
    }
}
