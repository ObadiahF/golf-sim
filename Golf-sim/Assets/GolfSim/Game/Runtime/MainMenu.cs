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
    /// opens the leaderboard.
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
        VisualElement holes;
        Button[] holeButtons;
        Label title, description, clock, date;
        ScreenFade fade;
        ScoresScreen scores;
        AudioSettingsPanel sound;
        int selected = -1, frontBackdrop, holeChoice;

        GameMode Mode => selected >= 0 ? modes[selected] : null;
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
            var play = root.Q<Button>("play");
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
            BuildCards(root.Q("cards"));
            fade = new ScreenFade(root);
            Select(0);
            NavInput.Register(OnNav, NavInput.MenuPriority);
        }

        void OnDisable()
        {
            NavInput.Unregister(OnNav);
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

        void BuildCards(VisualElement row)
        {
            row.Clear();
            modes = Array.FindAll(modes, m => m);
            cards = new VisualElement[modes.Length];
            for (int i = 0; i < modes.Length; i++)
            {
                int index = i;
                var card = new Button(() => { if (selected == index) Play(); else Select(index); });
                card.AddToClassList("card");
                card.focusable = false;
                card.style.backgroundImage = modes[i].banner;
                var label = new Label(modes[i].title);
                label.AddToClassList("card__title");
                card.Add(label);
                row.Add(card);
                cards[i] = card;
            }
        }

        void Select(int index)
        {
            if (modes.Length == 0 || index == selected) return;
            selected = (index + modes.Length) % modes.Length;
            for (int i = 0; i < cards.Length; i++) cards[i].EnableInClassList(Selected, i == selected);

            var mode = modes[selected];
            title.text = mode.title;
            description.text = mode.kind == GameMode.ModeKind.Round ? RoundDirector.MenuDescription(mode, HoleChoices[holeChoice]) : mode.description;
            // Crossfade: load the new art into the hidden layer, then swap which layer is shown.
            frontBackdrop = 1 - frontBackdrop;
            backdrops[frontBackdrop].style.backgroundImage = mode.banner;
            backdrops[frontBackdrop].AddToClassList(Shown);
            backdrops[1 - frontBackdrop].RemoveFromClassList(Shown);
        }

        void Play()
        {
            if (selected < 0 || ScreenFade.Loading || RoundDirector.Instance?.IsFetching == true) return; // a second Select / click during the fade or download
            var mode = modes[selected];
            switch (mode.kind)
            {
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
            if (Mode && Mode.kind == GameMode.ModeKind.Round) description.text = RoundDirector.MenuDescription(Mode, HoleChoices[holeChoice]);
        }
    }
}
