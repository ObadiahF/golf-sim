using System.Collections.Generic;
using System.Linq;
using GolfSim.Ball;
using GolfSim.Course;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// The driving range: a tee box at the south end of a wide, nearly flat range, with target greens and flags at
    /// 50 to 300 yards (yardage boards beside each, more along both edges every 50 yards). The flag nearest the
    /// selected club's carry is the pin the aim points at. Every shot is measured (carry, total, offline), then the
    /// next ball is teed up; the card keeps the session: the last shot and each club's averages.
    /// </summary>
    public class DrivingRange : PracticeFacility
    {
        const float Size = 480f;                  // m square
        const float Base = 2f;                    // ground height, m
        static readonly Vector2 Tee = new Vector2(Size / 2f, 25f);
        static readonly Color Board = new Color(0.10f, 0.17f, 0.22f);

        /// <summary>Targets: distance from the tee (yards), bearing (degrees, + right of straight) and flag colour.</summary>
        static readonly (int yards, float bearing, Color flag)[] Targets =
        {
            (50, -14f, new Color(0.98f, 0.80f, 0.10f)),
            (100, 9f, new Color(0.10f, 0.35f, 0.85f)),
            (150, -7f, new Color(0.95f, 0.45f, 0.10f)),
            (200, 5f, new Color(0.60f, 0.20f, 0.75f)),
            (250, -4f, new Color(0.95f, 0.95f, 0.95f)),
            (300, 2f, new Color(0.80f, 0.06f, 0.06f)), // the hole's own pin (HoleMarkers' red)
        };

        struct Shot
        {
            public string club;
            public float carry, total, offline, toFlag; // yards
            public int flag;                             // target yards
            public BallStatus end;
        }

        readonly List<Shot> shots = new List<Shot>();
        int target = Targets.Length - 1;

        public override string StateName => RangeState;
        public override string Title => "DRIVING RANGE";

        static Vector2 TargetAt(int i) => Tee + Bearing(Targets[i].bearing) * Targets[i].yards / ShotData.YardsPerMeter;
        static Vector2 Bearing(float degrees) => new Vector2(Mathf.Sin(degrees * Mathf.Deg2Rad), Mathf.Cos(degrees * Mathf.Deg2Rad));
        static float GreenRadius(int i) => 6f + 0.03f * Targets[i].yards / ShotData.YardsPerMeter;
        static Vector2 BunkerAt(int i) => TargetAt(i) + new Vector2(Mathf.Sign(Targets[i].bearing) * (GreenRadius(i) + 5f), -2f);

        public override HoleInfo Build(RuntimeThemeLibrary themes)
        {
            var plan = new FacilityGround.Plan
            {
                name = "Driving Range", size = Size, resolution = 513, maxHeight = 12f, height = Height,
                tee = Tee, pin = TargetAt(Targets.Length - 1),
            };
            plan.Area("rough", new[] { Vector2.zero, new Vector2(Size, 0f), new Vector2(Size, Size), new Vector2(0f, Size) });
            // Fairway, with a collar of rough around each target green so the greens stand out from the tee.
            var collars = Enumerable.Range(0, Targets.Length).Select(i => (IReadOnlyList<Vector2>)FacilityGround.Ellipse(TargetAt(i), GreenRadius(i) * 1.2f + 4f, GreenRadius(i) * 0.85f + 4f)).ToArray();
            plan.Area("fairway", new[] { Tee + new Vector2(-35f, 9f), Tee + new Vector2(35f, 9f), Tee + new Vector2(95f, 420f), Tee + new Vector2(-95f, 420f) }, collars);
            plan.Area("tee", new[] { Tee + new Vector2(-12f, -7f), Tee + new Vector2(12f, -7f), Tee + new Vector2(12f, 7f), Tee + new Vector2(-12f, 7f) });
            for (int i = 0; i < Targets.Length; i++)
            {
                float r = GreenRadius(i);
                plan.Area("green", FacilityGround.Ellipse(TargetAt(i), r * 1.2f, r * 0.85f));
                if (i is 2 or 4) plan.Area("bunker", FacilityGround.Ellipse(BunkerAt(i), 4f, 2.6f, 24));
            }
            var rng = new System.Random(7);
            foreach (int side in new[] { -1, 1 })
                FacilityGround.TreeRow(plan, Tee + new Vector2(side * 75f, -15f), Tee + new Vector2(side * 140f, 440f), 16f, rng);
            FacilityGround.TreeRow(plan, Tee + new Vector2(-140f, 448f), Tee + new Vector2(140f, 448f), 14f, rng);

            var hole = FacilityGround.Build(plan, themes);
            for (int i = 0; i < Targets.Length; i++)
            {
                if (i < Targets.Length - 1) FacilityGround.Pin(hole, $"Pin {Targets[i].yards}", TargetAt(i), Targets[i].flag);
                var beside = TargetAt(i) + Bearing(Targets[i].bearing + 90f) * (GreenRadius(i) * 1.2f + 4f);
                FacilityGround.Sign(hole, beside, Tee, Targets[i].yards.ToString(), 1.4f + Targets[i].yards * 0.009f, Board);
            }
            // Yardage boards along both edges of the fairway every 50 yards, each that far from the tee.
            for (int yards = 50; yards <= 300; yards += 50)
                foreach (int side in new[] { -1, 1 })
                {
                    float d = yards / ShotData.YardsPerMeter, across = 35f + 60f * (d - 9f) / 411f + 12f; // out past the fairway's edge
                    var at = Tee + new Vector2(side * across, Mathf.Sqrt(Mathf.Max(d * d - across * across, 1f)));
                    FacilityGround.Sign(hole, at, Tee, yards.ToString(), 0.9f, Board);
                }
            return hole;
        }

        /// <summary>Ground height: nearly flat (so distances read true), gentle crowns on the greens, rising at the far end and the sides.</summary>
        static float Height(float x, float z)
        {
            var p = new Vector2(x, z);
            float roll = 0.15f * Mathf.Sin(x * 0.045f) * Mathf.Cos(z * 0.031f) + 0.1f * Mathf.Sin(z * 0.07f + x * 0.02f);
            float h = Base + roll * Mathf.InverseLerp(12f, 35f, Vector2.Distance(p, Tee)); // the tee box is level
            for (int i = 0; i < Targets.Length; i++)
            {
                float r = GreenRadius(i) * 0.7f;
                h += 0.3f * Mathf.Exp(-(p - TargetAt(i)).sqrMagnitude / (2f * r * r));
                if (i is 2 or 4) h -= 0.45f * Mathf.Exp(-(p - BunkerAt(i)).sqrMagnitude / 8f);
            }
            h += 4f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Tee.y + 320f, Size - 10f, z)); // backstop
            h += 0.03f * Mathf.Max(0f, Mathf.Abs(x - Tee.x) - 110f);                           // banks at the sides
            return h;
        }

        public override void Bind(GolfBall ball, HoleInfo hole)
        {
            base.Bind(ball, hole);
            Club(Clubs.Bag[0].name);
        }

        /// <summary>Aims at the flag nearest the club's typical carry.</summary>
        public override string Club(string club)
        {
            float carry = Clubs.Find(club).carryYards;
            target = 0;
            for (int i = 1; i < Targets.Length; i++)
                if (Mathf.Abs(Targets[i].yards - carry) <= Mathf.Abs(Targets[target].yards - carry)) target = i;
            if (Hole) Hole.pinPosition = FacilityGround.OnGround(Hole, TargetAt(target));
            return club;
        }

        public override void Setup() => Ball.ResetToTee();

        public override string ShotFinished(GolfBall finished, string club)
        {
            var r = finished.Result;
            var shot = new Shot
            {
                club = club, carry = Yards(r.carry), total = Yards(r.total), offline = Yards(r.offline),
                toFlag = Yards(Round.FlatDistance(finished.transform.position, Hole.PinWorld)), flag = Targets[target].yards, end = finished.Status,
            };
            shots.Add(shot);
            Attempts++;
            if (shot.end == BallStatus.Holed) Made++;
            return shot.end switch
            {
                BallStatus.Holed => $"In the hole at the {shot.flag}!",
                BallStatus.OutOfBounds => $"Out of bounds · {shot.carry:0} carry",
                _ => $"{shot.carry:0} carry · {shot.total:0} total · {Side(shot.offline)}",
            };
        }

        public override void Render(PracticeHud hud)
        {
            string count = Attempts == 1 ? "1 SHOT" : $"{Attempts} SHOTS";
            if (shots.Count == 0)
            {
                hud.Show($"{Title}  ·  {count}", "Hit away", "Flags at 50 to 300 yd  ·  the aim follows your club");
                return;
            }
            var last = shots[^1];
            string where = last.end == BallStatus.OutOfBounds ? "out of bounds" : $"{last.toFlag:0} yd from the {last.flag} flag";
            var rows = Clubs.Bag.Select(c => c.name).Where(c => shots.Any(s => s.club == c)).Select(c =>
            {
                var mine = shots.Where(s => s.club == c && s.end != BallStatus.OutOfBounds).ToList();
                return mine.Count == 0
                    ? new[] { c, shots.Count(s => s.club == c).ToString(), "–", "–" }
                    : new[] { c, shots.Count(s => s.club == c).ToString(), $"{mine.Average(s => s.carry):0}", $"{mine.Average(s => s.total):0}" };
            });
            hud.Show($"{Title}  ·  {count}", $"{last.carry:0} carry  ·  {last.total:0} total",
                     $"{last.club}  ·  {Side(last.offline)} offline  ·  {where}",
                     new[] { "CLUB", "SHOTS", "CARRY", "TOTAL" }, rows.ToList());
        }
    }
}
