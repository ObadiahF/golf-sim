using GolfSim.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// The in-game overlay (RoundHud.uxml): hole, par, stroke, club, aim, distance and lie (rendered from the
    /// same StateMessage the phone gets), the turn announcement and current-player badge (TurnBanner), short
    /// toasts, the between-holes / final scorecard and the fade curtain for loading holes.
    /// </summary>
    public class RoundHud
    {
        const string Hidden = "hud--hidden", Open = "scorecard--open", ToastShown = "toast--shown";

        public readonly ScreenFade Fade;
        public readonly TurnBanner Banner;

        readonly VisualElement info, hint, scorecard, table;
        readonly Label eyebrow, player, strokes, club, aim, distance, lie, toast, title, subtitle, footer;
        IVisualElementScheduledItem hideToast;

        public RoundHud(UIDocument document)
        {
            var root = document.rootVisualElement;
            info = root.Q("hud-info");
            hint = root.Q("hud-hint");
            scorecard = root.Q("scorecard");
            table = root.Q("scorecard-table");
            eyebrow = root.Q<Label>("hud-eyebrow");
            player = root.Q<Label>("hud-player");
            strokes = root.Q<Label>("hud-strokes");
            club = root.Q<Label>("hud-club");
            aim = root.Q<Label>("hud-aim");
            distance = root.Q<Label>("hud-distance");
            lie = root.Q<Label>("hud-lie");
            toast = root.Q<Label>("hud-toast");
            title = root.Q<Label>("scorecard-title");
            subtitle = root.Q<Label>("scorecard-subtitle");
            footer = root.Q<Label>("scorecard-hint");
            root.pickingMode = PickingMode.Ignore;
            Banner = new TurnBanner(root);
            Fade = new ScreenFade(root);
            ShowInfo(false);
        }

        public void ShowInfo(bool show)
        {
            info.EnableInClassList(Hidden, !show);
            hint.EnableInClassList(Hidden, !show);
        }

        /// <summary>Shows a state: the HUD in game, nothing on other screens. lieLabel: the lie with its effect ("Rough −12%").</summary>
        public void Render(StateMessage s, string lieLabel = null)
        {
            bool playing = s.screen == StateMessage.Game || s.screen == StateMessage.Paused;
            bool inRound = !string.IsNullOrEmpty(s.currentPlayer);
            ShowInfo(playing);
            if (!playing || !inRound) Banner.HideBadge();
            if (!playing) return;
            eyebrow.text = inRound ? $"HOLE {s.hole}  ·  PAR {s.par}" : $"PRACTICE  ·  PAR {s.par}";
            // In a round the player's name is on the turn badge (top right).
            player.EnableInClassList(Hidden, inRound);
            player.text = "Practice";
            if (inRound) Banner.UpdateBadge(TurnInfo(s.hole, s.par, s.strokes));
            strokes.text = (s.strokes + 1).ToString();
            club.text = s.club;
            aim.text = Aim(s.aim);
            distance.text = $"{s.distanceToPin:0} yd";
            lie.text = lieLabel ?? Capitalize(s.lie);
        }

        public void Toast(string text, float seconds = 2.5f)
        {
            toast.text = text;
            toast.AddToClassList(ToastShown);
            hideToast?.Pause();
            hideToast = toast.schedule.Execute(() => toast.RemoveFromClassList(ToastShown)).StartingIn((long)(seconds * 1000));
        }

        public void ShowScorecard(Round round, string heading, string sub, string hintText)
        {
            title.text = heading;
            subtitle.text = sub;
            footer.text = hintText;
            ScoreTable.Scorecard(table, round);
            scorecard.AddToClassList(Open);
        }

        public void HideScorecard() => scorecard.RemoveFromClassList(Open);

        public bool ScorecardOpen => scorecard.ClassListContains(Open);

        /// <summary>"Hole 3 · Par 4 · Stroke 2" (strokes = taken so far, so this is the next one).</summary>
        public static string TurnInfo(int hole, int par, int strokes) => $"Hole {hole}  ·  Par {par}  ·  Stroke {strokes + 1}";

        public static string ToPar(int toPar) => toPar == 0 ? "E" : toPar > 0 ? $"+{toPar}" : toPar.ToString();

        static string Aim(float degrees) =>
            Mathf.Abs(degrees) < 0.05f ? "At pin" : $"{Mathf.Abs(degrees):0.#}° {(degrees > 0 ? "R" : "L")}";

        static string Capitalize(string s) => string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
