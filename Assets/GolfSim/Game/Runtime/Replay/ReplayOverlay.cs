using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// The replay's broadcast graphics on their own UI document over the HUD: letterbox bars, a "REPLAY" bug with the
    /// player and the shot (club, carry, total), a skip hint, a dip-to-black curtain for the transitions in and out,
    /// and between turns a small "Up: Replay" prompt. The director drives every value each frame (no USS transitions).
    /// </summary>
    public class ReplayOverlay
    {
        const float BarHeight = 11f; // % of the screen height per bar (about 2.39:1 between them)
        static readonly Color Gold = new Color32(250, 204, 21, 255);
        static readonly Color Ink = new Color32(8, 12, 16, 255);

        readonly VisualElement root, top, bottom, bug, curtain, prompt;
        readonly Label player, detail;

        public ReplayOverlay(UIDocument document)
        {
            root = document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            Fill(root);
            top = Bar(true);
            bottom = Bar(false);

            bug = Box(root);
            bug.style.left = new Length(4f, LengthUnit.Percent);
            bug.style.top = new Length(BarHeight + 3f, LengthUnit.Percent);
            bug.style.flexDirection = FlexDirection.Row;
            bug.style.alignItems = Align.Stretch;
            var tag = new Label("REPLAY");
            Text(tag, 26f, Ink, true);
            tag.style.backgroundColor = Gold;
            tag.style.paddingLeft = tag.style.paddingRight = 16f;
            tag.style.unityTextAlign = TextAnchor.MiddleCenter;
            tag.style.letterSpacing = 4f;
            bug.Add(tag);
            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.style.paddingLeft = 16f;
            text.style.paddingRight = 20f;
            text.style.paddingTop = text.style.paddingBottom = 8f;
            bug.Add(text);
            player = new Label();
            Text(player, 28f, Color.white, true);
            text.Add(player);
            detail = new Label();
            Text(detail, 20f, new Color32(201, 212, 222, 255), false);
            text.Add(detail);

            var skip = new Label("Select: skip");
            Text(skip, 18f, new Color32(147, 161, 174, 255), false);
            skip.style.position = Position.Absolute;
            skip.style.right = 28f;
            skip.style.bottom = 14f;
            bottom.Add(skip);

            prompt = Box(root);
            prompt.style.bottom = 64f;
            prompt.style.right = 28f;
            prompt.style.paddingLeft = prompt.style.paddingRight = 18f;
            prompt.style.paddingTop = prompt.style.paddingBottom = 8f;
            var promptText = new Label("▲  Replay");
            Text(promptText, 20f, Gold, true);
            prompt.Add(promptText);

            curtain = new VisualElement { pickingMode = PickingMode.Ignore };
            Fill(curtain);
            curtain.style.backgroundColor = Color.black;
            root.Add(curtain);
            Show(0f, 0f, 0f);
            ShowPrompt(false);
        }

        public void SetShot(string who, string what)
        {
            player.text = who;
            detail.text = what;
            player.style.display = string.IsNullOrEmpty(who) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>bars: 0..1 letterbox; bug: 0..1 opacity of the bug; dark: 0..1 curtain.</summary>
        public void Show(float bars, float bug, float dark)
        {
            float h = BarHeight * Smooth(bars);
            top.style.height = bottom.style.height = new Length(h, LengthUnit.Percent);
            top.style.display = bottom.style.display = bars > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            this.bug.style.opacity = bug;
            this.bug.style.translate = new Translate(-24f * (1f - Smooth(bug)), 0f);
            this.bug.style.display = bug > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            curtain.style.opacity = dark;
            curtain.style.display = dark > 0f ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void ShowPrompt(bool show) => prompt.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;

        VisualElement Bar(bool atTop)
        {
            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.style.position = Position.Absolute;
            bar.style.left = bar.style.right = 0f;
            if (atTop) bar.style.top = 0f;
            else bar.style.bottom = 0f;
            bar.style.backgroundColor = Color.black;
            root.Add(bar);
            return bar;
        }

        static VisualElement Box(VisualElement parent)
        {
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.style.position = Position.Absolute;
            box.style.backgroundColor = new Color(0.03f, 0.05f, 0.07f, 0.86f);
            box.style.borderTopLeftRadius = box.style.borderTopRightRadius = box.style.borderBottomLeftRadius = box.style.borderBottomRightRadius = 10f;
            box.style.overflow = Overflow.Hidden;
            parent.Add(box);
            return box;
        }

        static void Fill(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = e.style.right = e.style.top = e.style.bottom = 0f;
        }

        static void Text(Label label, float size, Color color, bool bold)
        {
            label.pickingMode = PickingMode.Ignore;
            label.style.fontSize = size;
            label.style.color = color;
            label.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0f;
        }

        static float Smooth(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * (3f - 2f * u);
        }
    }
}
