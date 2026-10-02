using System;
using System.Linq;
using GolfSim.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// The main menu's Scores overlay: everyone's stats from the game server (GET /api/players) as a ranked
    /// table. Left/Right (keyboard, gamepad or the phone's D-pad) or the tabs pick what to rank by; Back closes.
    /// </summary>
    public class ScoresScreen
    {
        const string Open = "scores--open", ActiveTab = "tab--selected", Current = "sc-cell--current";
        const int MaxRows = 12; // what fits on the TV; the rest are summarised in the status line

        /// <summary>A way to rank players: its tab title, how to read it and whether lower is better.</summary>
        readonly struct Ranking
        {
            public readonly string title;
            public readonly Func<PlayerStats, float> value;
            public readonly bool lowerIsBetter;
            public readonly Func<PlayerStats, string> format;

            public Ranking(string title, Func<PlayerStats, float> value, bool lowerIsBetter, Func<PlayerStats, string> format)
            {
                this.title = title;
                this.value = value;
                this.lowerIsBetter = lowerIsBetter;
                this.format = format;
            }
        }

        static readonly Ranking[] Rankings =
        {
            new Ranking("Handicap", p => p.handicap, true, p => Decimal(p.handicap, signed: false)),
            new Ranking("Average", p => p.averageToPar, true, p => Decimal(p.averageToPar, signed: true)),
            new Ranking("Best", p => p.bestTotal, true, p => GameApi.Has(p.bestTotal) ? p.bestTotal.ToString("0") : "—"),
            new Ranking("Wins", p => p.wins, false, p => p.wins.ToString()),
            new Ranking("Birdies", BirdiesOrBetter, false, p => BirdiesOrBetter(p).ToString("0")),
            new Ranking("Aces", p => p.holes.holesInOne, false, p => p.holes.holesInOne.ToString()),
        };

        readonly MonoBehaviour host;
        readonly VisualElement overlay, table;
        readonly Label status;
        readonly Button[] tabs;
        PlayerStats[] players;
        int ranking;

        public bool IsOpen { get; private set; }

        public ScoresScreen(VisualElement root, MonoBehaviour host)
        {
            this.host = host;
            overlay = root.Q("scores");
            table = root.Q("scores-table");
            status = root.Q<Label>("scores-status");
            var row = root.Q("scores-tabs");
            row.Clear();
            tabs = new Button[Rankings.Length];
            for (int i = 0; i < Rankings.Length; i++)
            {
                int index = i;
                tabs[i] = new Button(() => Rank(index)) { text = Rankings[i].title, focusable = false };
                tabs[i].AddToClassList("tab");
                row.Add(tabs[i]);
            }
            root.Q<Button>("scores-close").clicked += Close;
            root.Q<Button>("scores-close").focusable = false;
        }

        public void Show()
        {
            if (IsOpen) return;
            IsOpen = true;
            overlay.AddToClassList(Open);
            NavInput.Register(OnNav, NavInput.OverlayPriority);
            Rank(ranking);
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            overlay.RemoveFromClassList(Open);
            NavInput.Unregister(OnNav);
        }

        /// <summary>Fetches the stats again (each time the screen opens).</summary>
        public void Refresh()
        {
            status.text = "Loading scores…";
            table.Clear();
            host.StartCoroutine(GameApi.Players(
                result => { players = result; Render(); },
                error => status.text = $"Can't reach the game server ({error})."));
        }

        bool OnNav(NavKey key)
        {
            if (key == NavKey.Left) Rank(ranking - 1);
            else if (key == NavKey.Right) Rank(ranking + 1);
            else if (key is NavKey.Back or NavKey.Select) Close();
            return true; // modal
        }

        void Rank(int index)
        {
            ranking = (index % Rankings.Length + Rankings.Length) % Rankings.Length;
            for (int i = 0; i < tabs.Length; i++) tabs[i].EnableInClassList(ActiveTab, i == ranking);
            if (players != null) Render();
        }

        void Render()
        {
            table.Clear();
            var r = Rankings[ranking];
            var ranked = players.Where(p => Ranked(p, r)).OrderBy(p => r.lowerIsBetter ? r.value(p) : -r.value(p)).ThenBy(p => p.name).ToList();
            var rest = players.Where(p => !Ranked(p, r)).OrderBy(p => p.name).ToList();
            if (players.Length == 0 || players.All(p => p.holes.played == 0))
            {
                status.text = "No rounds yet. Play a round to get on the board.";
                return;
            }
            status.text = ranked.Count == 0
                ? $"Nobody has a {r.title.ToLowerInvariant()} yet{(r.title == "Handicap" ? " (it needs 3 finished rounds)" : "")}."
                : $"Ranked by {r.title.ToLowerInvariant()}{(r.lowerIsBetter ? ", lowest first" : "")}.";

            var head = ScoreTable.Row(table, "sc-row--head");
            ScoreTable.Cell(head, "#", "sc-cell--rank");
            ScoreTable.Cell(head, "PLAYER", "sc-cell--name");
            for (int i = 0; i < Rankings.Length; i++) ScoreTable.Cell(head, Rankings[i].title.ToUpperInvariant(), i == ranking ? "sc-cell--stat " + Current : "sc-cell--stat");
            ScoreTable.Cell(head, "ROUNDS", "sc-cell--stat");

            var shown = ranked.Concat(rest).ToList();
            if (shown.Count > MaxRows) status.text += $" Showing the top {MaxRows} of {shown.Count} players.";
            foreach (var p in shown.Take(MaxRows))
            {
                bool hasRank = ranked.Contains(p);
                int place = hasRank ? 1 + ranked.Count(q => Better(q, p, r)) : 0;
                var row = ScoreTable.Row(table, place == 1 ? "sc-row--leader" : null);
                ScoreTable.Cell(row, hasRank ? place.ToString() : "—", "sc-cell--rank");
                ScoreTable.Cell(row, p.name, "sc-cell--name");
                for (int i = 0; i < Rankings.Length; i++) ScoreTable.Cell(row, Rankings[i].format(p), i == ranking ? "sc-cell--stat " + Current : "sc-cell--stat");
                ScoreTable.Cell(row, p.finishedRounds.ToString(), "sc-cell--stat");
            }
        }

        /// <summary>Players without the stat (no finished rounds, or no handicap yet) are listed unranked at the bottom.</summary>
        static bool Ranked(PlayerStats p, Ranking r) => GameApi.Has(r.value(p)) && (r.lowerIsBetter ? p.finishedRounds > 0 : r.value(p) > 0);

        static bool Better(PlayerStats a, PlayerStats b, Ranking r) => r.lowerIsBetter ? r.value(a) < r.value(b) : r.value(a) > r.value(b);

        static float BirdiesOrBetter(PlayerStats p) => p.holes.holesInOne + p.holes.eagles + p.holes.birdies;

        static string Decimal(float value, bool signed) =>
            !GameApi.Has(value) ? "—" : signed && value > 0 ? $"+{value:0.0}" : value.ToString("0.0");
    }
}
