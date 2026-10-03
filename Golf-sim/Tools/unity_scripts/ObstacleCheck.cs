// Dev check (Play mode, Hole Simulator), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/ObstacleCheck.cs --entry ObstacleCheck.All
// The CLI stops a call after 30 s on the main thread, so All runs the tests a few at a time: repeat it until it prints
// the summary (AllReset starts over). Or run the entries below one by one; each takes 10 s or less on an idle Mac.
// Ball vs trees and rocks (ObstacleField) and the lie penalty, driven with GolfBall.Advance (the Editor doesn't
// tick Play mode in the background). Synthetic tests swap the hole's obstacles for one test object (Play-mode
// only, restored afterwards).
//   TreeLine    driver from the tee straight at the pin, through the forest on the dogleg
//   OverTree    driver over an 18 m conifer 110 m out: no hit, same shot as without trees
//   RockRoll    a ball rolled into a boulder bounces back
//   Canopy      7 irons through a crown (trunk missed): hit shots drop short
//   Replay      the same shot and seed lands on the same spot
//   Stress1..4  200 random shots and spots, 50 per entry: no NaN, nothing stuck, under the ground or inside a trunk
//   Lies        7 iron from 153 yd on fairway, rough, bunker, native and woods (trees off)
//   BunkerClubs every club from a bunker vs. the same spot as fairway: woods and hybrids barely get it out (the 5 wood
//               under a third of its fairway carry), the wedges keep most of theirs
//   Crowns      every drawn tree has its measured crown (HoleInfo.obstacles), no wider than the drawn tree's bounds
//   Drive       the default opening drive (Driver at the pin) and a fan of drives: every tree hit is on a drawn tree
//   DriveIrons  the same fan with a 5 iron and a wedge
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using GolfSim.Ball;
using GolfSim.Course;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class ObstacleCheck
{
    const float Frame = 0.02f;
    const float Y = ShotData.YardsPerMeter;

    static GolfBall Ball => Object.FindAnyObjectByType<GolfBall>();
    static HoleInfo Hole => Object.FindAnyObjectByType<HoleInfo>();

    const string Progress = "ObstacleCheck.All";
    const char Separator = '\u001e';
    const long PartMs = 5000; // one call starts tests until this much time has gone (the longest takes ~8 s, more under load)

    static readonly System.Func<string>[] Tests = { Crowns, Drive, DriveIrons, TreeLine, OverTree, RockRoll, Canopy, Replay, Lies, Stress1, Stress2, Stress3, Stress4 };

    /// <summary>Runs the next tests for about 5 s (a long one alone); once all have run, prints them all and a PASS / FAIL summary.</summary>
    public static string All()
    {
        string saved = SessionState.GetString(Progress, "");
        var results = saved.Length > 0 ? saved.Split(Separator).ToList() : new List<string>();
        var watch = Stopwatch.StartNew();
        while (results.Count < Tests.Length && watch.ElapsedMilliseconds < PartMs)
        {
            var test = Tests[results.Count];
            try { results.Add(test()); }
            catch (System.Exception e) { results.Add($"{test.Method.Name} FAIL: EXCEPTION {e}"); }
        }
        Ball.ResetToTee();
        if (results.Count < Tests.Length)
        {
            SessionState.SetString(Progress, string.Join(Separator, results));
            return $"{results.Count} of {Tests.Length} tests run ({string.Join(", ", Tests.Take(results.Count).Select(t => t.Method.Name))}): run All again";
        }
        SessionState.EraseString(Progress);
        int failed = results.Count(r => r.Contains("FAIL"));
        return $"{(failed == 0 ? "ALL PASS" : $"FAILED {failed} of {Tests.Length}")}\n{string.Join("\n", results)}";
    }

    /// <summary>Forgets an unfinished All, so the next one starts from the first test.</summary>
    public static string AllReset()
    {
        SessionState.EraseString(Progress);
        return "All starts over";
    }

    // ---- tests ----

    public static string TreeLine()
    {
        var ball = Ball;
        ball.ResetToTee();
        ball.aimOffset = 0f;
        var off = Shoot(Clubs.Find("Driver").shot, collide: false);
        var on = Shoot(Clubs.Find("Driver").shot, collide: true);
        bool pass = on.hits > 0 && on.total < off.total - 20f;
        return $"TreeLine {(pass ? "PASS" : "FAIL")}: no trees {off} | trees {on}";
    }

    public static string OverTree()
    {
        var hole = Hole;
        var ball = Ball;
        ball.ResetToTee();
        var dir = Flat(hole.PinWorld - hole.TeeWorld).normalized;
        var shot = Clubs.Find("Driver").shot;
        var spot = Ground(ball.transform.position + dir * 110f);
        return WithObstacles(new[] { Tree(spot, 0, 18f) }, () =>
        {
            var off = Shoot(shot, collide: false, aimAt: spot);
            var on = Shoot(shot, collide: true, aimAt: spot);
            bool pass = on.hits == 0 && Mathf.Abs(on.total - off.total) < 0.01f && off.heightAt(spot) > 18f;
            return $"OverTree {(pass ? "PASS" : "FAIL")}: ball {off.heightAt(spot):0.0} m over the ground at the tree | no trees {off} | tree {on}";
        });
    }

    public static string RockRoll()
    {
        var hole = Hole;
        var ball = Ball;
        var start = OnSurface("fairway", 120f);
        ball.PlaceOnGround(start);
        var dir = Flat(hole.PinWorld - start).normalized;
        var rock = Ground(start + dir * 4f);
        var shot = ShotData.FromMph(10f, 0f, 0f, 0f, 0f); // a firm roll
        return WithObstacles(new[] { new Obstacle { position = rock, radius = 0.5f, height = 0.6f, kind = 5 } }, () =>
        {
            var on = Shoot(shot, collide: true, from: start, aimAt: rock);
            float along = Vector3.Dot(Flat(on.rest - start), dir);
            bool pass = on.rock && along < 4f - 0.5f && !on.insideSolid;
            return $"RockRoll {(pass ? "PASS" : "FAIL")}: boulder 4 m ahead, ball stopped {along:0.00} m along the line ({on})";
        });
    }

    public static string Canopy()
    {
        var hole = Hole;
        var ball = Ball;
        var start = OnSurface("fairway", 150f);
        var dir = Flat(hole.PinWorld - start).normalized;
        var side = Vector3.Cross(Vector3.up, dir);
        var shot = Clubs.Find("7 Iron").shot;
        ball.PlaceOnGround(start);
        var line = Ground(start + dir * 45f);
        var tree = Ground(line + side * 2.5f); // crown over the line, trunk 2.5 m off it
        return WithObstacles(new[] { Tree(tree, 1, 16f) }, () =>
        {
            var off = Shoot(shot, collide: false, from: start, aimAt: line);
            int canopyShots = 0, dropped = 0, trunk = 0;
            for (uint seed = 1; seed <= 30; seed++)
            {
                var on = Shoot(shot, collide: true, from: start, aimAt: line, seed: seed);
                if (on.canopyHits > 0) canopyShots++;
                if (on.solidHits > 0) trunk++;
                if (on.canopyHits > 0 && on.carry < off.carry * 0.6f) dropped++;
            }
            bool pass = canopyShots >= 10 && dropped >= canopyShots * 0.8f && trunk == 0;
            return $"Canopy {(pass ? "PASS" : "FAIL")}: ball {off.heightAt(line):0.0} m up through a 16 m crown; 30 seeds: {canopyShots} hit leaves, " +
                   $"{dropped} of those carried < 60% of {off.carry * Y:0} yd, trunk hits {trunk}";
        });
    }

    public static string Replay()
    {
        var ball = Ball;
        ball.ResetToTee();
        ball.aimOffset = 0f;
        var a = Shoot(Clubs.Find("Driver").shot, collide: true, seed: 1234);
        var b = Shoot(Clubs.Find("Driver").shot, collide: true, seed: 1234);
        var c = Shoot(Clubs.Find("Driver").shot, collide: true, seed: 99);
        bool pass = a.rest == b.rest && a.hits == b.hits;
        return $"Replay {(pass ? "PASS" : "FAIL")}: seed 1234 twice -> {a.rest} / {b.rest} ({a.hits} hits); seed 99 -> {c.rest} ({c.hits} hits)";
    }

    public static string Lies()
    {
        var hole = Hole;
        var ball = Ball;
        var shot = Clubs.Find("7 Iron").shot;
        var sb = new StringBuilder("Lies (7 iron, 153 yd from the pin, trees off):");
        float fairway = 0f;
        bool pass = true;
        foreach (var surface in new[] { "fairway", "rough", "bunker", "native", "woods" })
        {
            var spot = OnSurface(surface, 153f / Y);
            if (spot == Vector3.zero) { sb.Append($"\n  {surface}: no spot found"); continue; }
            ball.PlaceOnGround(spot);
            string label = ball.LieEffectFor(shot).Label;
            var r = Shoot(shot, collide: false, from: spot, aimAt: hole.PinWorld);
            if (surface == "fairway") fairway = r.carry;
            else pass &= r.carry < fairway * 0.97f;
            sb.Append($"\n  {surface,-8} lie '{label}' result '{r.lieLabel}': carry {r.carry * Y:0.0} yd, apex {r.apex * Y:0.0} yd");
        }
        ball.ResetToTee();
        sb.Append($"\n  tee lie at the tee marker: '{ball.Lie}' ({ball.LieEffectFor(Clubs.Find("Driver").shot).Label})");
        pass &= ball.Lie == "tee";
        return (pass ? "PASS " : "FAIL ") + sb;
    }

    public static string BunkerClubs()
    {
        var hole = Hole;
        var ball = Ball;
        var spot = OnSurface("bunker", 120f / Y);
        if (spot == Vector3.zero) return "SKIP no bunker on this hole";
        var sb = new StringBuilder("Bunker carries by club (trees off), the lie label and the carry vs. a clean lie:");
        bool pass = true;
        foreach (var club in Clubs.Bag.Where(c => !c.IsPutter))
        {
            var clean = ball.Settings.LieFor("fairway", club.shot);
            var sand = ball.Settings.LieFor("bunker", club.shot);
            ball.PlaceOnGround(spot);
            var r = Shoot(club.shot, collide: false, from: spot, aimAt: hole.PinWorld);
            float kept = sand.speed / clean.speed;
            if (club.name == "5 Wood") pass &= kept < 0.5f;
            if (club.name == Clubs.SandWedge) pass &= kept > 0.85f;
            sb.Append($"\n  {club.name,-15} '{sand.Label}' launch {club.shot.launchAngle + sand.launch:0.0}°: carry {r.carry * Y:0} yd ({club.carryYards:0} yd clean)");
        }
        ball.ResetToTee();
        return (pass ? "PASS " : "FAIL ") + sb;
    }

    public static string Stress1() => Stress(0, 50);
    public static string Stress2() => Stress(50, 100);
    public static string Stress3() => Stress(100, 150);
    public static string Stress4() => Stress(150, 200);

    /// <summary>Random shots number first..end-1 (each from its own seed, so any range repeats exactly).</summary>
    static string Stress(int first, int end)
    {
        var hole = Hole;
        var ball = Ball;
        var terrain = hole.GetComponentInChildren<Terrain>();
        var size = terrain.terrainData.size;
        System.Random rand = null;
        float R(float a, float b) => a + (float)rand.NextDouble() * (b - a);
        int nan = 0, stuck = 0, under = 0, inside = 0, hits = 0;
        var statuses = new Dictionary<BallStatus, int>();
        long steps = 0;
        var watch = new Stopwatch();
        float worstUnder = 0f;
        for (int i = first; i < end; i++)
        {
            rand = new System.Random(7 + i);
            // Half the shots start next to a tree, so the forest gets a workout.
            Vector3 spot;
            if (i % 2 == 0)
            {
                var o = hole.obstacles[rand.Next(hole.obstacles.Length)];
                var p = hole.transform.TransformPoint(o.position);
                float a = R(0f, 360f) * Mathf.Deg2Rad;
                spot = p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (o.radius + R(0.3f, 4f));
            }
            else spot = terrain.transform.position + new Vector3(R(30f, size.x - 30f), 0f, R(30f, size.z - 30f));
            ball.PlaceOnGround(spot);
            if (ball.Obstacles.InsideSolid(ball.transform.position)) continue;
            ball.aimOffset = R(-180f, 180f);
            ball.windSpeed = R(0f, 13f);
            ball.windHeading = R(0f, 360f);
            var shot = ShotData.FromMph(R(2f, 200f), R(-5f, 60f), R(-15f, 15f), R(0f, 12000f), R(-3000f, 3000f));
            int hitCount = 0;
            System.Action<GolfBall, ObstacleHit> count = (_, _) => hitCount++;
            ball.HitObstacle += count;
            ball.Hit(shot);
            float t = 0f;
            watch.Start();
            while (ball.InMotion && t < 70f)
            {
                ball.Advance(Frame);
                t += Frame;
                steps += 10;
                var pos = ball.transform.position;
                if (float.IsNaN(pos.x + pos.y + pos.z)) { nan++; break; }
                float clear = pos.y - (terrain.SampleHeight(pos) + terrain.transform.position.y);
                if (ball.Status == BallStatus.Rolling && clear < BallPhysicsSettings.Radius - 0.01f) { under++; worstUnder = Mathf.Min(worstUnder, clear); }
                else if (clear < -0.05f && ball.Status != BallStatus.Holed) { under++; worstUnder = Mathf.Min(worstUnder, clear); }
            }
            watch.Stop();
            ball.HitObstacle -= count;
            if (ball.InMotion || t > 40f) stuck++;
            if (ball.Status == BallStatus.Stopped && ball.Obstacles.InsideSolid(ball.transform.position)) inside++;
            if (hitCount > 0) hits++;
            statuses[ball.Status] = statuses.TryGetValue(ball.Status, out int n) ? n + 1 : 1;
        }
        ball.windSpeed = 0f;
        ball.aimOffset = 0f;
        ball.ResetToTee();
        bool pass = nan == 0 && stuck == 0 && under == 0 && inside == 0;
        return $"Stress {first}-{end - 1} {(pass ? "PASS" : "FAIL")}: {statuses.Values.Sum()} shots, NaN {nan}, stuck {stuck}, under ground {under} (worst {worstUnder:0.000} m), " +
               $"inside a trunk/rock {inside}, shots that hit something {hits}; {string.Join(", ", statuses.Select(kv => $"{kv.Key} {kv.Value}"))}; " +
               $"{steps} steps in {watch.ElapsedMilliseconds} ms = {watch.Elapsed.TotalMilliseconds * 1000.0 / steps:0.00} µs per 2 ms step " +
               $"(whole ball sim, {ball.Obstacles.Count} obstacles)";
    }

    // A fitted cone is widest at its base, where the model's foliage can be a little narrower than the fit.
    const float WiderThanDrawn = 1.15f;

    public static string Crowns()
    {
        var hole = Hole;
        var drawn = DrawnTrees();
        var settings = Ball.Settings.obstacles;
        int trees = 0, measured = 0, wider = 0, onDrawn = 0;
        float ratioSum = 0f, worst = 0f;
        string worstModel = "";
        foreach (var o in hole.obstacles)
        {
            if (!o.IsTree) continue;
            trees++;
            if (o.HasCrown) measured++;
            var p = hole.transform.TransformPoint(o.position);
            var d = Nearest(drawn, p, out float off);
            if (off > 0.3f) continue;
            onDrawn++;
            float ratio = settings.CrownOf(o).radius * hole.transform.lossyScale.x / Mathf.Max(0.1f, d.halfWidth);
            ratioSum += ratio;
            if (ratio > worst) { worst = ratio; worstModel = d.name; }
            if (ratio > WiderThanDrawn) wider++;
        }
        bool pass = trees > 0 && measured == trees && wider == 0;
        return $"Crowns {(pass ? "PASS" : "FAIL")}: {measured}/{trees} trees have a measured crown; collision crown / drawn half-width " +
               $"avg {ratioSum / Mathf.Max(1, onDrawn):0.00}x, worst {worst:0.00}x ({worstModel}), wider than drawn (>{WiderThanDrawn}x) {wider} ({onDrawn} matched to a drawn tree)";
    }

    public static string Drive() => Drive("Driver");
    public static string DriveIrons() => Drive("5 Iron", "Wedge");

    static string Drive(params string[] clubs)
    {
        var ball = Ball;
        var drawn = DrawnTrees();
        var sb = new StringBuilder();
        int total = 0, ghosts = 0;
        string opening = "";
        foreach (var club in clubs)
            for (float aim = -40f; aim <= 40f; aim += 4f)
            {
                var hits = new List<ObstacleHit>();
                System.Action<GolfBall, ObstacleHit> record = (_, h) => hits.Add(h);
                ball.HitObstacle += record;
                ball.ResetToTee();
                ball.aimOffset = aim;
                var r = Shoot(Clubs.Find(club).shot, collide: true);
                ball.HitObstacle -= record;
                if (club == "Driver" && aim == 0f) opening = $"opening drive {r}; ";
                foreach (var h in hits)
                {
                    total++;
                    if (drawn.Any(t => Flat(t.position - h.point).magnitude <= t.halfWidth + 0.3f && h.point.y <= t.position.y + t.height + 0.5f)) continue;
                    var d = Nearest(drawn, h.point, out float off);
                    ghosts++;
                    if (ghosts <= 5) sb.Append($"\n  {club} aim {aim}: {(h.canopy ? "leaves" : "solid")} at {h.point}, {off:0.0} m from a drawn {d.name} (half-width {d.halfWidth:0.0})");
                }
            }
        ball.aimOffset = 0f;
        ball.ResetToTee();
        return $"Drive {string.Join(", ", clubs)} {(ghosts == 0 ? "PASS" : "FAIL")}: {opening}fan of {21 * clubs.Length} tee shots: {total} hits, {ghosts} off the drawn trees" + sb;
    }

    // ---- helpers ----

    struct DrawnTree { public Vector3 position; public float halfWidth, height; public string name; }

    /// <summary>The terrain's tree instances: where they stand, how far their LOD0 bounding box reaches from the trunk, and how tall they are drawn.</summary>
    static List<DrawnTree> DrawnTrees()
    {
        var terrain = Hole.GetComponentInChildren<Terrain>();
        var data = terrain.terrainData;
        var size = new List<Bounds>();
        foreach (var proto in data.treePrototypes)
        {
            var copy = Object.Instantiate(proto.prefab);
            copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var lods = copy.GetComponent<LODGroup>()?.GetLODs();
            var renderers = lods != null && lods.Length > 0 ? lods[0].renderers.Where(r => r).ToArray() : copy.GetComponentsInChildren<Renderer>();
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            size.Add(b);
            Object.DestroyImmediate(copy);
        }
        return data.treeInstances.Select(t =>
        {
            var b = size[t.prototypeIndex];
            return new DrawnTree
            {
                position = Vector3.Scale(t.position, data.size) + terrain.transform.position,
                halfWidth = Mathf.Max(-b.min.x, b.max.x, -b.min.z, b.max.z) * t.widthScale, height = b.max.y * t.heightScale,
                name = data.treePrototypes[t.prototypeIndex].prefab.name,
            };
        }).ToList();
    }

    static DrawnTree Nearest(List<DrawnTree> drawn, Vector3 p, out float distance)
    {
        var best = drawn.OrderBy(d => Flat(d.position - p).sqrMagnitude).First();
        distance = Flat(best.position - p).magnitude;
        return best;
    }

    struct Outcome
    {
        public float carry, total, apex;
        public int hits, solidHits, canopyHits;
        public bool tree, rock, insideSolid;
        public BallStatus status;
        public string lieLabel;
        public Vector3 rest;
        public List<Vector3> path;

        /// <summary>Ball height over the ground where its path passes this spot.</summary>
        public float heightAt(Vector3 spot)
        {
            var terrain = Object.FindAnyObjectByType<HoleInfo>().GetComponentInChildren<Terrain>();
            var p = path.OrderBy(q => Flat(q - spot).sqrMagnitude).First();
            return p.y - (terrain.SampleHeight(p) + terrain.transform.position.y);
        }

        public override string ToString() =>
            $"{status} carry {carry * Y:0.0} yd total {total * Y:0.0} yd, hits {hits} (solid {solidHits}, leaves {canopyHits}{(tree ? ", tree" : "")}{(rock ? ", rock" : "")})";
    }

    /// <summary>Hits from 'from' (default: where the ball is) aimed at a spot (default: as aimed now) and runs it to rest.</summary>
    static Outcome Shoot(ShotData shot, bool collide, Vector3? from = null, Vector3? aimAt = null, uint? seed = null)
    {
        var ball = Ball;
        var hole = Hole;
        if (from is Vector3 f) ball.PlaceOnGround(f);
        else if (ball.Status != BallStatus.Ready) ball.ResetToTee();
        if (aimAt is Vector3 target)
            ball.aimOffset = Vector3.SignedAngle(Flat(hole.PinWorld - ball.transform.position), Flat(target - ball.transform.position), Vector3.up);
        ball.collideWithObstacles = collide;
        var outcome = new Outcome { path = new List<Vector3>() };
        System.Action<GolfBall, ObstacleHit> record = (_, h) =>
        {
            outcome.hits++;
            if (h.canopy) outcome.canopyHits++; else outcome.solidHits++;
        };
        ball.HitObstacle += record;
        ball.Hit(shot, seed);
        for (int i = 0; i < 4000 && ball.InMotion; i++)
        {
            ball.Advance(Frame);
            outcome.path.Add(ball.transform.position);
        }
        ball.HitObstacle -= record;
        ball.collideWithObstacles = true;
        var r = ball.Result;
        outcome.carry = r.carry;
        outcome.total = r.total;
        outcome.apex = r.apex;
        outcome.tree = r.hitTree;
        outcome.rock = r.hitRock;
        outcome.status = ball.Status;
        outcome.lieLabel = r.LieLabel;
        outcome.rest = ball.transform.position;
        outcome.insideSolid = ball.Obstacles.InsideSolid(outcome.rest);
        // Back to the start for the next shot from the same spot.
        if (from is Vector3 again) ball.PlaceOnGround(again); else ball.ResetToTee();
        return outcome;
    }

    /// <summary>Runs a test with only these obstacles on the hole (world = hole-local here), then puts the real ones back.</summary>
    static string WithObstacles(Obstacle[] test, System.Func<string> body)
    {
        var hole = Hole;
        var real = hole.obstacles;
        var field = typeof(GolfBall).GetField("obstacles", BindingFlags.NonPublic | BindingFlags.Instance);
        try
        {
            hole.obstacles = test.Select(o => { o.position = hole.transform.InverseTransformPoint(o.position); return o; }).ToArray();
            field.SetValue(Ball, null); // rebuilt on the next Bind
            return body();
        }
        finally
        {
            hole.obstacles = real;
            field.SetValue(Ball, null);
        }
    }

    static Obstacle Tree(Vector3 ground, byte kind, float height) =>
        new Obstacle { position = ground, radius = 0.4f, height = height, kind = kind };

    /// <summary>A spot on this surface about this far from the pin, toward the tee side (Vector3.zero if none).</summary>
    static Vector3 OnSurface(string surface, float fromPin)
    {
        var hole = Hole;
        var map = new TerrainSurfaceMap(hole.GetComponentInChildren<Terrain>(), hole);
        var back = Flat(hole.TeeWorld - hole.PinWorld).normalized;
        for (int i = 0; i < 360; i++)
        {
            float a = (i % 2 == 0 ? 1 : -1) * (i / 2) * 1f;
            for (float d = fromPin; d < fromPin + 15f; d += 5f)
            {
                var p = Ground(hole.PinWorld + Quaternion.AngleAxis(a, Vector3.up) * back * d);
                if (map.Contains(p) && map.SurfaceAt(p) == surface && map.SurfaceAt(p + Vector3.right) == surface &&
                    map.SurfaceAt(p + Vector3.forward) == surface && !Ball.Obstacles.InsideSolid(p + Vector3.up * BallPhysicsSettings.Radius))
                    return p;
            }
        }
        return Vector3.zero;
    }

    static Vector3 Ground(Vector3 p)
    {
        var terrain = Hole.GetComponentInChildren<Terrain>();
        p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
        return p;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
