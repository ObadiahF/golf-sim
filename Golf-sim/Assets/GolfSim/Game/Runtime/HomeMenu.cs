using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// In-game pause menu (Back: Esc, gamepad B or the phone's Back): pauses the game (and its sounds) and offers
    /// Resume / Restart / Settings / Main Menu, chosen with Up/Down and Select (or the mouse), with the room code for
    /// phones joining mid-round (RoomBadge). Restart is skipped while CanRestart says no (e.g. on a round's scorecard).
    /// Behaviours listed in pauseWhileOpen (camera, shot controls...) are disabled while it is open.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class HomeMenu : MonoBehaviour
    {
        const string SelectedClass = "btn--selected";

        public string menuScene = "MainMenu";
        [Tooltip("Disabled while the HOME menu is open, e.g. the fly camera and the shot panel.")]
        public Behaviour[] pauseWhileOpen = new Behaviour[0];

        /// <summary>True while a pause menu is open; OpenChanged fires when that changes.</summary>
        public static bool IsOpen { get; private set; }
        public static event Action<bool> OpenChanged;
        /// <summary>Set by a game mode: false while Restart Hole isn't allowed (null: always allowed).</summary>
        public static Func<bool> CanRestart;

        VisualElement panel;
        ScreenFade fade;
        Button[] buttons;
        Action[] actions;
        SettingsScreen settings;
        int highlighted;
        bool open;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            IsOpen = false;
            OpenChanged = null;
            CanRestart = null;
        }

        public bool Open
        {
            get => open;
            set => SetOpen(value);
        }

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            panel = root.Q("home");
            buttons = new[] { root.Q<Button>("home-resume"), root.Q<Button>("home-restart"), root.Q<Button>("home-settings"), root.Q<Button>("home-menu") };
            settings = new SettingsScreen(root);
            actions = new Action[]
            {
                () => SetOpen(false),
                () => { if (RestartAllowed) fade.LoadScene(SceneManager.GetActiveScene().name); },
                () => settings.Show(),
                () => fade.LoadScene(menuScene),
            };
            for (int i = 0; i < buttons.Length; i++)
            {
                int index = i;
                buttons[i].focusable = false; // Up/Down/Select come from NavInput, so native focus would act twice
                buttons[i].clicked += () => actions[index]();
                buttons[i].RegisterCallback<PointerEnterEvent>(_ => Highlight(index));
            }
            fade = new ScreenFade(root);
            new RoomBadge(root);
            NavInput.Register(OnNav, NavInput.OverlayPriority);
        }

        void OnDisable()
        {
            NavInput.Unregister(OnNav);
            settings?.Close();
            Time.timeScale = 1f;
            if (open) Publish(false);
        }

        bool OnNav(NavKey key)
        {
            if (key == NavKey.Back)
            {
                SetOpen(!open);
                return true;
            }
            if (!open) return false;
            if (key == NavKey.Up) Highlight(highlighted - 1, -1);
            else if (key == NavKey.Down) Highlight(highlighted + 1, 1);
            else if (key == NavKey.Select) actions[highlighted]();
            return true; // the menu is modal: swallow Left/Right too
        }

        static bool RestartAllowed => CanRestart?.Invoke() != false;

        /// <summary>Highlights a button, stepping past disabled ones in direction `step`.</summary>
        void Highlight(int index, int step = 1)
        {
            for (int tries = 0; tries < buttons.Length && !buttons[Wrap(index)].enabledSelf; tries++) index += step;
            highlighted = Wrap(index);
            for (int i = 0; i < buttons.Length; i++) buttons[i].EnableInClassList(SelectedClass, i == highlighted);
        }

        void SetOpen(bool value)
        {
            if (open == value) return;
            open = value;
            panel.EnableInClassList("home--open", open);
            Time.timeScale = open ? 0f : 1f;
            foreach (var b in pauseWhileOpen)
                if (b) b.enabled = !open;
            if (open)
            {
                UnityEngine.Cursor.lockState = CursorLockMode.None;
                buttons[1].SetEnabled(RestartAllowed);
                Highlight(0);
            }
            else settings.Close();
            Publish(open);
        }

        int Wrap(int index) => (index % buttons.Length + buttons.Length) % buttons.Length;

        static void Publish(bool value)
        {
            IsOpen = value;
            OpenChanged?.Invoke(value);
        }
    }
}
