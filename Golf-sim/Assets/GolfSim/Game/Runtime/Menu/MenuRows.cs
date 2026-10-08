using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// One line of a menu list (the Settings screen, the course screen's options): a name, a control and an optional
    /// note under it. Up/Down picks a row (RowList), Left/Right steps its value, Select activates it. Styled by
    /// Settings.uss ("setting" and friends).
    /// </summary>
    public abstract class SettingRow
    {
        const string SelectedClass = "setting--selected";

        public readonly VisualElement element = new VisualElement();
        protected readonly Label note;

        protected SettingRow(string name, string noteText = null)
        {
            element.AddToClassList("setting");
            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.AddToClassList("setting__line");
            line.Add(MenuUi.Text(name, "setting__name"));
            Control = new VisualElement();
            Control.AddToClassList("setting__control");
            line.Add(Control);
            element.Add(line);
            note = MenuUi.Text(noteText ?? "", "setting__note");
            note.EnableInClassList("setting__note--empty", string.IsNullOrEmpty(noteText));
            element.Add(note);
        }

        /// <summary>Where the row's control goes (pills, a bar, a value, a button).</summary>
        protected VisualElement Control { get; }

        /// <summary>False for a read-only line: Up/Down passes over it.</summary>
        public virtual bool Selectable => true;

        public bool Selected
        {
            set => element.EnableInClassList(SelectedClass, value);
        }

        /// <summary>Shows the current value (and refreshes a live line such as the connection).</summary>
        public abstract void Render();

        /// <summary>Left (-1) / Right (+1).</summary>
        public virtual void Step(int direction) { }

        /// <summary>Select: by default the next value.</summary>
        public virtual void Activate() => Step(1);

        protected void SetNote(string text)
        {
            note.text = text ?? "";
            note.EnableInClassList("setting__note--empty", string.IsNullOrEmpty(text));
        }
    }

    /// <summary>A choice between a few values shown as pills (Off / Light / Normal / Strong); a click picks one.</summary>
    public class ChoiceSetting : SettingRow
    {
        const string ActiveClass = "pill--active";

        readonly Func<int> get;
        readonly Action<int> set;
        readonly Button[] pills;
        readonly Func<int, string> describe;

        /// <summary>get/set: the chosen index into labels. describe (optional): the note for the chosen value.</summary>
        public ChoiceSetting(string name, string[] labels, Func<int> get, Action<int> set, Func<int, string> describe = null)
            : base(name)
        {
            this.get = get;
            this.set = set;
            this.describe = describe;
            Control.AddToClassList("pills");
            pills = new Button[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                pills[i] = new Button(() => Choose(index)) { text = labels[i], focusable = false }; // keys come from NavInput
                pills[i].AddToClassList("pill");
                Control.Add(pills[i]);
            }
        }

        /// <summary>A setting stored as an enum: one pill per value, in order.</summary>
        public static ChoiceSetting For<T>(string name, string[] labels, Func<T> get, Action<T> set, Func<T, string> describe = null)
            where T : Enum =>
            new ChoiceSetting(name, labels, () => Convert.ToInt32(get()), i => set((T)Enum.ToObject(typeof(T), i)),
                              describe == null ? null : i => describe((T)Enum.ToObject(typeof(T), i)));

        /// <summary>On / Off.</summary>
        public static ChoiceSetting Toggle(string name, Func<bool> get, Action<bool> set, Func<bool, string> describe = null) =>
            new ChoiceSetting(name, new[] { "Off", "On" }, () => get() ? 1 : 0, i => set(i == 1),
                              describe == null ? null : i => describe(i == 1));

        public int Index => get();

        public override void Render()
        {
            int current = get();
            for (int i = 0; i < pills.Length; i++) pills[i].EnableInClassList(ActiveClass, i == current);
            if (describe != null) SetNote(describe(current));
        }

        /// <summary>Left/Right stop at the ends; Select (Activate) wraps around.</summary>
        public override void Step(int direction) => Choose(Mathf.Clamp(get() + direction, 0, pills.Length - 1));

        public override void Activate() => Choose((get() + 1) % pills.Length);

        void Choose(int index)
        {
            if (index != get()) set(index);
            Render();
        }
    }

    /// <summary>A read-only line (the room code, the version): skipped by Up/Down, refreshed while the screen is open.</summary>
    public class InfoRow : SettingRow
    {
        readonly Label value;
        readonly Func<string> text, noteText;

        public InfoRow(string name, Func<string> text, Func<string> noteText = null) : base(name)
        {
            this.text = text;
            this.noteText = noteText;
            value = MenuUi.Text("", "setting__value");
            Control.Add(value);
            element.AddToClassList("setting--info");
        }

        public override bool Selectable => false;

        public override void Render()
        {
            value.text = text();
            if (noteText != null) SetNote(noteText());
        }
    }

    /// <summary>A button line (Check for updates): Select or a click runs it; the note says how it went.</summary>
    public class ActionRow : SettingRow
    {
        readonly Action run;
        readonly Func<string> status;

        public ActionRow(string name, string buttonText, Action run, Func<string> status) : base(name)
        {
            this.run = run;
            this.status = status;
            var button = new Button(Activate) { text = buttonText, focusable = false };
            button.AddToClassList("pill");
            button.AddToClassList("pill--action");
            Control.Add(button);
        }

        public override void Render() => SetNote(status());

        public override void Activate()
        {
            run();
            Render();
        }
    }

    /// <summary>Small builders shared by the menus' code-built UI.</summary>
    public static class MenuUi
    {
        /// <summary>A label that ignores the pointer (clicks go to its row or card).</summary>
        public static Label Text(string text, string cssClass)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(cssClass);
            return label;
        }

        /// <summary>A new element with these classes.</summary>
        public static VisualElement Box(params string[] classes)
        {
            var box = new VisualElement();
            foreach (var c in classes) box.AddToClassList(c);
            return box;
        }

        /// <summary>(index + step) wrapped into 0..count-1.</summary>
        public static int Wrap(int index, int count) => count == 0 ? 0 : (index % count + count) % count;
    }
}
