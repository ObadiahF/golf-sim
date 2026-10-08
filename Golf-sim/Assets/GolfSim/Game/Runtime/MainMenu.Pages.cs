using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    // The main menu's pages behind its cards: "Choose a course" (Play; CoursePicker) and Practice (a card per practice
    // mode: the driving range and putting green, and the developer modes such as Hole Simulator when Settings >
    // Developer turns them on). The backdrop shows the highlighted card's art; Back returns to the main cards.
    public partial class MainMenu
    {
        enum Page { Home, Courses, Practice }

        const string Hidden = "page--hidden";

        Page page = Page.Home;
        VisualElement homePage, coursePage, practicePage;
        CoursePicker courses;
        CardGrid practiceCards;
        Label practiceTitle, practiceDescription;
        GameMode[] practiceShown = new GameMode[0];

        /// <summary>The page on screen, for checks: "home", "courses" or "practice".</summary>
        public string PageName => page.ToString().ToLowerInvariant();
        public CoursePicker Courses => courses;

        void CreatePages(VisualElement root)
        {
            homePage = root.Q("page-home");
            coursePage = root.Q("page-courses");
            practicePage = root.Q("page-practice");
            courses = new CoursePicker(root, this, ShowArt);
            practiceTitle = root.Q<Label>("practice-title");
            practiceDescription = root.Q<Label>("practice-description");
            practiceCards = new CardGrid(root.Q("practice-cards"));
            practiceCards.SelectionChanged += _ => ShowPracticeCard();
            practiceCards.Activated += _ => StartScene(PracticeMode);
            foreach (var back in root.Query<Button>(className: "page__back").ToList())
            {
                back.focusable = false;
                back.clicked += () => ShowPage(Page.Home);
            }
            RefreshPractice();
            ShowPage(Page.Home);
        }

        /// <summary>For checks and screenshots: opens a page by name ("home", "courses", "practice").</summary>
        public void ShowPage(string name)
        {
            if (Enum.TryParse(name, true, out Page p)) ShowPage(p);
        }

        void ShowPage(Page next)
        {
            page = next;
            homePage.EnableInClassList(Hidden, page != Page.Home);
            coursePage.EnableInClassList(Hidden, page != Page.Courses);
            practicePage.EnableInClassList(Hidden, page != Page.Practice);
            switch (page)
            {
                case Page.Courses: courses.Show(); break;
                case Page.Practice: ShowPracticeCard(); break;
                default: RefreshCards(); break;
            }
        }

        bool PageNav(NavKey key)
        {
            if (key == NavKey.Back)
            {
                ShowPage(Page.Home);
                return true;
            }
            if (page == Page.Courses) return courses.OnNav(key);
            if (key == NavKey.Select) StartScene(PracticeMode);
            else practiceCards.Move(key);
            return true;
        }

        GameMode PracticeMode => practiceCards.Selected >= 0 && practiceCards.Selected < practiceShown.Length ? practiceShown[practiceCards.Selected] : null;

        /// <summary>The practice cards: the practice modes, and the developer ones while Developer modes is on.</summary>
        void RefreshPractice()
        {
            var list = new List<GameMode>(Array.FindAll(practice, m => m));
            if (GameSettings.DeveloperModes) list.AddRange(Array.FindAll(developerModes, m => m));
            var current = PracticeMode;
            practiceShown = list.ToArray();
            practiceCards.Set(Array.ConvertAll(practiceShown, m => new CardInfo { title = m.title, banner = m.banner }),
                              Math.Max(0, Array.IndexOf(practiceShown, current)));
            if (page == Page.Practice) ShowPracticeCard();
        }

        void ShowPracticeCard()
        {
            var mode = PracticeMode;
            if (!mode) return;
            practiceTitle.text = mode.title;
            practiceDescription.text = mode.description;
            ShowArt(mode.banner);
        }

        /// <summary>The Practice card's line: "Driving Range, Putting Green".</summary>
        string PracticeSummary => string.Join(", ", Array.ConvertAll(Array.FindAll(practice, m => m), m => m.title));

        /// <summary>A scene card (practice): fades out and loads its scene with its facility.</summary>
        void StartScene(GameMode mode)
        {
            if (!mode || string.IsNullOrEmpty(mode.sceneName) || ScreenFade.Loading || RoundDirector.Instance?.IsFetching == true) return;
            RoundDirector.SelectPractice(mode.practice);
            fade.LoadScene(mode.sceneName);
        }
    }
}
