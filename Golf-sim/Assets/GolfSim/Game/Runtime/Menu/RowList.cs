using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// A column of SettingRows driven by NavKeys: Up/Down moves the highlight over the selectable rows (read-only lines
    /// are passed over), Left/Right steps the highlighted row, Select activates it. The pointer highlights the row it
    /// is over. Moving past the first or last row returns false, so a screen can hand the highlight elsewhere.
    /// </summary>
    public class RowList
    {
        readonly List<SettingRow> rows;

        /// <summary>The highlighted row's index, or -1 (nothing highlighted: the highlight is elsewhere on the screen).</summary>
        public int Highlighted { get; private set; } = -1;
        public SettingRow Current => Highlighted >= 0 ? rows[Highlighted] : null;
        public IReadOnlyList<SettingRow> Rows => rows;

        /// <summary>The highlight moved (by keys or the pointer).</summary>
        public event Action<SettingRow> HighlightChanged;

        public RowList(IEnumerable<SettingRow> rows)
        {
            this.rows = new List<SettingRow>(rows);
            for (int i = 0; i < this.rows.Count; i++)
            {
                int index = i;
                if (this.rows[i].Selectable) this.rows[i].element.RegisterCallback<PointerEnterEvent>(_ => Highlight(index));
            }
        }

        public void RenderAll()
        {
            foreach (var row in rows) row.Render();
        }

        /// <summary>Highlights this row (-1: none).</summary>
        public void Highlight(int index)
        {
            if (index >= rows.Count || index >= 0 && !rows[index].Selectable) return;
            if (index == Highlighted) return;
            Highlighted = index;
            for (int i = 0; i < rows.Count; i++) rows[i].Selected = i == Highlighted;
            HighlightChanged?.Invoke(Current);
        }

        /// <summary>The first (direction 1) or last (-1) selectable row.</summary>
        public void HighlightEnd(int direction) => Highlight(Next(direction > 0 ? -1 : rows.Count, direction));

        /// <summary>The next selectable row in this direction; false (nothing changes) past the end.</summary>
        public bool Move(int direction)
        {
            int next = Next(Highlighted, direction);
            if (next < 0) return false;
            Highlight(next);
            return true;
        }

        /// <summary>Up/Down move (false past an end), Left/Right step, Select activates; Back is the screen's.</summary>
        public bool Handle(NavKey key)
        {
            switch (key)
            {
                case NavKey.Up: return Move(-1);
                case NavKey.Down: return Move(1);
                case NavKey.Left: Current?.Step(-1); return Current != null;
                case NavKey.Right: Current?.Step(1); return Current != null;
                case NavKey.Select: Current?.Activate(); return Current != null;
                default: return false;
            }
        }

        int Next(int from, int direction)
        {
            for (int i = from + direction; i >= 0 && i < rows.Count; i += direction)
                if (rows[i].Selectable) return i;
            return -1;
        }
    }
}
