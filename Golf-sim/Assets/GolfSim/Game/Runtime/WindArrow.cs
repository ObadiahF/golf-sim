using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// Draws an arrow in a HUD element (in its text color) and turns it the way the wind blows relative to the aim:
    /// up is helping, right is left to right, down is into the player's face (StateMessage.windAngle).
    /// </summary>
    public class WindArrow
    {
        readonly VisualElement element;

        public WindArrow(VisualElement element)
        {
            this.element = element;
            element.generateVisualContent += Draw;
        }

        /// <summary>Points the arrow (degrees clockwise from up); hidden when calm.</summary>
        public void Show(bool calm, float angle)
        {
            element.style.visibility = calm ? Visibility.Hidden : Visibility.Visible;
            element.style.rotate = new Rotate(angle);
        }

        void Draw(MeshGenerationContext context)
        {
            var r = element.contentRect;
            float w = r.width, h = r.height;
            var p = context.painter2D;
            p.fillColor = element.resolvedStyle.color;
            p.BeginPath();
            p.MoveTo(new Vector2(w * 0.5f, 0f));            // the tip
            p.LineTo(new Vector2(w * 0.95f, h * 0.48f));
            p.LineTo(new Vector2(w * 0.64f, h * 0.48f));
            p.LineTo(new Vector2(w * 0.64f, h));             // the shaft
            p.LineTo(new Vector2(w * 0.36f, h));
            p.LineTo(new Vector2(w * 0.36f, h * 0.48f));
            p.LineTo(new Vector2(w * 0.05f, h * 0.48f));
            p.ClosePath();
            p.Fill();
        }
    }
}
