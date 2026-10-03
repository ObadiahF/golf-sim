using GolfSim.Course;
using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// A map view's hole picture (HoleMapPainter) with its live overlay (MapOverlay) on top, shared by the course map
    /// and the mini map. The picture is painted to the view's shape once per hole, and again if the view changes shape.
    /// </summary>
    public class HolePicture
    {
        const float TextureScale = 1.25f; // texture pixels per panel pixel (a 1080p panel on a 4K TV stays crisp enough)
        const int MaxTexture = 1600;

        readonly VisualElement view, image;
        readonly MapOverlay overlay;
        readonly string owner;
        HoleInfo paintedHole;
        Vector2 paintedSize;
        MapScene scene;

        /// <summary>Fills `view` with the picture and the overlay; `owner` names it in the log.</summary>
        public HolePicture(VisualElement view, MapOverlay overlay, string owner)
        {
            this.view = view;
            this.overlay = overlay;
            this.owner = owner;
            image = new VisualElement { pickingMode = PickingMode.Ignore };
            image.AddToClassList("map__image");
            view.Add(image);
            view.Add(overlay);
            view.RegisterCallback<GeometryChangedEvent>(_ => Show(scene));
        }

        public MapFrame Frame { get; private set; }
        public Texture2D Texture { get; private set; }

        /// <summary>Paints the hole's picture ahead of time (e.g. while the hole fades in), so showing it doesn't stall.</summary>
        public void Prepare(HoleInfo hole)
        {
            if (hole && hole != paintedHole) Paint(hole);
        }

        /// <summary>Draws the scene (null = nothing over the picture), painting its hole first if needed.</summary>
        public void Show(MapScene shown)
        {
            scene = shown;
            if (scene != null && scene.hole && (scene.hole != paintedHole || Changed(view.contentRect.size))) Paint(scene.hole);
            overlay.Show(scene != null && scene.hole && scene.hole == paintedHole ? scene : null, Frame);
        }

        bool Changed(Vector2 size) => size.x > 1f && (Mathf.Abs(size.x - paintedSize.x) > 4f || Mathf.Abs(size.y - paintedSize.y) > 4f);

        void Paint(HoleInfo hole)
        {
            var size = view.contentRect.size;
            if (float.IsNaN(size.x) || size.x < 1f || size.y < 1f) return; // not laid out yet: GeometryChanged paints
            if (Texture) Object.Destroy(Texture);
            Frame = MapFrame.Fit(hole, size.x / size.y);
            float scale = Mathf.Min(TextureScale, MaxTexture / Mathf.Max(size.x, size.y));
            var started = Time.realtimeSinceStartup;
            Texture = HoleMapPainter.Paint(hole, Frame, Mathf.RoundToInt(size.x * scale), Mathf.RoundToInt(size.y * scale));
            if (Texture) Debug.Log($"[{owner}] Painted hole {hole.holeRef} ({Texture.width}x{Texture.height}) in {(Time.realtimeSinceStartup - started) * 1000f:0} ms");
            image.style.backgroundImage = Texture ? new StyleBackground(Texture) : new StyleBackground(StyleKeyword.None);
            paintedHole = hole;
            paintedSize = size;
        }
    }
}
