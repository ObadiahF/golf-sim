using System.Linq;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// Table building for the scorecard (RoundHud) and the Scores screen: rows of .sc-cell labels (Menu.uss).
    /// The scorecard is laid out like a real card: up to 9 holes in one block, more as front 9 + OUT, back 9 + IN,
    /// then TOT and to-par.
    /// </summary>
    public static class ScoreTable
    {
        public static VisualElement Row(VisualElement table, string extraClass = null)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("sc-row");
            if (extraClass != null) row.AddToClassList(extraClass);
            table.Add(row);
            return row;
        }

        /// <summary>A cell; classes is a space-separated list of extra USS classes (may be null).</summary>
        public static Label Cell(VisualElement row, string text, string classes = null)
        {
            var cell = new Label(text) { pickingMode = PickingMode.Ignore };
            cell.AddToClassList("sc-cell");
            if (classes != null)
                foreach (var c in classes.Split(' ').Where(c => c.Length > 0)) cell.AddToClassList(c);
            row.Add(cell);
            return cell;
        }

        /// <summary>Fills the table with the round's scorecard; the current hole's column is highlighted.</summary>
        public static void Scorecard(VisualElement table, Round round)
        {
            table.Clear();
            var segments = round.holeCount > 9
                ? new[] { (from: 0, to: 9, label: "OUT"), (from: 9, to: round.holeCount, label: "IN") }
                : new[] { (from: 0, to: round.holeCount, label: (string)null) };
            var leaders = round.Leaders();

            var head = Row(table, "sc-row--head");
            Cell(head, "HOLE", "sc-cell--name");
            foreach (var s in segments)
            {
                for (int h = s.from; h < s.to; h++) Cell(head, (h + 1).ToString(), Highlight(round, h));
                if (s.label != null) Cell(head, s.label, "sc-cell--total");
            }
            Cell(head, "TOT", "sc-cell--total");
            Cell(head, "±", "sc-cell--total");

            var pars = Row(table, "sc-row--par");
            Cell(pars, "PAR", "sc-cell--name");
            foreach (var s in segments)
            {
                for (int h = s.from; h < s.to; h++) Cell(pars, Blank(round.pars[h]), Highlight(round, h));
                if (s.label != null) Cell(pars, Blank(Sum(round.pars, s.from, s.to)), "sc-cell--total");
            }
            Cell(pars, Blank(round.pars.Sum()), "sc-cell--total");
            Cell(pars, "", "sc-cell--total");

            for (int p = 0; p < round.players.Length; p++)
            {
                var row = Row(table, leaders.Contains(round.players[p]) ? "sc-row--leader" : null);
                Cell(row, round.players[p], "sc-cell--name");
                foreach (var s in segments)
                {
                    for (int h = s.from; h < s.to; h++)
                    {
                        int score = round.scores[p][h];
                        string vsPar = score > 0 && round.pars[h] > 0 && score != round.pars[h] ? score < round.pars[h] ? " sc-cell--under" : " sc-cell--over" : "";
                        Cell(row, Blank(score), Highlight(round, h) + vsPar);
                    }
                    if (s.label != null) Cell(row, Blank(Sum(round.scores[p], s.from, s.to)), "sc-cell--total");
                }
                Cell(row, round.Total(p).ToString(), "sc-cell--total");
                Cell(row, RoundHud.ToPar(round.ToPar(p)), "sc-cell--total");
            }
        }

        static string Highlight(Round round, int hole) => hole == round.HoleIndex ? "sc-cell--current" : null;

        static string Blank(int value) => value > 0 ? value.ToString() : "";

        static int Sum(int[] values, int from, int to) => values.Skip(from).Take(to - from).Sum();
    }
}
