using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// Wii-style announcements on the HUD: "ALICE'S TURN" big in the middle of the screen, which then shrinks
    /// and slides into the current-player badge in the top-right corner; and the winner banner with confetti.
    /// Each player has an accent colour (by turn order). Animations are USS transitions (RoundHud.uss).
    /// </summary>
    public class TurnBanner
    {
        const string Center = "turn-banner--center", BadgeShown = "turn-badge--shown";
        const int HoldMs = 1500, MoveMs = 550, ConfettiPieces = 70;

        /// <summary>Accent per player in turn order (the server allows 8 players).</summary>
        public static readonly Color[] Accents =
        {
            new Color32(52, 211, 153, 255), new Color32(96, 165, 250, 255), new Color32(251, 191, 36, 255), new Color32(251, 113, 133, 255),
            new Color32(167, 139, 250, 255), new Color32(251, 146, 60, 255), new Color32(45, 212, 191, 255), new Color32(163, 230, 53, 255),
        };

        readonly VisualElement root, banner, card, badge, badgeDot, confetti;
        readonly Label title, detail, badgeName, badgeDetail;
        IVisualElementScheduledItem step;
        int sequence;
        bool badgeWanted; // false once the turn is over (e.g. the hole ended during the announcement)

        public TurnBanner(VisualElement root)
        {
            this.root = root;
            banner = root.Q("turn-banner");
            card = root.Q("turn-banner-card");
            title = root.Q<Label>("turn-banner-title");
            detail = root.Q<Label>("turn-banner-detail");
            badge = root.Q("turn-badge");
            badgeDot = root.Q("turn-badge-dot");
            badgeName = root.Q<Label>("turn-badge-name");
            badgeDetail = root.Q<Label>("turn-badge-detail");
            confetti = root.Q("confetti");
        }

        public static Color AccentFor(int playerIndex) => Accents[(playerIndex % Accents.Length + Accents.Length) % Accents.Length];

        /// <summary>Big centred "NAME'S TURN" for 1.5 s, then it flies into the corner badge, which stays.</summary>
        public void AnnounceTurn(string player, int playerIndex, string info)
        {
            var color = AccentFor(playerIndex);
            badge.RemoveFromClassList(BadgeShown);
            badgeWanted = true;
            SetBadge(player, info, color);
            Show($"{player.ToUpperInvariant()}'S TURN", info, color);
            int id = sequence;
            Schedule(HoldMs, () => FlyToBadge(id));
        }

        /// <summary>Updates the badge text (e.g. the stroke count) without an announcement.</summary>
        public void UpdateBadge(string info) => badgeDetail.text = info;

        public void HideBadge()
        {
            badgeWanted = false;
            badge.RemoveFromClassList(BadgeShown);
        }

        /// <summary>The winner banner with confetti; onDone runs when it has faded (e.g. to show the scorecard).</summary>
        public void Celebrate(string headline, string info, Color color, Action onDone)
        {
            HideBadge();
            Show(headline, info, color);
            Confetti();
            int id = sequence;
            Schedule(3000, () =>
            {
                if (id != sequence) return;
                banner.RemoveFromClassList(Center);
                onDone?.Invoke();
            });
        }

        /// <summary>Stops any announcement (e.g. a new hole is loading).</summary>
        public void Clear()
        {
            sequence++;
            step?.Pause();
            banner.RemoveFromClassList(Center);
            ResetCard();
            HideBadge();
        }

        void Show(string headline, string info, Color color)
        {
            sequence++;
            step?.Pause();
            ResetCard();
            card.style.display = StyleKeyword.Null;
            title.text = headline;
            title.style.color = color;
            detail.text = info;
            SetBorder(card, color);
            banner.AddToClassList(Center);
        }

        void FlyToBadge(int id)
        {
            if (id != sequence) return;
            if (!badgeWanted)
            {
                banner.RemoveFromClassList(Center); // just fade out
                return;
            }
            Rect from = card.worldBound, to = badge.worldBound;
            if (from.width <= 0f || to.width <= 0f) { Land(id); return; }
            card.style.translate = new Translate(to.center.x - from.center.x, to.center.y - from.center.y);
            float s = Mathf.Clamp(to.width / from.width, 0.1f, 1f);
            card.style.scale = new Scale(new Vector2(s, s));
            Schedule(MoveMs, () => Land(id));
        }

        void Land(int id)
        {
            if (id != sequence) return;
            card.style.display = DisplayStyle.None; // hide instantly, then reset without animating back
            banner.RemoveFromClassList(Center);
            ResetCard();
            if (badgeWanted) badge.AddToClassList(BadgeShown);
            Schedule(50, () => card.style.display = StyleKeyword.Null);
        }

        void ResetCard()
        {
            card.style.translate = StyleKeyword.Null;
            card.style.scale = StyleKeyword.Null;
        }

        void SetBadge(string player, string info, Color color)
        {
            badgeName.text = player;
            badgeDetail.text = info;
            badgeDot.style.backgroundColor = color;
            SetBorder(badge, color);
        }

        static void SetBorder(VisualElement e, Color color)
        {
            e.style.borderTopColor = e.style.borderBottomColor = e.style.borderLeftColor = e.style.borderRightColor = color;
        }

        void Schedule(int ms, Action action) => step = root.schedule.Execute(action).StartingIn(ms);

        /// <summary>Coloured pieces that fall and tumble across the screen, then remove themselves.</summary>
        void Confetti()
        {
            confetti.Clear();
            float width = Mathf.Max(root.layout.width, 800f), height = Mathf.Max(root.layout.height, 600f);
            var rng = new System.Random();
            for (int i = 0; i < ConfettiPieces; i++)
            {
                var piece = new VisualElement { pickingMode = PickingMode.Ignore };
                piece.AddToClassList("confetti__piece");
                piece.style.left = (float)rng.NextDouble() * width;
                piece.style.backgroundColor = Accents[rng.Next(Accents.Length)];
                piece.style.transitionDuration = new StyleList<TimeValue>(new System.Collections.Generic.List<TimeValue> { new TimeValue(2.2f + (float)rng.NextDouble() * 1.8f) });
                piece.style.transitionDelay = new StyleList<TimeValue>(new System.Collections.Generic.List<TimeValue> { new TimeValue((float)rng.NextDouble() * 0.8f) });
                confetti.Add(piece);
                float drift = ((float)rng.NextDouble() - 0.5f) * 240f, fall = height + 80f, spin = ((float)rng.NextDouble() - 0.5f) * 1440f;
                piece.schedule.Execute(() =>
                {
                    piece.style.translate = new Translate(drift, fall);
                    piece.style.rotate = new Rotate(new Angle(spin));
                }).StartingIn(30);
            }
            confetti.schedule.Execute(() => confetti.Clear()).StartingIn(5500);
        }
    }
}
