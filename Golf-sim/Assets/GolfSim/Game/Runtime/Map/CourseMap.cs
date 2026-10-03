using GolfSim.Ball;
using GolfSim.Course;
using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// The course map on the round HUD: the whole hole from above (HoleMapPainter's picture, tee at the bottom, pin at
    /// the top), with the aim line to the club's carry, the pin, the tee and every ball drawn live over it (MapOverlay),
    /// and the distances under it. A panel on the right that slides in and out (CourseMap.uss), placed in the HUD below
    /// the scorecard, the turn banner and the fade, so those always cover it. Show and Hide only; when it may show is
    /// the game's call. The picture is painted once per hole (and again if the panel changes shape).
    /// </summary>
    public class CourseMap
    {
        const string Open = "map--open", StyleSheetName = "CourseMap";
        const float TextureScale = 1.25f; // texture pixels per panel pixel (a 1080p panel on a 4K TV stays crisp enough)
        const int MaxTexture = 1600;

        readonly VisualElement panel, view, image;
        readonly MapOverlay overlay;
        readonly Label eyebrow, toPin, carryCaption, carry, leaves, aim;
        MapScene scene;
        HoleInfo paintedHole;
        Vector2 paintedSize;
        MapFrame frame;
        Texture2D texture;

        public CourseMap(VisualElement hudRoot)
        {
            var sheet = Resources.Load<StyleSheet>(StyleSheetName);
            if (sheet) hudRoot.styleSheets.Add(sheet);
            else Debug.LogWarning($"[CourseMap] No Resources/{StyleSheetName}.uss; the map is unstyled.");
            panel = Element("map");
            var header = Element("map__header", panel);
            eyebrow = header.AddLabel("hud__eyebrow");
            header.AddLabel("map__keys").text = "Left/Right: aim  ·  Up/Down: club  ·  M: close";
            view = Element("map__view", panel);
            image = Element("map__image", view);
            overlay = new MapOverlay();
            view.Add(overlay);
            var stats = Element("map__stats", panel);
            toPin = Stat(stats, "TO PIN");
            carry = Stat(stats, "CARRY", out carryCaption);
            leaves = Stat(stats, "LEAVES");
            aim = Stat(stats, "AIM");
            view.RegisterCallback<GeometryChangedEvent>(_ => Refresh());
            // Under the scorecard, the turn banner, the confetti and the fade: they always show over the map.
            var scorecard = hudRoot.Q("scorecard");
            if (scorecard != null) scorecard.parent.Insert(scorecard.parent.IndexOf(scorecard), panel);
            else hudRoot.Add(panel);
        }

        public bool IsShown => panel.ClassListContains(Open);
        /// <summary>What it shows (or last showed), and the ground its picture covers; for tools.</summary>
        public MapScene Scene => scene;
        public MapFrame Frame => frame;
        public Texture2D Picture => texture;
        public VisualElement Panel => panel;

        /// <summary>Opens the map on this scene, or redraws it (aim, club, the next player).</summary>
        public void Show(MapScene shown)
        {
            scene = shown;
            panel.AddToClassList(Open);
            Refresh();
        }

        public void Hide() => panel.RemoveFromClassList(Open);

        /// <summary>Paints the hole's picture ahead of time (e.g. while the hole fades in), so opening the map doesn't stall.</summary>
        public void Prepare(HoleInfo hole)
        {
            if (hole && hole != paintedHole) Paint(hole);
        }

        void Refresh()
        {
            if (scene == null || !scene.hole) return;
            if (scene.hole != paintedHole || Changed(view.contentRect.size)) Paint(scene.hole);
            float y = ShotData.YardsPerMeter;
            eyebrow.text = $"{scene.title}  ·  {Round.FlatDistance(scene.hole.TeeWorld, scene.hole.PinWorld) * y:0} YD";
            toPin.text = $"{scene.ToPin * y:0} yd";
            carryCaption.text = Clubs.Find(scene.club).IsPutter ? "PUTT" : $"{scene.club.ToUpperInvariant()} CARRY";
            carry.text = $"{scene.carry * y:0} yd";
            leaves.text = $"{scene.AimPointToPin * y:0} yd";
            aim.text = RoundHud.Aim(scene.aim);
            overlay.Show(scene, frame);
        }

        bool Changed(Vector2 size) => size.x > 1f && (Mathf.Abs(size.x - paintedSize.x) > 4f || Mathf.Abs(size.y - paintedSize.y) > 4f);

        void Paint(HoleInfo hole)
        {
            var size = view.contentRect.size;
            if (float.IsNaN(size.x) || size.x < 1f || size.y < 1f) return; // not laid out yet: GeometryChanged paints
            if (texture) Object.Destroy(texture);
            frame = MapFrame.Fit(hole, size.x / size.y);
            float scale = Mathf.Min(TextureScale, MaxTexture / Mathf.Max(size.x, size.y));
            var started = Time.realtimeSinceStartup;
            texture = HoleMapPainter.Paint(hole, frame, Mathf.RoundToInt(size.x * scale), Mathf.RoundToInt(size.y * scale));
            if (texture) Debug.Log($"[CourseMap] Painted hole {hole.holeRef} ({texture.width}x{texture.height}) in {(Time.realtimeSinceStartup - started) * 1000f:0} ms");
            image.style.backgroundImage = texture ? new StyleBackground(texture) : new StyleBackground(StyleKeyword.None);
            paintedHole = hole;
            paintedSize = size;
            overlay.Show(scene != null && scene.hole == hole ? scene : null, frame);
        }

        static VisualElement Element(string cls, VisualElement parent = null)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList(cls);
            parent?.Add(e);
            return e;
        }

        static Label Stat(VisualElement row, string caption) => Stat(row, caption, out _);

        static Label Stat(VisualElement row, string caption, out Label captionLabel)
        {
            var stat = Element("hud__stat", row);
            captionLabel = stat.AddLabel("hud__caption");
            captionLabel.text = caption;
            return stat.AddLabel("hud__value");
        }
    }
}
