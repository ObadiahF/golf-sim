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
    /// the game's call. The picture is painted once per hole (and again if the panel changes shape), by HolePicture.
    /// </summary>
    public class CourseMap
    {
        const string Open = "map--open", StyleSheetName = "CourseMap";

        readonly VisualElement panel;
        readonly HolePicture picture;
        readonly Label eyebrow, toPin, carryCaption, carry, leaves, aim;
        MapScene scene;

        public CourseMap(VisualElement hudRoot)
        {
            AddStyles(hudRoot);
            panel = Element("map");
            var header = Element("map__header", panel);
            eyebrow = header.AddLabel("hud__eyebrow");
            header.AddLabel("map__keys").text = "Left/Right: aim  ·  Up/Down: club  ·  M: close";
            picture = new HolePicture(Element("map__view", panel), new MapOverlay(), "CourseMap");
            var stats = Element("map__stats", panel);
            toPin = Stat(stats, "TO PIN");
            carry = Stat(stats, "CARRY", out carryCaption);
            leaves = Stat(stats, "LEAVES");
            aim = Stat(stats, "AIM");
            UnderOverlays(hudRoot, panel);
        }

        public bool IsShown => panel.ClassListContains(Open);
        /// <summary>What it shows (or last showed), and the ground its picture covers; for tools.</summary>
        public MapScene Scene => scene;
        public MapFrame Frame => picture.Frame;
        public Texture2D Picture => picture.Texture;
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
        public void Prepare(HoleInfo hole) => picture.Prepare(hole);

        void Refresh()
        {
            picture.Show(scene);
            if (scene == null || !scene.hole) return;
            float y = ShotData.YardsPerMeter;
            eyebrow.text = $"{scene.title}  ·  {Round.FlatDistance(scene.hole.TeeWorld, scene.hole.PinWorld) * y:0} YD";
            toPin.text = $"{scene.ToPin * y:0} yd";
            carryCaption.text = Clubs.Find(scene.club).IsPutter ? "PUTT" : $"{scene.club.ToUpperInvariant()} CARRY";
            carry.text = $"{scene.carry * y:0} yd";
            leaves.text = $"{scene.AimPointToPin * y:0} yd";
            aim.text = RoundHud.Aim(scene.aim);
        }

        /// <summary>The maps' stylesheet (Resources/CourseMap.uss, the course map's and the mini map's), once per HUD.</summary>
        internal static void AddStyles(VisualElement hudRoot)
        {
            var sheet = Resources.Load<StyleSheet>(StyleSheetName);
            if (!sheet) Debug.LogWarning($"[CourseMap] No Resources/{StyleSheetName}.uss; the maps are unstyled.");
            else if (!hudRoot.styleSheets.Contains(sheet)) hudRoot.styleSheets.Add(sheet);
        }

        /// <summary>Adds a panel to the HUD under the scorecard, the turn banner, the confetti and the fade, so they always show over it.</summary>
        internal static void UnderOverlays(VisualElement hudRoot, VisualElement panel)
        {
            var scorecard = hudRoot.Q("scorecard");
            if (scorecard != null) scorecard.parent.Insert(scorecard.parent.IndexOf(scorecard), panel);
            else hudRoot.Add(panel);
        }

        internal static VisualElement Element(string cls, VisualElement parent = null)
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
