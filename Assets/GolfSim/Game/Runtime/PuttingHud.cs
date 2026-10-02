using GolfSim.Ball;
using GolfSim.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// The putt card at the bottom left of the HUD in putting mode: distance in feet and metres, the rise or fall to
    /// the hole, how long it plays, the green speed and the assist level, plus a power bar after each putt
    /// ("Putted 4.2 m of 5.0 m", the strength against the read). Built in code on the HUD's document and styled
    /// with RoundHud.uss's card classes.
    /// </summary>
    public class PuttingHud
    {
        const string Hidden = "hud--hidden";
        const float BarWidth = 360f;

        /// <summary>One putt for the result bar, metres.</summary>
        public class Stroke
        {
            /// <summary>Flat distance to the pin when it was struck.</summary>
            public float target;
            /// <summary>How far the stroke would roll on a flat green (its strength, PuttModel).</summary>
            public float hitFor;
            /// <summary>The strength the read asked for (PuttPreview.PlaysAs).</summary>
            public float playsAs;
            public float rolled;
            public bool holed, done;

            /// <summary>"Putted 4.2 m of 5.0 m" (the app says the same), or "Holed from 5.0 m!".</summary>
            public string Summary => holed ? $"Holed from {target:0.0} m!" : $"Putted {rolled:0.0} m of {target:0.0} m";
        }

        readonly VisualElement card, result, fill, marker;
        readonly Label distance, elevation, playsAs, green, assist, resultText, resultDetail;

        public PuttingHud(VisualElement root)
        {
            card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.AddToClassList("hud");
            card.style.top = StyleKeyword.Auto;
            card.style.bottom = 70;
            card.Add(Text("PUTT", "hud__eyebrow"));
            distance = card.AddLabel("hud__player");
            var stats = new VisualElement { pickingMode = PickingMode.Ignore };
            stats.AddToClassList("hud__stats");
            elevation = Stat(stats, "SLOPE TO HOLE");
            playsAs = Stat(stats, "PLAYS LIKE");
            green = Stat(stats, "GREEN");
            assist = Stat(stats, "READ (P)");
            card.Add(stats);

            result = new VisualElement { pickingMode = PickingMode.Ignore };
            result.style.marginTop = 14;
            resultText = result.AddLabel("hud__value");
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.style.width = BarWidth;
            track.style.height = 16;
            track.style.marginTop = 6;
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
            SetRadius(track, 8);
            fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.style.height = Length.Percent(100);
            fill.style.backgroundColor = new Color(0.98f, 0.82f, 0.25f);
            SetRadius(fill, 8);
            marker = new VisualElement { pickingMode = PickingMode.Ignore };
            marker.style.position = Position.Absolute;
            marker.style.width = 4;
            marker.style.top = -5;
            marker.style.bottom = -5;
            marker.style.backgroundColor = Color.white;
            track.Add(fill);
            track.Add(marker);
            result.Add(track);
            resultDetail = result.AddLabel("hud__caption");
            resultDetail.style.marginTop = 6;
            card.Add(result);

            (root.Q("hud-root") ?? root).Add(card); // inside the tree RoundHud.uss styles
            Hide();
        }

        public void Hide() => card.AddToClassList(Hidden);

        /// <summary>Shows the putt card for this state (null hides it) and the last putt's bar if there is one.</summary>
        public void Render(StateMessage s, Stroke last)
        {
            card.EnableInClassList(Hidden, s == null);
            if (s == null) return;
            distance.text = $"{s.puttDistance / PuttModel.MetersPerFoot:0.0} ft  ·  {s.puttDistance:0.0} m";
            elevation.text = Slope(s.elevation);
            playsAs.text = $"{s.puttPlaysAs:0.0} m";
            green.text = $"Stimp {s.stimp:0.#}";
            assist.text = char.ToUpperInvariant(s.puttingAssist[0]) + s.puttingAssist.Substring(1);
            ShowStroke(last);
        }

        /// <summary>The result bar: strength against the read, and how far it actually went.</summary>
        public void ShowStroke(Stroke stroke)
        {
            result.EnableInClassList(Hidden, stroke == null || !stroke.done);
            if (stroke == null || !stroke.done) return;
            resultText.text = stroke.Summary;
            float scale = Mathf.Max(stroke.playsAs * 1.5f, stroke.hitFor * 1.1f, 0.5f);
            fill.style.width = Length.Percent(Mathf.Clamp01(stroke.hitFor / scale) * 100f);
            marker.style.left = Mathf.Clamp01(stroke.playsAs / scale) * BarWidth - 2f;
            float off = stroke.hitFor / Mathf.Max(stroke.playsAs, 0.01f) - 1f;
            string verdict = Mathf.Abs(off) < 0.05f ? "right on the read" : $"{Mathf.Abs(off) * 100f:0}% {(off > 0 ? "firm" : "soft")}";
            resultDetail.text = $"STRENGTH {stroke.hitFor:0.0} M  ·  READ {stroke.playsAs:0.0} M  ·  {verdict.ToUpperInvariant()}";
        }

        /// <summary>"12 cm up", "8 cm down" or "Flat".</summary>
        public static string Slope(double metres)
        {
            int cm = (int)System.Math.Round(metres * 100.0);
            return cm == 0 ? "Flat" : $"{Mathf.Abs(cm)} cm {(cm > 0 ? "up" : "down")}";
        }

        static Label Text(string text, string cls)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(cls);
            return label;
        }

        static Label Stat(VisualElement row, string caption)
        {
            var stat = new VisualElement { pickingMode = PickingMode.Ignore };
            stat.AddToClassList("hud__stat");
            stat.Add(Text(caption, "hud__caption"));
            var value = stat.AddLabel("hud__value");
            row.Add(stat);
            return value;
        }

        static void SetRadius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r;
            e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r;
            e.style.borderBottomRightRadius = r;
        }
    }

    static class PuttingHudExtensions
    {
        /// <summary>Adds an empty label with this class.</summary>
        public static Label AddLabel(this VisualElement parent, string cls)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList(cls);
            parent.Add(label);
            return label;
        }
    }
}
