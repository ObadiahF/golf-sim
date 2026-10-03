using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// Main menu: the selected mode's art fills the screen as a backdrop, with its title, description and
    /// Play over it, and a row of mode cards below. Click a card (or Left/Right on the keyboard, gamepad or
    /// phone remote) to select it; click it again, press Play or Select to start. Starting fades out and
    /// loads the mode's scene; a round mode starts a round (Up/Down picks 9 or 18 holes); the Scores card
    /// opens the leaderboard. When the game server has a newer version, an Update card joins the row (UpdateFlow).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class MainMenu : MonoBehaviour
    {
        const string Selected = "card--selected";
        const string Shown = "backdrop--shown";
        const string TabSelected = "tab--selected", HolesHidden = "segmented--hidden";
        static readonly int[] HoleChoices = { 9, 18 };
        static readonly Color Background = new Color32(11, 15, 20, 255);

        public GameMode[] modes = new GameMode[0];

        VisualElement[] backdrops, cards;
        VisualElement holes, cardRow;
        Button play;
        Button[] holeButtons;
        Label title, description, clock, date, version;
        ScreenFade fade;
        ScoresScreen scores;
        AudioSettingsPanel sound;
        UpdateFlow updates;
        /// <summary>The cards on screen: modes, plus the Update card while there is an update.</summary>
        GameMode[] shown = new GameMode[0];
        int selected = -1, frontBackdrop, holeChoice;

        GameMode Mode => selected >= 0 ? shown[selected] : null;
        /// <summary>The 9 / 18 choice shows on a round card unless Play would resume the server's game.</summary>
        bool ChoosingHoles => Mode && Mode.kind == GameMode.ModeKind.Round && !RoundDirector.CanResume;

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            PlainText.Apply(root); // the Resume line lists the players' names
            backdrops = new[] { root.Q("backdrop-a"), root.Q("backdrop-b") };
            root.Q("shade").style.backgroundImage = MenuArt.Shade(Background);
            title = root.Q<Label>("title");
            description = root.Q<Label>("description");
            clock = root.Q<Label>("clock");
            date = root.Q<Label>("date");
            version = root.Q<Label>("version");
            play = root.Q<Button>("play");
            var quit = root.Q<Button>("quit");
            play.clicked += Play;
            quit.clicked += Application.Quit;
            play.focusable = quit.focusable = false; // keys come from NavInput, so native focus would act twice
            holes = root.Q("holes");
            holeButtons = new[] { root.Q<Button>("holes-9"), root.Q<Button>("holes-18") };
            for (int i = 0; i < holeButtons.Length; i++)
            {
                int index = i;
                holeButtons[i].focusable = false;
                holeButtons[i].clicked += () => ChooseHoles(index);
            }
            ChooseHoles(0);
            scores = new ScoresScreen(root, this);
            sound = new AudioSettingsPanel(root);
            cardRow = root.Q("cards");
            BuildCards();
            fade = new ScreenFade(root);
            Select(0);
            NavInput.Register(OnNav, NavInput.MenuPriority);
            updates = new UpdateFlow(this, RefreshUpdateCard);
            version.text = UpdateFlow.VersionLine;
        }

        void OnDisable()
        {
            NavInput.Unregister(OnNav);
            updates?.Dispose();
            scores?.Close();
            sound?.Close();
        }

        bool OnNav(NavKey key)
        {
            switch (key)
            {
                case NavKey.Right: Select(selected + 1); return true;
                case NavKey.Left: Select(selected - 1); return true;
                case NavKey.Select: Play(); return true;
                case NavKey.Up or NavKey.Down when ChoosingHoles: ChooseHoles(1 - holeChoice); return true;
                default: return false;
            }
        }

        void ChooseHoles(int index)
        {
            holeChoice = index;
            for (int i = 0; i < holeButtons.Length; i++) holeButtons[i].EnableInClassList(TabSelected, i == holeChoice);
        }

        void BuildCards()
        {
            cardRow.Clear();
            var list = new System.Collections.Generic.List<GameMode>(Array.FindAll(modes, m => m));
            if (UpdateFlow.CardWanted) list.Add(UpdateFlow.Mode);
            shown = list.ToArray();
            cards = new VisualElement[shown.Length];
            for (int i = 0; i < shown.Length; i++)
            {
                int index = i;
                var card = new Button(() => { if (selected == index) Play(); else Select(index); });
                card.AddToClassList("card");
                card.EnableInClassList("card--update", shown[i].kind == GameMode.ModeKind.Update);
                card.focusable = false;
                card.style.backgroundImage = shown[i].banner;
                var label = new Label(shown[i].title);
                label.AddToClassList("card__title");
                card.Add(label);
                cardRow.Add(card);
                cards[i] = card;
            }
        }

        /// <summary>An update appeared or went away (or its text changed): add or drop its card, keeping the selection.</summary>
        void RefreshUpdateCard()
        {
            version.text = UpdateFlow.VersionLine;
            if (UpdateFlow.CardWanted == Array.IndexOf(shown, UpdateFlow.Mode) >= 0) return;
            var current = Mode;
            BuildCards();
            selected = -1;
            Select(Math.Max(0, Array.IndexOf(shown, current)));
        }

        void Select(int index)
        {
            if (shown.Length == 0 || index == selected) return;
            selected = (index + shown.Length) % shown.Length;
            for (int i = 0; i < cards.Length; i++) cards[i].EnableInClassList(Selected, i == selected);

            var mode = shown[selected];
            title.text = mode.title;
            description.text = Describe(mode);
            play.text = mode.kind == GameMode.ModeKind.Update ? "Update" : "Play";
            // Crossfade: load the new art into the hidden layer, then swap which layer is shown.
            frontBackdrop = 1 - frontBackdrop;
            backdrops[frontBackdrop].style.backgroundImage = mode.banner;
            backdrops[frontBackdrop].AddToClassList(Shown);
            backdrops[1 - frontBackdrop].RemoveFromClassList(Shown);
        }

        void Play()
        {
            if (selected < 0 || ScreenFade.Loading || RoundDirector.Instance?.IsFetching == true) return; // a second Select / click during the fade or download
            var mode = shown[selected];
            switch (mode.kind)
            {
                case GameMode.ModeKind.Update: updates.Start(); break;
                case GameMode.ModeKind.Round: RoundDirector.PlayFromMenu(HoleChoices[holeChoice]); break;
                case GameMode.ModeKind.Scores: scores.Show(); break;
                case GameMode.ModeKind.Settings: sound.Show(); break;
                default:
                    if (string.IsNullOrEmpty(mode.sceneName)) break;
                    RoundDirector.SelectPractice(mode.practice);
                    fade.LoadScene(mode.sceneName);
                    break;
            }
        }

        void Update()
        {
            var now = DateTime.Now;
            clock.text = now.ToString("h:mm tt");
            date.text = now.ToString("dddd, MMMM d");
            holes.EnableInClassList(HolesHidden, !ChoosingHoles);
            updates.Tick();
            if (Mode && Mode.kind is GameMode.ModeKind.Round or GameMode.ModeKind.Update) description.text = Describe(Mode);
        }

        /// <summary>The hero text: what Play will do on this card.</summary>
        string Describe(GameMode mode) => mode.kind switch
        {
            GameMode.ModeKind.Round => RoundDirector.MenuDescription(mode, HoleChoices[holeChoice]),
            GameMode.ModeKind.Update => UpdateFlow.Description,
            _ => mode.description,
        };
    }
}
