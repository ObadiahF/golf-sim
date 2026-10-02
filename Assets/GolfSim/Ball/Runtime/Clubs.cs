using System;
using UnityEngine;

namespace GolfSim.Ball
{
    /// <summary>One club: its name and short label (as in the SwingRemote app), a typical shot and its carry.</summary>
    public readonly struct Club
    {
        public readonly string name;
        public readonly string shortName;
        public readonly ShotData shot;
        /// <summary>Typical carry in yards, for picking a club by distance (0 for the putter).</summary>
        public readonly float carryYards;

        public Club(string name, string shortName, ShotData shot, float carryYards)
        {
            this.name = name;
            this.shortName = shortName;
            this.shot = shot;
            this.carryYards = carryYards;
        }

        public bool IsPutter => carryYards <= 0f;
    }

    /// <summary>The bag: TrackMan PGA Tour averages (ball speed, launch, spin), plus a putt.</summary>
    public static class Clubs
    {
        public const string Putter = "Putter";

        public static readonly Club[] Bag =
        {
            new Club("Driver", "DR", ShotData.FromMph(167, 10.9f, 0, 2686, 0), 275),
            new Club("3 Wood", "3W", ShotData.FromMph(158, 9.2f, 0, 3655, 0), 243),
            new Club("5 Iron", "5I", ShotData.FromMph(132, 12.1f, 0, 5361, 0), 194),
            new Club("7 Iron", "7I", ShotData.FromMph(120, 16.3f, 0, 7097, 0), 172),
            new Club("9 Iron", "9I", ShotData.FromMph(109, 20.4f, 0, 8647, 0), 148),
            new Club("Wedge", "PW", ShotData.FromMph(102, 24.2f, 0, 9304, 0), 136),
            new Club(Putter, "PT", ShotData.FromMph(5, 1f, 0, 0, 0), 0),
        };

        /// <summary>Index of the club with this name or short label (case-insensitive), or -1.</summary>
        public static int IndexOf(string name)
        {
            string key = name?.Trim();
            return Array.FindIndex(Bag, c => string.Equals(c.name, key, StringComparison.OrdinalIgnoreCase) ||
                                             string.Equals(c.shortName, key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The club with this name, or the driver.</summary>
        public static Club Find(string name) => Bag[Mathf.Max(0, IndexOf(name))];

        /// <summary>The bag's own spelling of this club name, or null if it isn't one.</summary>
        public static string Normalize(string name)
        {
            int i = IndexOf(name);
            return i < 0 ? null : Bag[i].name;
        }

        /// <summary>The club after (step +1) or before (-1) this one, wrapping around the bag.</summary>
        public static string Step(string name, int step) =>
            Bag[((Mathf.Max(0, IndexOf(name)) + step) % Bag.Length + Bag.Length) % Bag.Length].name;

        /// <summary>
        /// A sensible club for this distance and lie: the putter on the green, otherwise the shortest club
        /// that carries far enough (the driver when nothing does).
        /// </summary>
        public static string Suggest(float yardsToPin, string lie)
        {
            if (lie == "green") return Putter;
            for (int i = Bag.Length - 1; i >= 0; i--)
                if (!Bag[i].IsPutter && Bag[i].carryYards >= yardsToPin) return Bag[i].name;
            return Bag[0].name;
        }
    }
}
