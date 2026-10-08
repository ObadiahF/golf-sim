using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// The Settings overlay, opened from the main menu's Settings card and the pause menu: one scrolling list in sections
    /// (Sound, Gameplay, Display, Connection, About, Developer; see SettingsScreen.Sections.cs) with the section names
    /// down the side. Up/Down picks a setting, Left/Right changes it, Select changes it too (or runs it), Back closes
    /// (keyboard, gamepad or the phone's D-pad); the mouse works on everything. Every change is saved and applied at
    /// once (GameSettings, GameAudio). While one is open the phones are told the screen is "settings", so they show
    /// their remote. Built in code into any UI Toolkit root that uses Settings.uss.
    /// </summary>
    public partial class SettingsScreen
    {
        const string OpenClass = "settings--open", CurrentSection = "settings__section--current";
        const long RefreshMs = 500; // the connection and update lines while open

        readonly VisualElement overlay;
        readonly ScrollView scroll;
        readonly RowList list;
        readonly List<Section> sections = new List<Section>();
        readonly IVisualElementScheduledItem refresh;

        /// <summary>A heading in the list and its name in the side column; rows [first, end).</summary>
        class Section
        {
            public string title;
            public Label heading, side;
            public int first, end;
        }

        public bool IsOpen { get; private set; }

        /// <summary>A Settings screen is open (main menu or pause menu); OpenChanged fires when that changes.</summary>
        public static bool AnyOpen => openCount > 0;
        public static event Action<bool> OpenChanged;
        static int openCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            openCount = 0;
            OpenChanged = null;
        }

        public SettingsScreen(VisualElement root)
        {
            overlay = MenuUi.Box("settings");
            var panel = MenuUi.Box("settings__panel");
            overlay.Add(panel);

            var header = MenuUi.Box("settings__header");
            var titles = new VisualElement();
            titles.Add(MenuUi.Text("OPTIONS", "eyebrow"));
            titles.Add(MenuUi.Text("Settings", "page__title"));
            header.Add(titles);
            var done = new Button(Close) { text = "Done", focusable = false };
            done.AddToClassList("btn");
            done.AddToClassList("btn--primary");
            done.AddToClassList("btn--small");
            header.Add(done);
            panel.Add(header);

            var body = MenuUi.Box("settings__body");
            var side = MenuUi.Box("settings__side");
            scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.AddToClassList("settings__scroll");
            body.Add(side);
            body.Add(scroll);
            panel.Add(body);
            panel.Add(MenuUi.Text("Up/Down: choose  ·  Left/Right: change  ·  Back: close", "hint"));

            var rows = new List<SettingRow>();
            foreach (var (title, sectionRows) in Sections())
            {
                var section = new Section { title = title, first = rows.Count };
                section.heading = MenuUi.Text(title, "settings__heading");
                scroll.Add(section.heading);
                foreach (var row in sectionRows)
                {
                    scroll.Add(row.element);
                    rows.Add(row);
                }
                section.end = rows.Count;
                section.side = new Label(title);
                section.side.AddToClassList("settings__section");
                section.side.RegisterCallback<ClickEvent>(_ => Jump(section));
                side.Add(section.side);
                sections.Add(section);
            }
            list = new RowList(rows);
            list.HighlightChanged += OnHighlight;
            refresh = overlay.schedule.Execute(list.RenderAll).Every(RefreshMs);
            refresh.Pause();
            root.Add(overlay);
        }

        /// <summary>Opens on the first setting (Master volume).</summary>
        public void Show()
        {
            if (IsOpen) return;
            list.RenderAll();
            list.Highlight(-1);
            list.HighlightEnd(1);
            scroll.scrollOffset = Vector2.zero;
            overlay.AddToClassList(OpenClass);
            NavInput.Register(OnNav, NavInput.ModalPriority);
            refresh.Resume();
            SetOpen(true);
        }

        public void Close()
        {
            overlay.RemoveFromClassList(OpenClass);
            NavInput.Unregister(OnNav);
            refresh.Pause();
            SetOpen(false);
        }

        /// <summary>For checks and screenshots: shows a section's heading (and highlights its first setting, if it has one).</summary>
        public void ShowSection(string title)
        {
            var section = sections.Find(s => s.title == title);
            if (section != null) Jump(section);
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
            if (key == NavKey.Back) Close();
            else list.Handle(key);
            return true; // modal
        }

        void Jump(Section section)
        {
            for (int i = section.first; i < section.end; i++)
                if (list.Rows[i].Selectable)
                {
                    list.Highlight(i);
                    break;
                }
            // The section's heading to the top of the list (as far as the list scrolls), once laid out.
            scroll.schedule.Execute(() => scroll.scrollOffset = new Vector2(0f, section.heading.layout.y));
            MarkSection(section);
        }

        void OnHighlight(SettingRow row)
        {
            if (row == null) return;
            int index = IndexOf(row);
            var section = sections.Find(s => index >= s.first && index < s.end);
            // A section's first setting brings its heading into view too.
            ScrollTo(section != null && FirstSelectable(section) == index ? section.heading : row.element);
            MarkSection(section);
        }

        void MarkSection(Section current)
        {
            foreach (var s in sections) s.side.EnableInClassList(CurrentSection, s == current);
        }

        void ScrollTo(VisualElement element)
        {
            if (scroll.contentContainer.layout.height > 0f) scroll.ScrollTo(element);
            else scroll.schedule.Execute(() => scroll.ScrollTo(element)); // not laid out yet (just opened)
        }

        int IndexOf(SettingRow row)
        {
            for (int i = 0; i < list.Rows.Count; i++)
                if (list.Rows[i] == row) return i;
            return -1;
        }

        int FirstSelectable(Section section)
        {
            for (int i = section.first; i < section.end; i++)
                if (list.Rows[i].Selectable) return i;
            return -1;
        }
    }
}
