using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// The Sound settings overlay, opened from the main menu's Sound card and the pause menu: one bar per volume
    /// (master, effects, crowd, ambience, interface), saved through GameAudio. Up/Down picks a volume, Left/Right
    /// changes it by 10 % (to the next 10 % mark from a default off the grid), Back or Select closes (keyboard, gamepad or
    /// the phone's D-pad); a click on a bar sets it. While one is open the phones are told the screen is "settings".
    /// Built in code into any UI Toolkit root that uses Menu.uss.
    /// </summary>
    public class AudioSettingsPanel
    {
        const string Open = "sound--open", Selected = "sound__row--selected";
        const float Step = 0.1f;

        readonly struct Channel
        {
            public readonly string name;
            public readonly Func<float> get;
            public readonly Action<float> set;

            public Channel(string name, Func<float> get, Action<float> set)
            {
                this.name = name;
                this.get = get;
                this.set = set;
            }
        }

        static readonly Channel[] Channels =
        {
            new Channel("Master", () => GameAudio.MasterVolume, v => GameAudio.MasterVolume = v),
            Bus("Effects", SoundBus.Sfx),
            Bus("Crowd", SoundBus.Crowd),
            Bus("Ambience", SoundBus.Ambience),
            Bus("Interface", SoundBus.Ui),
        };

        readonly VisualElement overlay;
        readonly VisualElement[] rows, fills;
        readonly Label[] values;
        int highlighted;

        public bool IsOpen { get; private set; }

        /// <summary>A Sound panel is open (main menu or pause menu); OpenChanged fires when that changes.</summary>
        public static bool AnyOpen => openCount > 0;
        public static event Action<bool> OpenChanged;
        static int openCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            openCount = 0;
            OpenChanged = null;
        }

        public AudioSettingsPanel(VisualElement root)
        {
            overlay = new VisualElement();
            overlay.AddToClassList("sound");
            var panel = new VisualElement();
            panel.AddToClassList("home__panel");
            panel.AddToClassList("sound__panel");
            overlay.Add(panel);
            panel.Add(Text("Sound", "home__title"));
            panel.Add(Text("Up/Down: choose  ·  Left/Right: change  ·  Back: close", "home__subtitle"));

            rows = new VisualElement[Channels.Length];
            fills = new VisualElement[Channels.Length];
            values = new Label[Channels.Length];
            for (int i = 0; i < Channels.Length; i++)
            {
                int index = i;
                var row = new VisualElement();
                row.AddToClassList("sound__row");
                row.Add(Text(Channels[i].name, "sound__name"));
                var bar = new VisualElement();
                bar.AddToClassList("sound__bar");
                fills[i] = new VisualElement { pickingMode = PickingMode.Ignore };
                fills[i].AddToClassList("sound__fill");
                bar.Add(fills[i]);
                bar.RegisterCallback<PointerDownEvent>(e =>
                {
                    Highlight(index);
                    Set(index, bar.layout.width > 0f ? e.localPosition.x / bar.layout.width : Channels[index].get());
                });
                row.Add(bar);
                values[i] = Text("", "sound__value");
                row.Add(values[i]);
                row.RegisterCallback<PointerEnterEvent>(_ => Highlight(index));
                panel.Add(row);
                rows[i] = row;
            }
            var close = new Button(Close) { text = "Done", focusable = false };
            close.AddToClassList("btn");
            close.AddToClassList("btn--primary");
            close.AddToClassList("sound__close");
            panel.Add(close);
            root.Add(overlay);
        }

        public void Show()
        {
            if (IsOpen) return;
            for (int i = 0; i < Channels.Length; i++) Render(i);
            Highlight(0);
            overlay.AddToClassList(Open);
            NavInput.Register(OnNav, NavInput.ModalPriority);
            SetOpen(true);
        }

        public void Close()
        {
            overlay.RemoveFromClassList(Open);
            NavInput.Unregister(OnNav);
            SetOpen(false);
        }

        void SetOpen(bool open)
        {
            if (IsOpen == open) return;
            IsOpen = open;
            openCount = Mathf.Max(0, openCount + (open ? 1 : -1));
            OpenChanged?.Invoke(AnyOpen);
        }

        bool OnNav(NavKey key)
        {
            switch (key)
            {
                case NavKey.Up: Highlight(highlighted - 1); break;
                case NavKey.Down: Highlight(highlighted + 1); break;
                case NavKey.Left: Set(highlighted, Stepped(Channels[highlighted].get(), -1)); break;
                case NavKey.Right: Set(highlighted, Stepped(Channels[highlighted].get(), 1)); break;
                default: Close(); break; // Select or Back
            }
            return true; // modal
        }

        void Highlight(int index)
        {
            highlighted = (index % rows.Length + rows.Length) % rows.Length;
            for (int i = 0; i < rows.Length; i++) rows[i].EnableInClassList(Selected, i == highlighted);
        }

        void Set(int index, float value)
        {
            Channels[index].set(Mathf.Floor(Mathf.Clamp01(value) / Step + 0.5f) * Step); // whole steps (halves up), as the D-pad gives
            Render(index);
        }

        /// <summary>The next 10 % mark in this direction: 70 → 60 or 80, and 75 (a default) → 70 or 80.</summary>
        public static float Stepped(float value, int direction)
        {
            const float Slack = 1e-3f; // float noise: 0.7 is 6.9999 steps
            float steps = value / Step;
            float next = direction > 0 ? Mathf.Floor(steps + Slack) + 1f : Mathf.Ceil(steps - Slack) - 1f;
            return Mathf.Clamp01(next * Step);
        }

        void Render(int index)
        {
            float value = Channels[index].get();
            fills[index].style.width = Length.Percent(value * 100f);
            values[index].text = $"{Mathf.RoundToInt(value * 100f)}%";
        }

        static Channel Bus(string name, SoundBus bus) => new Channel(name, () => GameAudio.GetVolume(bus), v => GameAudio.SetVolume(bus, v));

        static Label Text(string text, string cssClass)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(cssClass);
            return label;
        }
    }
}
