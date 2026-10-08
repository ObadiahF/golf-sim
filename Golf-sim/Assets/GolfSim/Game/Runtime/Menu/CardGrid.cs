using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>What a menu card shows: its art, title, a line under it and a corner badge; dimmed when it has nothing yet.</summary>
    public struct CardInfo
    {
        public string title, subtitle, badge;
        public Texture2D banner;
        public bool dimmed;
        /// <summary>An extra USS class (card--update).</summary>
        public string style;
    }

    /// <summary>
    /// A row or grid of picture cards (the main menu's channels, the courses, the practice modes). Left/Right moves
    /// through them in order (wrapping), Up/Down by a row in a grid; a click selects a card and a click on the selected
    /// one activates it (Select does that from the keyboard, gamepad or phone, through the screen that owns the grid).
    /// </summary>
    public class CardGrid
    {
        const string SelectedClass = "card--selected", DimmedClass = "card--dimmed";

        readonly VisualElement container;
        readonly int columns;
        readonly List<Button> cards = new List<Button>();
        readonly List<(Label title, Label subtitle, Label badge)> labels = new List<(Label, Label, Label)>();

        public int Selected { get; private set; } = -1;
        public int Count => cards.Count;

        /// <summary>The selection moved (keys or a click).</summary>
        public event Action<int> SelectionChanged;
        /// <summary>The selected card was clicked again.</summary>
        public event Action<int> Activated;

        /// <summary>columns: cards per row in a grid; 0 for a single row.</summary>
        public CardGrid(VisualElement container, int columns = 0)
        {
            this.container = container;
            this.columns = columns;
        }

        /// <summary>Replaces the cards and selects `select` (clamped).</summary>
        public void Set(IReadOnlyList<CardInfo> infos, int select = 0)
        {
            container.Clear();
            cards.Clear();
            labels.Clear();
            for (int i = 0; i < infos.Count; i++)
            {
                int index = i;
                var card = new Button(() => { if (Selected == index) Activated?.Invoke(index); else Select(index); }) { focusable = false };
                card.AddToClassList("card");
                var badge = MenuUi.Text("", "card__badge");
                var caption = MenuUi.Box("card__caption");
                caption.pickingMode = PickingMode.Ignore;
                var title = MenuUi.Text("", "card__title");
                var subtitle = MenuUi.Text("", "card__subtitle");
                caption.Add(title);
                caption.Add(subtitle);
                card.Add(badge);
                card.Add(caption);
                container.Add(card);
                cards.Add(card);
                labels.Add((title, subtitle, badge));
                Show(i, infos[i]);
            }
            Selected = -1;
            Select(Mathf.Clamp(select, 0, Mathf.Max(0, infos.Count - 1)));
        }

        /// <summary>Redraws one card (a new subtitle or badge) without rebuilding the grid.</summary>
        public void Show(int index, CardInfo info)
        {
            var card = cards[index];
            var (title, subtitle, badge) = labels[index];
            card.style.backgroundImage = info.banner;
            title.text = info.title;
            subtitle.text = info.subtitle ?? "";
            subtitle.style.display = string.IsNullOrEmpty(info.subtitle) ? DisplayStyle.None : DisplayStyle.Flex;
            badge.text = info.badge ?? "";
            badge.style.display = string.IsNullOrEmpty(info.badge) ? DisplayStyle.None : DisplayStyle.Flex;
            card.EnableInClassList(DimmedClass, info.dimmed);
            foreach (var c in card.GetClasses())
                if (c.StartsWith("card--") && c != SelectedClass && c != DimmedClass)
                {
                    card.RemoveFromClassList(c);
                    break;
                }
            if (!string.IsNullOrEmpty(info.style)) card.AddToClassList(info.style);
        }

        public void Select(int index)
        {
            if (cards.Count == 0) return;
            index = MenuUi.Wrap(index, cards.Count);
            if (index == Selected) return;
            Selected = index;
            for (int i = 0; i < cards.Count; i++) cards[i].EnableInClassList(SelectedClass, i == Selected);
            SelectionChanged?.Invoke(Selected);
        }

        /// <summary>Moves the selection; false when Up/Down would leave the grid (or a single row), so the screen can move on.</summary>
        public bool Move(NavKey key)
        {
            switch (key)
            {
                case NavKey.Left: Select(Selected - 1); return true;
                case NavKey.Right: Select(Selected + 1); return true;
                case NavKey.Up when columns > 0 && Selected >= columns: Select(Selected - columns); return true;
                case NavKey.Down when columns > 0 && Row(Selected) < Row(cards.Count - 1):
                    Select(Mathf.Min(Selected + columns, cards.Count - 1));
                    return true;
                default: return false;
            }
        }

        int Row(int index) => columns > 0 ? index / columns : 0;
    }
}
