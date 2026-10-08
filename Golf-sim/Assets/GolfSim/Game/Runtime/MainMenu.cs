using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// Main menu: the selected card's art fills the screen as a backdrop, with its title, description and a button over
    /// it, and a row of cards below: Play, Practice, Scores and Settings (modes), plus an Update card when the game server
    /// has a newer version (UpdateFlow). Left/Right (keyboard, gamepad or the phone's D-pad) or a click selects a card;
    /// Select, the button or a second click opens it. Play resumes the server's game in progress, else opens "Choose a
    /// course" (CoursePicker); Practice opens the practice cards (MainMenu.Pages.cs); Scores the leaderboard; Settings
    /// the Settings screen. Back returns from a page to the cards. Under the logo: the room code the phones pair with,
    /// and the connection (RoomBadge); top right the version and the clock.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public partial class MainMenu : MonoBehaviour
    {
        const string Shown = "backdrop--shown";
        static readonly Color Background = new Color32(11, 15, 20, 255);

        [Tooltip("The main cards in order: Play (Round), Practice, Scores, Settings.")]
        public GameMode[] modes = new GameMode[0];
        [Tooltip("The Practice page's cards (driving range, putting green...).")]
        public GameMode[] practice = new GameMode[0];
        [Tooltip("Practice cards only shown with Settings > Developer > Developer modes on (Hole Simulator).")]
        public GameMode[] developerModes = new GameMode[0];

        VisualElement[] backdrops;
        CardGrid cards;
        Button action;
        Label eyebrow, title, description, clock, date, version;
        ScreenFade fade;
        ScoresScreen scores;
        SettingsScreen settings;
        UpdateFlow updates;
        /// <summary>The cards on screen: modes, plus the Update card while there is an update.</summary>
        GameMode[] shown = new GameMode[0];
        int frontBackdrop;
        Texture2D art;

        GameMode Mode => cards != null && cards.Selected >= 0 && cards.Selected < shown.Length ? shown[cards.Selected] : null;

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            PlainText.Apply(root); // the Resume line lists the players' names
            backdrops = new[] { root.Q("backdrop-a"), root.Q("backdrop-b") };
            root.Q("shade").style.backgroundImage = MenuArt.Shade(Background);
            eyebrow = root.Q<Label>("eyebrow");
            title = root.Q<Label>("title");
            description = root.Q<Label>("description");
            clock = root.Q<Label>("clock");
            date = root.Q<Label>("date");
            version = root.Q<Label>("version");
            action = root.Q<Button>("action");
            var quit = root.Q<Button>("quit");
            action.clicked += Open;
            quit.clicked += Application.Quit;
            action.focusable = quit.focusable = false; // keys come from NavInput, so native focus would act twice
            scores = new ScoresScreen(root, this);
            settings = new SettingsScreen(root);
            cards = new CardGrid(root.Q("cards"));
            cards.SelectionChanged += _ => ShowCard();
            cards.Activated += _ => Open();
            CreatePages(root);
            BuildCards();
            fade = new ScreenFade(root);
            NavInput.Register(OnNav, NavInput.MenuPriority);
            GameSettings.Changed += OnSettingsChanged;
            updates = new UpdateFlow(this, RefreshUpdateCard);
            version.text = UpdateFlow.VersionLine;
            new RoomBadge(root);
        }

        void OnDisable()
        {
            NavInput.Unregister(OnNav);
            GameSettings.Changed -= OnSettingsChanged;
            updates?.Dispose();
            scores?.Close();
            settings?.Close();
        }

        bool OnNav(NavKey key)
        {
            if (page != Page.Home) return PageNav(key);
            switch (key)
            {
                case NavKey.Left or NavKey.Right: cards.Move(key); return true;
                case NavKey.Select: Open(); return true;
                default: return false;
            }
        }

        void BuildCards(GameMode select = null)
        {
            var list = new List<GameMode>(Array.FindAll(modes, m => m));
            if (UpdateFlow.CardWanted) list.Add(UpdateFlow.Mode);
            shown = list.ToArray();
            cards.Set(Array.ConvertAll(shown, Card), Math.Max(0, Array.IndexOf(shown, select)));
            ShowCard();
        }

        /// <summary>A main card: Play shows the last course chosen, Practice what it holds.</summary>
        CardInfo Card(GameMode mode) => mode.kind switch
        {
            GameMode.ModeKind.Round => new CardInfo { title = mode.title, subtitle = CourseCatalog.Last.title, banner = PlayArt(mode) },
            GameMode.ModeKind.Practice => new CardInfo { title = mode.title, subtitle = PracticeSummary, banner = mode.banner },
            GameMode.ModeKind.Update => new CardInfo { title = mode.title, banner = mode.banner, style = "card--update" },
            _ => new CardInfo { title = mode.title, banner = mode.banner },
        };

        /// <summary>An update appeared or went away (or its text changed): add or drop its card, keeping the selection.</summary>
        void RefreshUpdateCard()
        {
            version.text = UpdateFlow.VersionLine;
            if (UpdateFlow.CardWanted == Array.IndexOf(shown, UpdateFlow.Mode) >= 0) return;
            BuildCards(Mode);
        }

        /// <summary>The selected card's art, title, text and button.</summary>
        void ShowCard()
        {
            var mode = Mode;
            if (!mode) return;
            eyebrow.text = Eyebrow(mode.kind);
            title.text = mode.title;
            description.text = Describe(mode);
            action.text = ActionText(mode);
            ShowArt(mode.kind == GameMode.ModeKind.Round ? PlayArt(mode) : mode.banner);
        }

        /// <summary>Crossfades the backdrop to this art (the hidden layer gets it, then the layers swap).</summary>
        void ShowArt(Texture2D texture)
        {
            if (texture == art) return;
            art = texture;
            frontBackdrop = 1 - frontBackdrop;
            backdrops[frontBackdrop].style.backgroundImage = texture;
            backdrops[frontBackdrop].AddToClassList(Shown);
            backdrops[1 - frontBackdrop].RemoveFromClassList(Shown);
        }

        /// <summary>Select / the button / a second click on the selected card.</summary>
        void Open()
        {
            var mode = Mode;
            if (!mode || ScreenFade.Loading || RoundDirector.Instance?.IsFetching == true) return; // a second Select during the fade or download
            switch (mode.kind)
            {
                case GameMode.ModeKind.Update: updates.Start(); break;
                case GameMode.ModeKind.Round when RoundDirector.CanResume: RoundDirector.Instance.PlayRound(); break;
                case GameMode.ModeKind.Round: ShowPage(Page.Courses); break;
                case GameMode.ModeKind.Practice: ShowPage(Page.Practice); break;
                case GameMode.ModeKind.Scores: scores.Show(); break;
                case GameMode.ModeKind.Settings: settings.Show(); break;
                default: StartScene(mode); break;
            }
        }

        void Update()
        {
            var now = DateTime.Now;
            clock.text = now.ToString("h:mm tt");
            date.text = now.ToString("dddd, MMMM d");
            updates.Tick();
            if (Mode && Mode.kind is GameMode.ModeKind.Round or GameMode.ModeKind.Update)
            {
                description.text = Describe(Mode); // a game started or ended on the server; the update's progress
                action.text = ActionText(Mode);
            }
        }

        /// <summary>A setting changed: the developer modes come and go under Practice; the round length shows on Play.</summary>
        void OnSettingsChanged()
        {
            RefreshPractice();
            RefreshCards();
        }

        /// <summary>Redraws the main cards and the hero (Play shows the course last chosen).</summary>
        void RefreshCards()
        {
            for (int i = 0; i < shown.Length; i++) cards.Show(i, Card(shown[i]));
            ShowCard();
        }

        /// <summary>The hero text: what the button will do on this card.</summary>
        string Describe(GameMode mode) => mode.kind switch
        {
            GameMode.ModeKind.Round => RoundDirector.ResumeDescription() ??
                                       $"{mode.description} Last course: {CourseCatalog.Last.title}, {GameSettings.RoundLength} holes.",
            GameMode.ModeKind.Update => UpdateFlow.Description,
            _ => mode.description,
        };

        /// <summary>Play's art: the last course chosen (the card's own before a single course or Night/Sunset was picked).</summary>
        static Texture2D PlayArt(GameMode mode) => CourseCatalog.Last == CourseCatalog.Default ? mode.banner : CourseCatalog.Last.Banner;

        static string Eyebrow(GameMode.ModeKind kind) => kind switch
        {
            GameMode.ModeKind.Round => "PLAY",
            GameMode.ModeKind.Practice => "PRACTICE",
            GameMode.ModeKind.Scores => "STATS",
            GameMode.ModeKind.Settings => "OPTIONS",
            GameMode.ModeKind.Update => "UPDATE",
            _ => "GAME MODE",
        };

        static string ActionText(GameMode mode) => mode.kind switch
        {
            GameMode.ModeKind.Round => RoundDirector.CanResume ? "Resume" : "Choose a course",
            GameMode.ModeKind.Update => "Update",
            GameMode.ModeKind.Scores => "Leaderboard",
            GameMode.ModeKind.Settings => "Open",
            GameMode.ModeKind.Practice => "Practice",
            _ => "Play",
        };
    }
}
