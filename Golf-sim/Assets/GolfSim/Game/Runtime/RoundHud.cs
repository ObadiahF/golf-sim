using GolfSim.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// The in-game overlay (RoundHud.uxml): hole, par, stroke, club, aim, distance and lie (rendered from the
    /// same StateMessage the phone gets), the turn announcement and current-player badge (TurnBanner), short
    /// toasts, the between-holes / final scorecard and the fade curtain for loading holes.
    /// What shows is decided here from the state alone (Render), so every screen change puts the HUD back as it should
    /// be: hidden during an instant replay, the stats in game and paused, the key hints only in game.
    /// </summary>
    public class RoundHud
    {
        const string Hidden = "hud--hidden", Open = "scorecard--open", ToastShown = "toast--shown", LoadingOpen = "loading--open";

        public readonly ScreenFade Fade;
        public readonly TurnBanner Banner;

        readonly Label hint;
        readonly string defaultHint;
        readonly VisualElement hudRoot, info, scorecard, table, loading, loadingFill;
        readonly Label eyebrow, player, strokesCaption, strokes, club, aim, distance, lie, toast, title, subtitle, footer, loadingTitle, loadingDetail;
        IVisualElementScheduledItem hideToast;

        public RoundHud(UIDocument document)
        {
            var root = document.rootVisualElement;
            hudRoot = root.Q("hud-root");
            info = root.Q("hud-info");
            hint = root.Q<Label>("hud-hint");
            defaultHint = hint.text;
            scorecard = root.Q("scorecard");
            table = root.Q("scorecard-table");
            eyebrow = root.Q<Label>("hud-eyebrow");
            player = root.Q<Label>("hud-player");
            strokesCaption = root.Q<Label>("hud-strokes-caption");
            strokes = root.Q<Label>("hud-strokes");
            club = root.Q<Label>("hud-club");
            aim = root.Q<Label>("hud-aim");
            distance = root.Q<Label>("hud-distance");
            lie = root.Q<Label>("hud-lie");
            toast = root.Q<Label>("hud-toast");
            title = root.Q<Label>("scorecard-title");
            subtitle = root.Q<Label>("scorecard-subtitle");
            footer = root.Q<Label>("scorecard-hint");
            loading = root.Q("loading");
            loadingFill = root.Q("loading-fill");
            loadingTitle = root.Q<Label>("loading-title");
            loadingDetail = root.Q<Label>("loading-detail");
            root.pickingMode = PickingMode.Ignore;
            PlainText.Apply(root); // names (badge, banner, toasts, scorecard) come from the app
            Banner = new TurnBanner(root);
            Fade = new ScreenFade(root);
            info.AddToClassList(Hidden);
            hint.AddToClassList(Hidden);
        }

        /// <summary>The key hints along the bottom: these (a practice facility's own keys), or the usual ones.</summary>
        public void KeyHint(string text) => hint.text = text ?? defaultHint;

        /// <summary>
        /// Shows a state: the HUD in game (the stats also under the pause menu), nothing on other screens, and none of
        /// it during a replay. lieLabel: the lie with its effect ("Rough −12%"). current: the player up, whose
        /// finished hole (holed or picked up) shows as their score instead of a next stroke.
        /// </summary>
        public void Render(StateMessage s, string lieLabel = null, PlayerBall current = null)
        {
            bool playing = s.screen == StateMessage.Game || s.screen == StateMessage.Paused;
            bool inRound = !string.IsNullOrEmpty(s.currentPlayer);
            hudRoot.EnableInClassList(Hidden, s.screen == StateMessage.Replay); // a class: an inline display reset to Null could leave it hidden
            info.EnableInClassList(Hidden, !playing);
            hint.EnableInClassList(Hidden, s.screen != StateMessage.Game);
            if (!inRound) Banner.HideBadge();
            Banner.Cover(!playing);
            if (!playing) return;
            eyebrow.text = Eyebrow(s);
            // In a round the player's name is on the turn badge (top right).
            player.EnableInClassList(Hidden, inRound);
            player.text = "Practice";
            bool finished = current is { Done: true };
            if (inRound) Banner.UpdateBadge(finished ? FinishedInfo(s.hole, s.par, current) : TurnInfo(s.hole, s.par, s.strokes));
            bool facility = !string.IsNullOrEmpty(s.practice); // the range or putting green: its shots so far
            strokesCaption.text = finished ? "SCORE" : facility ? "SHOTS" : "STROKE";
            strokes.text = (finished ? s.strokes : facility ? s.attempts : s.strokes + 1).ToString();
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

        /// <summary>The download overlay over everything (the menu included): progress 0..1.</summary>
        public void ShowLoading(string heading, string detail, float progress)
        {
            loadingTitle.text = heading;
            loadingDetail.text = detail;
            loadingFill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100f);
            loading.AddToClassList(LoadingOpen);
        }

        public void HideLoading() => loading.RemoveFromClassList(LoadingOpen);

        public bool ScorecardOpen => scorecard.ClassListContains(Open);

        /// <summary>"HOLE 3  ·  PAR 4" in a round, "DRIVING RANGE" on a facility, "PRACTICE  ·  PAR 4" on the practice hole (the stats card and the course map).</summary>
        internal static string Eyebrow(StateMessage s) =>
            !string.IsNullOrEmpty(s.currentPlayer) ? $"HOLE {s.hole}  ·  PAR {s.par}" : PracticeFacility.TitleFor(s.practice) ?? $"PRACTICE  ·  PAR {s.par}";

        /// <summary>"Hole 3 · Par 4 · Stroke 2" (strokes = taken so far, so this is the next one).</summary>
        public static string TurnInfo(int hole, int par, int strokes) => HoleLine(hole, par, $"Stroke {strokes + 1}");

        /// <summary>"Hole 3 · Par 4 · Picked up: 9" or "… · Holed: 4" once the player's hole is over.</summary>
        public static string FinishedInfo(int hole, int par, PlayerBall ball) =>
            HoleLine(hole, par, $"{(ball.pickedUp ? "Picked up" : "Holed")}: {ball.strokes}");

        static string HoleLine(int hole, int par, string detail) => $"Hole {hole}  ·  Par {par}  ·  {detail}";

        public static string ToPar(int toPar) => toPar == 0 ? "E" : toPar > 0 ? $"+{toPar}" : toPar.ToString();

        internal static string Aim(float degrees) =>
            Mathf.Abs(degrees) < 0.05f ? "At pin" : $"{Mathf.Abs(degrees):0.#}° {(degrees > 0 ? "R" : "L")}";

        static string Capitalize(string s) => string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
