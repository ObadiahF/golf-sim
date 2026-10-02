using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// Player names come from the app, so text is shown literally: rich text off, so "&lt;size=90&gt;Huge" or
    /// "&lt;color=red&gt;" can't restyle or break the layout.
    /// </summary>
    public static class PlainText
    {
        /// <summary>Turns rich text off for every text element under root (labels, buttons), root included.</summary>
        public static void Apply(VisualElement root) => root.Query<TextElement>().ForEach(Apply);

        public static void Apply(TextElement text) => text.enableRichText = false;
    }
}
