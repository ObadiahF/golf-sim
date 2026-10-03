using System;
using UnityEngine;

namespace GolfSim.Ball
{
    /// <summary>One club: its name and short label (as in the SwingRemote app), a typical shot, its carry and loft.</summary>
    public readonly struct Club
    {
        public readonly string name;
        public readonly string shortName;
        /// <summary>The typical shot, tagged with this club's name.</summary>
        public readonly ShotData shot;
        /// <summary>Typical carry in yards, for picking a club by distance (0 for the putter).</summary>
        public readonly float carryYards;
        /// <summary>Loft in degrees: how well it gets under a ball sitting down (BallPhysicsSettings.LieResponse.minLoft).</summary>
        public readonly float loft;

        public Club(string name, string shortName, ShotData shot, float carryYards, float loft)
        {
            this.name = name;
            this.shortName = shortName;
            shot.club = name;
            this.shot = shot;
            this.carryYards = carryYards;
            this.loft = loft;
        }

        public bool IsPutter => carryYards <= 0f;
    }

    /// <summary>
    /// The bag, 14 clubs: TrackMan PGA Tour averages (ball speed, launch, spin, carry) from the driver to the pitching
    /// wedge; the gap, sand and lob wedges continue the trend (carries from the fitted flight model); plus a putt.
    /// </summary>
    public static class Clubs
    {
        public const string Putter = "Putter";
        public const string SandWedge = "Sand Wedge";

        public static readonly Club[] Bag =
        {
            new Club("Driver", "DR", ShotData.FromMph(167, 10.9f, 0, 2686, 0), 275, 10.5f),
            new Club("3 Wood", "3W", ShotData.FromMph(158, 9.2f, 0, 3655, 0), 243, 15f),
            new Club("5 Wood", "5W", ShotData.FromMph(152, 9.4f, 0, 4350, 0), 230, 18f),
            new Club("4 Hybrid", "4H", ShotData.FromMph(146, 10.2f, 0, 4437, 0), 225, 22f),
            new Club("5 Iron", "5I", ShotData.FromMph(132, 12.1f, 0, 5361, 0), 194, 27f),
            new Club("6 Iron", "6I", ShotData.FromMph(127, 14.1f, 0, 6231, 0), 183, 30f),
            new Club("7 Iron", "7I", ShotData.FromMph(120, 16.3f, 0, 7097, 0), 172, 34f),
            new Club("8 Iron", "8I", ShotData.FromMph(115, 18.1f, 0, 7998, 0), 160, 38f),
            new Club("9 Iron", "9I", ShotData.FromMph(109, 20.4f, 0, 8647, 0), 148, 42f),
            new Club("Pitching Wedge", "PW", ShotData.FromMph(102, 24.2f, 0, 9304, 0), 136, 46f),
            new Club("Gap Wedge", "GW", ShotData.FromMph(96, 26.5f, 0, 9600, 0), 120, 50f),
            new Club(SandWedge, "SW", ShotData.FromMph(88, 29.5f, 0, 9900, 0), 105, 56f),
            new Club("Lob Wedge", "LW", ShotData.FromMph(80, 32.5f, 0, 10100, 0), 90, 60f),
            new Club(Putter, "PT", ShotData.FromMph(5, 1f, 0, 0, 0), 0, 3f),
        };

        /// <summary>Old names still accepted (phones and saved settings from before the bag grew).</summary>
        static string Alias(string name) =>
            string.Equals(name, "Wedge", StringComparison.OrdinalIgnoreCase) ? "Pitching Wedge" : name;

        /// <summary>Index of the club with this name or short label (case-insensitive), or -1.</summary>
        public static int IndexOf(string name)
        {
            string key = Alias(name?.Trim());
            return Array.FindIndex(Bag, c => string.Equals(c.name, key, StringComparison.OrdinalIgnoreCase) ||
                                             string.Equals(c.shortName, key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The loft this shot was hit with: its club's, or for a shot that doesn't name one (panel sliders, older
        /// phones) the loft of the club whose typical launch and spin are closest.
        /// </summary>
        public static float LoftOf(ShotData shot)
        {
            int i = IndexOf(shot.club);
            if (i >= 0) return Bag[i].loft;
            var best = Bag[0];
            float bestScore = float.MaxValue;
            foreach (var c in Bag)
            {
                float dl = (shot.launchAngle - c.shot.launchAngle) / 10f, ds = (shot.backspin - c.shot.backspin) / 3000f;
                float score = dl * dl + ds * ds;
                if (score < bestScore) { bestScore = score; best = c; }
            }
            return best.loft;
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

        /// <summary>Clubs with less loft than this are no use from a bunker (woods and hybrids catch the sand or the lip).</summary>
        public const float BunkerMinLoft = 30f;
        /// <summary>Yards to the pin within which a bunker shot is a splash out with the sand wedge.</summary>
        const float GreensideBunkerYards = 60f;

        /// <summary>
        /// A sensible club for this distance and lie: the putter on the green, the sand wedge in a greenside bunker,
        /// otherwise the shortest club that carries far enough (the longest playable one when nothing does). From a
        /// bunker that is an iron at most: woods and hybrids don't get the ball out.
        /// </summary>
        public static string Suggest(float yardsToPin, string lie)
        {
            if (lie == "green") return Putter;
            bool bunker = lie == "bunker";
            if (bunker && yardsToPin <= GreensideBunkerYards) return SandWedge;
            string longest = null;
            for (int i = Bag.Length - 1; i >= 0; i--)
            {
                if (Bag[i].IsPutter || (bunker && Bag[i].loft < BunkerMinLoft)) continue;
                if (Bag[i].carryYards >= yardsToPin) return Bag[i].name;
                longest = Bag[i].name;
            }
            return longest ?? Bag[0].name;
        }
    }
}
