using System.Collections.Generic;
using GolfSim.Ball;
using GolfSim.Course;
using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>What the course map shows over the hole picture: positions in world space, distances in metres.</summary>
    public class MapScene
    {
        /// <summary>A ball on the map: the player up's (big, ringed) or another player's (a dot and their name).</summary>
        public struct Marker
        {
            public string name;
            public Vector3 position;
            public Color color;
        }

        public HoleInfo hole;
        /// <summary>"HOLE 3  ·  PAR 4" or "PRACTICE  ·  PAR 4".</summary>
        public string title;
        /// <summary>The ball about to be hit, the way it is aimed (flat, unit) and how far the club carries.</summary>
        public Marker ball;
        public Vector3 aimDirection;
        public float carry;
        public string club;
        /// <summary>Degrees right (+) or left (-) of the pin.</summary>
        public float aim;
        public readonly List<Marker> others = new List<Marker>();
        /// <summary>False while the ball is flying: no aim line or target.</summary>
        public bool aiming = true;
        /// <summary>Where the last shot went, ball positions in order (the mini map; null = none).</summary>
        public List<Vector3> trail;

        public Vector3 AimPoint => ball.position + aimDirection * carry;
        public float ToPin => Round.FlatDistance(ball.position, hole.PinWorld);
        public float AimPointToPin => Round.FlatDistance(AimPoint, hole.PinWorld);
    }

    /// <summary>
    /// The course map's live layer, drawn over the hole picture with the vector API: distance arcs from the ball,
    /// the aim line to the club's carry, the target there, the pin and tee, and every ball, with small labels.
    /// Redrawn whenever the scene changes (aim, club, the next player). Compact (the mini map): smaller marks, the
    /// aim line, the pin, the balls and the shot's trail, no arcs and no labels.
    /// </summary>
    public class MapOverlay : VisualElement
    {
        const float ArcStep = 50f;        // yards between distance arcs
        const float ArcSpread = 24f;      // degrees either side of the aim
        const float Edge = 0.03f;         // balls off the map are pinned this far inside its edge
        static readonly Color Ink = new Color(0.03f, 0.05f, 0.06f, 0.75f);
        static readonly Color Flag = new Color32(250, 204, 21, 255);
        static readonly Color Line = new Color(1f, 1f, 1f, 0.95f);
        static readonly Color Trail = new Color(1f, 0.92f, 0.45f, 0.95f);

        readonly bool compact;
        readonly float k; // size of the marks
        MapScene scene;
        MapFrame frame;
        readonly List<Label> labels = new List<Label>();
        int used;

        public MapOverlay(bool compact = false)
        {
            this.compact = compact;
            k = compact ? 0.55f : 1f;
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = style.top = style.right = style.bottom = 0;
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => Layout());
        }

        public void Show(MapScene shown, MapFrame map)
        {
            scene = shown;
            frame = map;
            MarkDirtyRepaint();
            Layout();
        }

        Vector2 Size => contentRect.size;
        /// <summary>A scene on a hole that still exists (a hole being replaced leaves the last scene behind), laid out.</summary>
        bool Drawable => scene != null && scene.hole && frame.IsValid && Size.x > 0f;
        Vector2 Point(Vector3 world)
        {
            var m = frame.ToMap(world);
            return new Vector2(Mathf.Clamp(m.x, Edge, 1f - Edge) * Size.x, Mathf.Clamp(m.y, Edge, 1f - Edge) * Size.y);
        }
        float Pixels(float meters) => meters / frame.size.x * Size.x;

        void Draw(MeshGenerationContext mgc)
        {
            if (!Drawable) return;
            var p = mgc.painter2D;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            Vector2 ball = Point(scene.ball.position), aim = Point(scene.AimPoint), pin = Point(scene.hole.PinWorld);
            var dir = frame.Direction(scene.aimDirection);
            float heading = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            // Distance arcs every 50 yd around the aim, out past the carry.
            float reach = Mathf.Max(scene.carry, 10f) * ShotData.YardsPerMeter + ArcStep * 0.8f;
            for (float yards = ArcStep; !compact && scene.aiming && yards <= reach; yards += ArcStep)
            {
                p.strokeColor = new Color(1f, 1f, 1f, 0.22f);
                p.lineWidth = 2f;
                p.BeginPath();
                p.Arc(ball, Pixels(yards / ShotData.YardsPerMeter), Angle.Degrees(heading - ArcSpread), Angle.Degrees(heading + ArcSpread));
                p.Stroke();
            }

            // Tee markers.
            var tee = Point(scene.hole.TeeWorld);
            var across = Perpendicular(frame.Direction(scene.hole.PinWorld - scene.hole.TeeWorld)) * (9f * k);
            Dot(p, tee - across, 4f * k, Color.white, Ink, 1.5f * k);
            Dot(p, tee + across, 4f * k, Color.white, Ink, 1.5f * k);

            if (scene.aiming)
            {
                // The aim line, outlined so it reads on every surface, and the target at the carry.
                Segment(p, ball, aim, Ink, 9f * k);
                Segment(p, ball, aim, Line, 4.5f * k);
                Dot(p, aim, 17f * k, new Color(1f, 1f, 1f, 0.2f), Line, 3f * k);
                Dot(p, aim, 4f * k, Line, Ink, 0f);
            }

            DrawPin(p, pin);
            DrawTrail(p);

            foreach (var other in scene.others) Dot(p, Point(other.position), 8f * k, other.color, Color.white, 2.5f * k);
            // The player up: a halo, their colour and a white ring.
            Dot(p, ball, 20f * k, new Color(scene.ball.color.r, scene.ball.color.g, scene.ball.color.b, 0.28f), Color.clear, 0f);
            Dot(p, ball, 11f * k, scene.ball.color, Color.white, 3.5f * k);
        }

        /// <summary>The shot's path over the ground so far, outlined like the aim line.</summary>
        void DrawTrail(Painter2D p)
        {
            if (scene.trail == null || scene.trail.Count < 2) return;
            foreach (var (color, width) in new[] { (Ink, 7f * k), (Trail, 3.5f * k) })
            {
                p.strokeColor = color;
                p.lineWidth = width;
                p.BeginPath();
                p.MoveTo(Point(scene.trail[0]));
                for (int i = 1; i < scene.trail.Count; i++) p.LineTo(Point(scene.trail[i]));
                p.Stroke();
            }
        }

        void DrawPin(Painter2D p, Vector2 cup)
        {
            Dot(p, cup, 5f * k, Color.white, Ink, 2f * k);
            var top = cup + new Vector2(0f, -38f * k);
            Segment(p, cup, top, Ink, 5f * k);
            Segment(p, cup, top, Color.white, 2.5f * k);
            p.fillColor = Flag;
            p.strokeColor = Ink;
            p.lineWidth = 1.5f * k;
            p.BeginPath();
            p.MoveTo(top);
            p.LineTo(top + new Vector2(22f, 7f) * k);
            p.LineTo(top + new Vector2(0f, 14f) * k);
            p.ClosePath();
            p.Fill();
            p.Stroke();
        }

        /// <summary>Places the labels: the pin's distance, the carry, the arcs' yardages and the other players' names.</summary>
        void Layout()
        {
            used = 0;
            if (Drawable && !compact)
            {
                Vector2 ball = Point(scene.ball.position), aim = Point(scene.AimPoint), pin = Point(scene.hole.PinWorld);
                var dir = frame.Direction(scene.aimDirection);
                float y2p = ShotData.YardsPerMeter;
                // Beside the flag (it flies to the right), or left of the stick near the right edge.
                bool pinLeft = pin.x > Size.x * 0.75f;
                Tag($"{scene.ToPin * y2p:0} yd", pin + new Vector2(pinLeft ? -52f : 66f, -31f), "map__tag map__tag--pin");
                // The carry beside the target: right of it, else left (past the map's edge or over another ball there).
                // The arcs' yardages go on the other side.
                bool Clear(Vector2 at) => at.x > 50f && at.x < Size.x - 50f && scene.others.TrueForAll(o => Vector2.Distance(Point(o.position), at) > 56f);
                Vector2 right = aim + Vector2.right * 64f, left = aim + Vector2.left * 64f;
                bool tagRight = Clear(right) || !Clear(left);
                // Not on top of the pin's (a putt, or a carry that reaches the pin): the footer has it.
                if (Vector2.Distance(aim, pin) > 48f) Tag($"{scene.carry * y2p:0} yd", tagRight ? right : left, "map__tag map__tag--aim");
                float end = (Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + (tagRight ? -ArcSpread : ArcSpread)) * Mathf.Deg2Rad;
                float reach = Mathf.Max(scene.carry, 10f) * y2p + ArcStep * 0.8f;
                for (float yards = ArcStep; yards <= reach; yards += ArcStep)
                    if (Mathf.Abs(yards - scene.carry * y2p) > ArcStep * 0.4f)
                        Tag($"{yards:0}", ball + new Vector2(Mathf.Cos(end), Mathf.Sin(end)) * Pixels(yards / y2p), "map__tag map__tag--arc");
                foreach (var other in scene.others) Tag(other.name, Point(other.position) + new Vector2(0f, 22f), "map__tag map__tag--name");
            }
            for (int i = used; i < labels.Count; i++) labels[i].style.display = DisplayStyle.None;
        }

        void Tag(string text, Vector2 at, string classes)
        {
            if (used == labels.Count)
            {
                var label = new Label { pickingMode = PickingMode.Ignore };
                label.style.position = Position.Absolute;
                label.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
                labels.Add(label);
                Add(label);
            }
            var l = labels[used++];
            l.ClearClassList();
            foreach (var c in classes.Split(' ')) l.AddToClassList(c);
            l.text = text;
            l.style.left = Mathf.Clamp(at.x, 30f, Size.x - 30f);
            l.style.top = Mathf.Clamp(at.y, 16f, Size.y - 16f);
            l.style.display = DisplayStyle.Flex;
        }

        static Vector2 Perpendicular(Vector2 d) => new Vector2(-d.y, d.x);

        static void Segment(Painter2D p, Vector2 a, Vector2 b, Color color, float width)
        {
            p.strokeColor = color;
            p.lineWidth = width;
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(b);
            p.Stroke();
        }

        static void Dot(Painter2D p, Vector2 at, float radius, Color fill, Color outline, float outlineWidth)
        {
            p.fillColor = fill;
            p.BeginPath();
            p.Arc(at, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill();
            if (outlineWidth <= 0f) return;
            p.strokeColor = outline;
            p.lineWidth = outlineWidth;
            p.Stroke();
        }
    }
}
