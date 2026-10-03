using System.Linq;
using GolfSim.Ball;
using GolfSim.Course;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// The putting green: a large contoured green (a tilt, a ridge, a back tier and a swale) with six cups, ringed by
    /// fringe and rough. A cycle of putts (short, medium, long and breaking, each from a spot worked out from the
    /// slope at its cup) is played in order with the putter: the ball goes to the next spot after each putt, holed or
    /// not; a mulligan replays the last one and Up/Down step through the cycle. Only the cup in play shows its flag.
    /// Putting mode, the break line and the phone's power meter come from the round's putting code as on any green.
    /// </summary>
    public class PuttingGreen : PracticeFacility
    {
        const float Size = 128f;   // m square: room for a ring of trees, so the horizon isn't the map's edge
        const float Margin = 0.7f; // m: putts stay this far inside the green's edge
        static readonly Vector2 Centre = new Vector2(Size / 2f, Size / 2f);
        static readonly Vector2 Radii = new Vector2(17f, 13f);

        static readonly Vector2[] Cups = new[]
        {
            new Vector2(-8f, 6f), new Vector2(7f, 7f), new Vector2(0f, -1f),
            new Vector2(10f, -4f), new Vector2(-9f, -5f), new Vector2(2f, -8.5f),
        }.Select(c => Centre + c).ToArray();

        enum Kind { Short, Medium, Long, Breaking }
        enum Line { Uphill, Downhill, Sidehill }

        /// <summary>The cycle: which cup, how far, what kind of putt and how it sits on the slope.</summary>
        static readonly (int cup, float metres, Kind kind, Line line)[] Cycle =
        {
            (2, 1.2f, Kind.Short, Line.Uphill),
            (0, 2.2f, Kind.Short, Line.Sidehill),
            (3, 4.5f, Kind.Medium, Line.Downhill),
            (4, 6f, Kind.Breaking, Line.Sidehill),
            (1, 10f, Kind.Long, Line.Uphill),
            (5, 5.5f, Kind.Medium, Line.Uphill),
            (2, 8f, Kind.Breaking, Line.Sidehill),
            (0, 14f, Kind.Long, Line.Downhill),
        };

        readonly Vector2[] spots = new Vector2[Cycle.Length];
        readonly Transform[] pins = new Transform[Cups.Length];
        readonly int[] made = new int[4], tried = new int[4];
        int current, last;
        string lastResult;

        public override string StateName => PuttingGreenState;
        public override string Title => "PUTTING GREEN";
        public override bool ResetsAim => true;
        public override string Waiting => "Next putt coming up";
        public override float NextDelay(GolfBall finished) => finished.Status == BallStatus.Holed ? 3f : 2.5f;

        // ---- the green ----

        /// <summary>The green's edge as a radius scale at angle a (1 = the ellipse): a few lobes so it isn't a plain oval.</summary>
        static float Edge(float a) => 1f + 0.08f * Mathf.Sin(3f * a + 1f) + 0.05f * Mathf.Sin(5f * a + 2f);

        /// <summary>True when (x, z) is on the green at least `margin` metres inside its edge.</summary>
        static bool OnGreen(Vector2 p, float margin)
        {
            var d = p - Centre;
            float a = Mathf.Atan2(d.y / Radii.y, d.x / Radii.x);
            float scaled = new Vector2(d.x / Radii.x, d.y / Radii.y).magnitude; // 1 on the plain ellipse
            return scaled < Edge(a) - margin / Mathf.Min(Radii.x, Radii.y);      // the margin measured on the short axis (at least that everywhere)
        }

        /// <summary>Heights: rising 1.6 % toward the back, a diagonal ridge, a raised back-right tier and a front-left swale.</summary>
        static float Height(float x, float z)
        {
            float h = 1.5f + 0.016f * (z - Centre.y);
            float across = (x - Centre.x) - 0.35f * (z - Centre.y); // distance across the ridge line
            h += 0.14f * Mathf.Exp(-across * across / (2f * 2.2f * 2.2f));
            h += 0.18f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 9f, (x - Centre.x - 4f) + (z - Centre.y - 2f)));
            float sx = x - Centre.x + 7f, sz = z - Centre.y + 4f; // the swale, front left
            h -= 0.12f * Mathf.Exp(-(sx * sx + sz * sz) / (2f * 4f * 4f));
            return h;
        }

        public override HoleInfo Build(RuntimeThemeLibrary themes)
        {
            var plan = new FacilityGround.Plan
            {
                name = "Putting Green", size = Size, resolution = 513, maxHeight = 4f, height = Height,
                tee = Centre - new Vector2(30f, 30f), pin = Cups[0], // the tee is out of the way: the ball is placed on the green
            };
            plan.Area("rough", new[] { Vector2.zero, new Vector2(Size, 0f), new Vector2(Size, Size), new Vector2(0f, Size) });
            plan.Area("fairway", FacilityGround.Ring(Centre, 64, a => (Radii + Vector2.one * 3f) * Edge(a)));
            plan.Area("green", FacilityGround.Ring(Centre, 64, a => Radii * Edge(a)));
            plan.Area("bunker", FacilityGround.Ellipse(Centre + new Vector2(-21f, -9f), 4.5f, 2.6f, 24));
            var rng = new System.Random(11);
            var ring = FacilityGround.Ring(Centre, 8, a => new Vector2(42f + 6f * Mathf.Sin(3f * a), 40f + 5f * Mathf.Cos(2f * a)));
            for (int i = 0; i < ring.Length; i++) FacilityGround.TreeRow(plan, ring[i], ring[(i + 1) % ring.Length], 9f, rng);

            var hole = FacilityGround.Build(plan, themes);
            FacilityGround.HideTee(hole);
            pins[0] = hole.transform.Find("Pin");
            for (int i = 1; i < Cups.Length; i++) pins[i] = FacilityGround.Pin(hole, $"Pin {i + 1}", Cups[i], new Color(0.98f, 0.80f, 0.10f));
            for (int i = 0; i < Cycle.Length; i++) spots[i] = SpotFor(Cycle[i]);
            return hole;
        }

        /// <summary>
        /// Where a putt is played from: `metres` from its cup, below it (uphill) or above it (downhill) on the fall line,
        /// turned in steps until the whole line stays on the green and clear of the other cups; a sidehill putt takes
        /// the clear line with the most slope across it (the most break).
        /// </summary>
        static Vector2 SpotFor((int cup, float metres, Kind kind, Line line) putt)
        {
            var cup = Cups[putt.cup];
            var downhill = -Gradient(cup).normalized;
            var start = putt.line == Line.Downhill ? -downhill : downhill;
            Vector2? best = null;
            float bestAcross = -1f;
            for (int k = 0; k < 24; k++)
            {
                float turn = (k + 1) / 2 * 15f * (k % 2 == 0 ? 1f : -1f); // 0, +15, -15, +30, -30 ...
                var d = Rotate(start, turn);
                if (!LineClear(cup, d, putt.metres, putt.cup)) continue;
                if (putt.line != Line.Sidehill) return cup + d * putt.metres;
                float across = 0f;
                for (float t = 0.2f; t < 1f; t += 0.2f)
                    across += Mathf.Abs(Vector2.Dot(Gradient(cup + d * putt.metres * t), new Vector2(-d.y, d.x)));
                if (across > bestAcross) (best, bestAcross) = (cup + d * putt.metres, across);
            }
            if (best is { } spot) return spot;
            Debug.LogWarning($"[PuttingGreen] No clear {putt.metres} m line to cup {putt.cup + 1}; using a short straight one.");
            return cup + start * Mathf.Min(putt.metres, 1f);
        }

        /// <summary>The slope at a spot: rise per metre east (x) and north (y).</summary>
        static Vector2 Gradient(Vector2 p)
        {
            const float e = 0.25f;
            return new Vector2(Height(p.x + e, p.y) - Height(p.x - e, p.y), Height(p.x, p.y + e) - Height(p.x, p.y - e)) / (2f * e);
        }

        static bool LineClear(Vector2 cup, Vector2 dir, float metres, int cupIndex)
        {
            for (float t = 0f; t <= metres; t += 0.25f)
                if (!OnGreen(cup + dir * t, Margin)) return false;
            var spot = cup + dir * metres;
            for (int i = 0; i < Cups.Length; i++)
                if (i != cupIndex && Vector2.Distance(Cups[i], spot) < 0.6f) return false;
            return true;
        }

        static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        // ---- play ----

        public override void Bind(GolfBall ball, HoleInfo hole)
        {
            base.Bind(ball, hole);
            ball.Placed += OnPlaced;
        }

        public override void Unbind()
        {
            if (Ball) Ball.Placed -= OnPlaced;
            base.Unbind();
        }

        /// <summary>Teed up (the ball's start, a reset, a hit after holing out): back to the putt in play instead.</summary>
        void OnPlaced(GolfBall placed)
        {
            if (placed.Lie == "tee") Setup();
        }

        public override string Club(string club) => Clubs.Putter;

        public override void Advance(int step) => current = ((current + step) % Cycle.Length + Cycle.Length) % Cycle.Length;

        public override void Again() => current = last;

        public override int StepFor(NavKey key) => key switch { NavKey.Down => 1, NavKey.Up => -1, _ => 0 };

        public override string KeyHint => "Left/Right: aim  ·  Up/Down: previous / next putt  ·  Esc: menu";

        public override void Setup()
        {
            int cup = Cycle[current].cup;
            Hole.pinPosition = FacilityGround.OnGround(Hole, Cups[cup]);
            for (int i = 0; i < pins.Length; i++)
                if (pins[i]) FacilityGround.ShowFlag(pins[i], i == cup);
            Ball.PlaceOnGround(Hole.transform.TransformPoint(new Vector3(spots[current].x, 0f, spots[current].y)));
        }

        public override string ShotFinished(GolfBall finished, string club)
        {
            var putt = Cycle[current];
            bool holed = finished.Status == BallStatus.Holed;
            last = current;
            Attempts++;
            tried[(int)putt.kind]++;
            if (holed)
            {
                Made++;
                made[(int)putt.kind]++;
            }
            float left = Round.FlatDistance(finished.transform.position, Hole.PinWorld);
            lastResult = holed ? "Holed!" : left < 1f ? $"Missed by {left * 100f:0} cm" : $"Missed by {left:0.0} m";
            // A stopped putt keeps the putting HUD's toast ("Putted 4.2 m of 5.0 m").
            return holed ? $"Holed! {Made} of {Attempts}" : null;
        }

        public override void Render(PracticeHud hud)
        {
            var putt = Cycle[current];
            string pct = Attempts > 0 ? $"  ·  {100f * Made / Attempts:0}%" : "";
            string detail = $"Putt {current + 1} of {Cycle.Length}: {putt.kind.ToString().ToLowerInvariant()}, {putt.line.ToString().ToLowerInvariant()}, cup {putt.cup + 1}";
            if (lastResult != null) detail += $"  ·  last: {lastResult.ToLowerInvariant()}";
            var rows = Enumerable.Range(0, 4).Where(k => tried[k] > 0)
                .Select(k => new[] { ((Kind)k).ToString(), $"{made[k]} / {tried[k]}" }).ToList();
            hud.Show(Title, Attempts == 0 ? "No putts yet" : $"Made {Made} of {Attempts}{pct}", detail, rows.Count > 0 ? new[] { "PUTTS", "MADE" } : null, rows);
        }
    }
}
