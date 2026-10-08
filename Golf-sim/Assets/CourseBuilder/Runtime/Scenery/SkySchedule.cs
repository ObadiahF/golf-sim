namespace GolfSim.Course
{
    /// <summary>
    /// The time of day of each hole in a round left on Auto. A round is an afternoon, not a slideshow: it starts at
    /// a time its first hole's theme likes (ThemeScenery.startWeights) and after each hole the light may move on
    /// toward night (golden hour, dusk, night) but never back. Everything comes from the round's seed, so a hole
    /// always gets the same sky in that round (a reload, a resumed server game).
    /// </summary>
    public static class SkySchedule
    {
        /// <summary>Chance, per hole played, that the light moves on to the next time of day.</summary>
        public const float AdvanceChance = 0.22f;
        /// <summary>Sunset: chance per hole that golden hour gives way to dusk, and the hole by which it always has.</summary>
        public const float SunsetChance = 0.3f;
        public const int SunsetDuskBy = 5;

        public static TimeOfDay For(uint seed, int holeIndex, ThemeScenery firstHole)
        {
            var time = firstHole.PickStart(Roll(seed, 0));
            for (int hole = 1; hole <= holeIndex && time < TimeOfDay.Night; hole++)
                if (Roll(seed, hole) < AdvanceChance) time++;
            return time;
        }

        /// <summary>The choice's fixed time, else the round's schedule (Auto, or a Sunset round's).</summary>
        public static TimeOfDay For(SkyChoice choice, uint seed, int holeIndex, ThemeScenery firstHole) =>
            choice.Fixed() ?? (choice == SkyChoice.Sunset ? Sunset(seed, holeIndex) : For(seed, holeIndex, firstHole));

        /// <summary>A Sunset round: golden hour on the first tee, then dusk from a hole the seed picks (by SunsetDuskBy at the latest).</summary>
        public static TimeOfDay Sunset(uint seed, int holeIndex)
        {
            var time = TimeOfDay.GoldenHour;
            for (int hole = 1; hole <= holeIndex && time < TimeOfDay.Dusk; hole++)
                if (hole >= SunsetDuskBy || Roll(seed, hole) < SunsetChance) time = TimeOfDay.Dusk;
            return time;
        }

        /// <summary>A stable 0..1 roll per (seed, hole): a few rounds of an integer hash (no System.Random state).</summary>
        static float Roll(uint seed, int hole)
        {
            uint h = seed * 0x9E3779B1u ^ (uint)(hole + 1) * 0x85EBCA77u;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
