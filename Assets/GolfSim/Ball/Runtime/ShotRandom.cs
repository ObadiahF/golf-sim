using System;
using UnityEngine;

namespace GolfSim.Ball
{
    /// <summary>
    /// Small deterministic random generator (xorshift32) for the chance parts of a shot (tree canopies, rebound
    /// scatter). Seeded per shot, so the same shot from the same spot with the same seed replays identically.
    /// </summary>
    public struct ShotRandom
    {
        uint state;

        public ShotRandom(uint seed) => state = seed != 0 ? seed : 0x9E3779B9u;

        public uint NextUInt()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        /// <summary>Uniform in [0, 1).</summary>
        public float Next01() => (NextUInt() >> 8) * (1f / 16777216f);

        public float Range(float min, float max) => min + (max - min) * Next01();

        public float Range(Vector2 minMax) => Range(minMax.x, minMax.y);

        /// <summary>A seed made from the launch conditions and the spot the ball is hit from (FNV-1a).</summary>
        public static uint SeedFor(ShotData shot, Vector3 origin)
        {
            uint h = 2166136261u;
            foreach (float f in new[] { shot.ballSpeed, shot.launchAngle, shot.launchDirection, shot.backspin, shot.sidespin, origin.x, origin.y, origin.z })
            {
                h ^= unchecked((uint)BitConverter.SingleToInt32Bits(f));
                h *= 16777619u;
            }
            return h;
        }
    }
}
