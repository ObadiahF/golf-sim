using System.Collections.Generic;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// Cuts a recorded shot like golf on TV. Full shots: down-the-line behind the player on a long lens, a tower
    /// camera far to the side tracking the ball with lead room, then a camera at the landing zone looking back at the
    /// ball dropping in (in slow motion), and for long shots a high overhead of the whole tracer. Tree hits cut to a
    /// camera on the tree; holed shots end on a low camera behind the cup; putts are filmed from behind the putter.
    /// Every camera is placed clear of terrain and trees with a view of what it films (CameraSpots).
    /// </summary>
    public class ReplayCameraman
    {
        const float FullFlight = 2.2f; // s in the air: below this a shot is a chip or pitch (fewer cameras)

        readonly ReplaySettings s;
        readonly CameraSpots spots;

        ShotRecording rec;
        Vector3 dir, right;

        public ReplayCameraman(ReplaySettings settings, CameraSpots spots)
        {
            s = settings;
            this.spots = spots;
        }

        public ReplayPlan Plan(ShotRecording recording)
        {
            rec = recording;
            dir = rec.Direction;
            right = Vector3.Cross(Vector3.up, dir);
            var plan = new ReplayPlan { start = -s.preRoll, end = rec.Duration + s.hold };
            if (rec.Rolled) PlanPutt(plan);
            else PlanFlight(plan);
            return plan;
        }

        // ---- putts and bump-and-runs ----

        void PlanPutt(ReplayPlan plan)
        {
            float end = rec.Duration;
            var target = rec.RestToPin < 4f || rec.Holed ? rec.pin : rec.Rest;
            float cut = end;
            if (rec.Holed || rec.closestToPin < 1f)
            {
                // Behind the putter until the ball is about 1.5 m out, then the cup camera.
                cut = Mathf.Max(TimeWithin(rec.pin, 1.6f), 1.4f);
                if (end - cut < 0.6f) cut = end;
            }
            plan.shots.Add(BehindPutter(plan.start, cut, target));
            if (cut < end) plan.shots.Add(Cup(cut, plan.end));
            if (rec.Holed) plan.Speed(end - 0.55f, end + 0.2f, s.slowMotion + 0.05f);
        }

        ReplayShot BehindPutter(float start, float end, Vector3 target)
        {
            float length = Flat(target - rec.Launch).magnitude;
            var wanted = rec.Launch - dir * Mathf.Clamp(length * 0.35f, 2.5f, 5f) + right * 0.45f + Vector3.up * 1.1f;
            var pos = spots.Place(wanted, new[] { rec.Launch, target }, 0.9f);
            var middle = Vector3.Lerp(rec.Launch, target, 0.5f);
            float fov = Mathf.Clamp(Vector3.Angle(rec.Launch - pos, target - pos) * 1.5f, 18f, 45f);
            return new ReplayShot
            {
                name = "behind the putter", start = start, end = end, from = pos, to = pos + (middle - pos).normalized * 0.4f,
                fovFrom = fov, fovTo = fov * 0.93f, lookAt = middle, track = 0.35f, frame = new Vector2(0f, -0.08f), sharpness = 2.5f,
            };
        }

        /// <summary>Low behind the hole, looking back along the ball's line: it rolls toward the lens and drops.</summary>
        ReplayShot Cup(float start, float end)
        {
            var incoming = Flat(rec.pin - rec.PositionAt(Mathf.Max(0f, rec.Duration - 1.2f)));
            if (incoming.sqrMagnitude < 0.01f) incoming = Flat(rec.pin - rec.Launch);
            var along = incoming.normalized;
            var side = Vector3.Cross(Vector3.up, along);
            var wanted = rec.pin + along * 1.9f + side * 0.45f + Vector3.up * 0.25f;
            var pos = spots.Place(wanted, new[] { rec.pin + Vector3.up * 0.05f }, 0.2f, 0.5f);
            var look = rec.pin + Vector3.up * 0.04f - along * 0.4f;
            return new ReplayShot
            {
                name = "cup", start = start, end = end, from = pos, to = pos - along * 0.25f, fovFrom = s.cupFov, fovTo = s.cupFov * 0.85f,
                lookAt = look, track = 0.3f, frame = new Vector2(0f, -0.05f), sharpness = 2f, wobble = 0.05f,
            };
        }

        // ---- shots through the air ----

        void PlanFlight(ReplayPlan plan)
        {
            float end = rec.Duration;
            float land = rec.LandTime >= 0f ? rec.LandTime : end; // water / out of bounds from the air: the splash
            float hit = rec.ObstacleTime;
            bool full = land >= FullFlight;
            bool overhead = full && Flat(rec.Rest - rec.Launch).magnitude >= s.overheadMeters;
            float hold = plan.end;
            if (overhead) hold = end + Mathf.Min(s.hold, 0.9f);

            // Where the action is after the strike: the tree, or the landing.
            float focus = hit >= 0f && hit < land ? hit : land;
            float approach = full ? s.slowLead : 0.45f;
            float dtlEnd = full ? Mathf.Clamp(focus * 0.4f, 1.4f, 3f) : focus - approach;
            dtlEnd = Mathf.Min(dtlEnd, focus - approach);
            if (dtlEnd < 0.5f) dtlEnd = Mathf.Min(0.5f, focus);
            plan.shots.Add(DownTheLine(plan.start, dtlEnd, full));

            float next = dtlEnd;
            if (full && focus - approach - dtlEnd >= 1.2f)
            {
                plan.shots.Add(Tower(dtlEnd, focus - approach, rec.PositionAt(focus)));
                next = focus - approach;
            }

            if (hit >= 0f && hit < land)
            {
                plan.shots.Add(Tree(next, hold, hit));
                plan.Speed(hit - 0.35f, hit + 0.45f, s.slowMotion);
            }
            else
            {
                float cupCut = rec.Holed && end - land > 2.2f ? end - 1.3f : hold;
                plan.shots.Add(Landing(next, Mathf.Min(cupCut, hold), land));
                if (cupCut < hold) plan.shots.Add(Cup(cupCut, hold));
                if (full || rec.Water) plan.Speed(land - s.slowLead, land + 0.3f, s.slowMotion);
                if (end - land > 3f && !rec.Holed) plan.Speed(land + 1.4f, end - 0.8f, s.rollFastForward);
            }
            if (rec.Holed) plan.Speed(end - 0.55f, end + 0.2f, s.slowMotion + 0.05f);
            if (overhead)
            {
                plan.shots.Add(Overhead(hold, hold + 2f));
                plan.end = hold + 2f;
            }
        }

        /// <summary>Low behind the golfer, slightly offset, on a long lens: holds on the strike, then tilts up after the ball.</summary>
        ReplayShot DownTheLine(float start, float end, bool full)
        {
            float back = full ? 11f : 6.5f;
            var wanted = rec.Launch - dir * back + right * (full ? 1.6f : 1.1f) + Vector3.up * 1.45f;
            var early = rec.PositionAt(Mathf.Min(end, 1f));
            var pos = spots.Place(wanted, new[] { rec.Launch, early }, 1.2f, 1.5f);
            float fov = full ? s.downTheLineFov : s.downTheLineFov + 8f;
            return new ReplayShot
            {
                name = "down the line", start = start, end = end, from = pos, to = pos + dir * 0.6f, fovFrom = fov, fovTo = fov * 1.12f,
                lookAt = rec.Launch + dir * 90f + Vector3.up * 6f, track = 0.6f, frame = new Vector2(0f, -0.08f), sharpness = 3.2f,
            };
        }

        /// <summary>A tower far to the side of the flight, panning with the ball, lead room ahead of it, tracer drawing.</summary>
        ReplayShot Tower(float start, float end, Vector3 landing)
        {
            var a = rec.PositionAt(start);
            var b = rec.PositionAt(end);
            var mid = rec.PositionAt((start + end) * 0.5f);
            float span = Flat(b - a).magnitude;
            float off = Mathf.Clamp(span * 0.7f, 70f, 140f);
            float height = Mathf.Clamp(rec.Apex * 0.9f, 12f, 45f); // a tower above the trees, level with the flight
            var side = Vector3.Cross(Vector3.up, dir);
            // It must see where the ball comes down too, so the descent ends over the fairway or green, not behind trees.
            var pos = spots.Search(Flat(mid) + Vector3.up * mid.y, new[] { mid, a, b, landing + Vector3.up * 0.5f }, new[] { off, off * 0.75f, off * 1.3f }, height,
                                   d => Mathf.Max(CameraSpots.Toward(side - dir * 0.3f)(d), CameraSpots.Toward(-side - dir * 0.3f)(d)), 4f);
            float fov = Mathf.Clamp(Vector3.Angle(a - pos, b - pos) * 0.8f, 18f, s.trackingFov);
            return new ReplayShot
            {
                name = "tower", start = start, end = end, from = pos, to = pos + dir * Mathf.Min(10f, span * 0.05f),
                fovFrom = fov, fovTo = fov * 0.92f, track = 1f, lead = 1f / 6f, frame = new Vector2(0f, 0.02f), sharpness = 5f,
            };
        }

        /// <summary>Beyond the landing spot looking back: the ball drops in from the sky, bounces and rolls toward the lens.</summary>
        ReplayShot Landing(float start, float end, float land)
        {
            var landAt = rec.PositionAt(land);
            var roll = Flat(rec.Rest - landAt);
            float distance = Mathf.Clamp(rec.Carry * 0.1f, 12f, 28f);
            float along = Mathf.Max(distance, Vector3.Dot(roll, dir) + 9f);
            float sideSign = Vector3.Dot(roll, right) > 2f ? -1f : 1f; // not in the roll's way
            var wanted = dir + right * sideSign * 0.45f;
            var incoming = rec.PositionAt(Mathf.Max(0f, land - 0.5f));
            var pos = spots.Search(landAt, new[] { landAt + Vector3.up * 0.3f, rec.Rest + Vector3.up * 0.1f, incoming },
                                   new[] { along, along * 0.75f, along * 1.3f }, 2.2f, CameraSpots.Toward(wanted), 1f);
            var look = Vector3.Lerp(landAt, rec.Rest, 0.35f);
            return new ReplayShot
            {
                name = "landing", start = start, end = end, from = pos, to = pos + (look - pos).normalized * 1.5f,
                fovFrom = s.landingFov, fovTo = s.landingFov * 0.8f, lookAt = look, track = 0.75f, frame = new Vector2(0f, -0.06f), sharpness = 2.6f,
            };
        }

        /// <summary>On the tree, from open ground on the side the ball came in: the clatter in slow motion, then where it drops.</summary>
        ReplayShot Tree(float start, float end, float hit)
        {
            var at = rec.PositionAt(hit);
            var incoming = Flat(at - rec.PositionAt(Mathf.Max(0f, hit - 0.6f)));
            var along = incoming.sqrMagnitude > 0.01f ? incoming.normalized : dir;
            var side = Vector3.Cross(Vector3.up, along);
            var pos = spots.Search(at, new[] { at, rec.Rest + Vector3.up * 0.2f }, new[] { 32f, 24f, 45f }, 3f,
                                   d => Mathf.Max(CameraSpots.Toward(-along + side)(d), CameraSpots.Toward(-along - side)(d)), 6f);
            float fov = Mathf.Clamp(Vector3.Angle(at - pos, rec.Rest - pos) * 1.6f, 22f, 40f);
            return new ReplayShot
            {
                name = "tree", start = start, end = end, from = pos, to = pos + (at - pos).normalized * 2f, fovFrom = fov, fovTo = fov * 0.9f,
                lookAt = Vector3.Lerp(at, rec.Rest, 0.4f), track = 0.6f, frame = Vector2.zero, sharpness = 2.4f,
            };
        }

        /// <summary>A blimp shot from high behind the tee: the whole tracer laid over the hole, ball at rest at the far end.</summary>
        ReplayShot Overhead(float start, float end)
        {
            var a = rec.Launch;
            var b = rec.Rest;
            float length = Flat(b - a).magnitude;
            var look = Vector3.Lerp(a, b, 0.55f);
            var wanted = a - dir * length * 0.3f + right * length * 0.12f + Vector3.up * (length * 0.45f + rec.Apex);
            var pos = spots.Place(wanted, new[] { look }, 20f, 10f);
            float fov = Mathf.Clamp(Vector3.Angle(a - pos, b - pos) * 1.25f, 25f, 55f); // the whole line with a margin
            return new ReplayShot
            {
                name = "overhead", start = start, end = end, from = pos, to = Vector3.Lerp(pos, look, 0.06f), fovFrom = fov, fovTo = fov * 0.95f,
                lookAt = look, track = 0f, sharpness = 3f, wobble = 0.03f,
            };
        }

        /// <summary>First time the ball is within this distance of the point, or the end.</summary>
        float TimeWithin(Vector3 point, float distance)
        {
            for (float t = 0f; t < rec.Duration; t += ShotRecording.SampleTime * 4f)
                if (Flat(rec.PositionAt(t) - point).magnitude <= distance) return t;
            return rec.Duration;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
