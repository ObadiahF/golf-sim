using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// The practice card at the top right of the HUD on a practice facility: what it is and the session so far
    /// (a headline, a detail line and a small table, e.g. each club's averages or made putts by kind). Built in code
    /// on the HUD's document like the putt card, styled with RoundHud.uss's card classes.
    /// </summary>
    public class PracticeHud
    {
        const string Hidden = "hud--hidden";
        const float FirstColumn = 150f, Column = 96f;

        readonly VisualElement card, table;
        readonly Label eyebrow, headline, detail;

        public PracticeHud(VisualElement root)
        {
            card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.AddToClassList("hud");
            card.style.left = StyleKeyword.Auto;
            card.style.right = 28;
            card.style.top = 160; // below the toast, which can run across the top right
            card.style.maxWidth = 620;
            eyebrow = card.AddLabel("hud__eyebrow");
            headline = card.AddLabel("hud__player");
            headline.style.marginBottom = 2;
            detail = card.AddLabel("hud__caption");
            detail.style.whiteSpace = WhiteSpace.Normal;
            table = new VisualElement { pickingMode = PickingMode.Ignore };
            table.style.marginTop = 12;
            card.Add(table);
            (root.Q("hud-root") ?? root).Add(card); // inside the tree RoundHud.uss styles
            Hide();
        }

        public void Hide() => card.AddToClassList(Hidden);

        /// <summary>Shows the card. header: the table's column captions (null: no table); rows: one string per column.</summary>
        public void Show(string eyebrowText, string headlineText, string detailText, string[] header = null, IReadOnlyList<string[]> rows = null)
        {
            card.RemoveFromClassList(Hidden);
            eyebrow.text = eyebrowText;
            headline.text = headlineText;
            detail.text = detailText;
            table.Clear();
            if (header == null || rows == null) return;
            table.Add(Row(header, "hud__caption"));
            foreach (var cells in rows) table.Add(Row(cells, "hud__value"));
        }

        static VisualElement Row(string[] cells, string cls)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row;
            for (int i = 0; i < cells.Length; i++)
            {
                var label = row.AddLabel(cls);
                label.text = cells[i];
                label.style.width = i == 0 ? FirstColumn : Column;
                label.style.unityTextAlign = i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
                if (cls == "hud__value") label.style.fontSize = 20;
            }
            return row;
        }
    }
}
