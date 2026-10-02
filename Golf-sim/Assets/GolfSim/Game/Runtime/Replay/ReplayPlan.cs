using System.Collections.Generic;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// A replay's edit: its camera shots in order, and the playback speed over the recording (slow motion into the
    /// landing, the tree, the cup or the water; a quicker roll-out). Times are recording times (0 = the strike).
    /// </summary>
    public class ReplayPlan
    {
        public readonly List<ReplayShot> shots = new List<ReplayShot>();
        public float start, end;
        readonly List<(float from, float to, float speed)> windows = new List<(float, float, float)>();

        const float Ramp = 0.18f; // s of recording time to ease into and out of a speed change

        /// <summary>Plays [from, to] at this speed (0.3 = slow motion), easing in and out.</summary>
        public void Speed(float from, float to, float speed)
        {
            if (to > from) windows.Add((from, to, speed));
        }

        /// <summary>Playback speed at this recording time: the strongest window wins (slow motion over a fast-forward).</summary>
        public float SpeedAt(float t)
        {
            float slow = 1f, fast = 1f;
            foreach (var (from, to, s) in windows)
            {
                float w = Mathf.Clamp01(Mathf.Min(t - (from - Ramp), (to + Ramp) - t) / Ramp);
                float here = Mathf.Lerp(1f, s, w * w * (3f - 2f * w));
                slow = Mathf.Min(slow, here);
                fast = Mathf.Max(fast, here);
            }
            return slow < 1f ? slow : fast;
        }

        /// <summary>The shot showing time t (the last one holds after the end).</summary>
        public ReplayShot ShotAt(float t)
        {
            for (int i = shots.Count - 1; i >= 0; i--)
                if (t >= shots[i].start) return shots[i];
            return shots.Count > 0 ? shots[0] : null;
        }

        public void Reset()
        {
            foreach (var s in shots) s.Reset();
        }

        /// <summary>Wall-clock length of the replay (for logs and tests).</summary>
        public float RealLength(float step = 1f / 120f)
        {
            float real = 0f;
            for (float t = start; t < end; t += step * SpeedAt(t)) real += step;
            return real;
        }
    }
}
