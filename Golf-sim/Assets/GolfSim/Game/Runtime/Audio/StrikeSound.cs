using GolfSim.Ball;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>How well a shot was struck, as it sounds (the clips are named for it: strike_iron_thin_2).</summary>
    public enum StrikeQuality { Pure, Solid, Thin, Fat, Toe }

    /// <summary>
    /// The sound of a strike: which club family (driver, woods and hybrids, irons, wedges, putter) and how well it was
    /// struck, judged from the launch against the club's typical shot (Clubs.Bag):
    /// a launch far too low is thin; a big tilt of the spin axis or a start well off line is off the toe or heel; slow
    /// and high is fat; near the club's speed, launch and a straight spin axis is pure; anything else is solid. Louder
    /// and a little higher the faster the ball. The same for the live shot and its replay.
    /// </summary>
    public static class StrikeSound
    {
        const float PuttSpeed = 12f; // m/s; slower than this off any club is a putt or a tap

        public static void Play(ShotData shot, string clubName, Vector3 at, bool flat)
        {
            var club = Clubs.Find(clubName);
            var id = Family(club, shot.ballSpeed);
            var quality = Quality(shot, club, id);
            float full = id switch // m/s of a full swing with this family
            {
                SoundId.StrikePutter => 10f,
                SoundId.StrikeDriver => 75f,
                SoundId.StrikeWood => 68f,
                SoundId.StrikeWedge => 45f,
                _ => 58f,
            };
            float k = Mathf.Clamp01(shot.ballSpeed / full);
            bool putt = id == SoundId.StrikePutter;
            GameAudio.Play(id, at, Mathf.Lerp(putt ? 0.25f : 0.45f, 1f, k), Mathf.Lerp(0.94f, 1.04f, k), flat,
                           quality.ToString().ToLowerInvariant());
        }

        public static SoundId Family(Club club, float ballSpeed)
        {
            if (club.IsPutter || ballSpeed < PuttSpeed) return SoundId.StrikePutter;
            if (club.name == "Driver") return SoundId.StrikeDriver;
            if (club.name.Contains("Wood") || club.name.Contains("Hybrid")) return SoundId.StrikeWood;
            if (club.name.Contains("Wedge")) return SoundId.StrikeWedge;
            return SoundId.StrikeIron;
        }

        public static StrikeQuality Quality(ShotData shot, Club club, SoundId family)
        {
            float offLine = Mathf.Abs(shot.launchDirection);
            if (family == SoundId.StrikePutter)
                return offLine > 4f ? StrikeQuality.Toe : offLine < 1.2f && shot.launchAngle <= 4f ? StrikeQuality.Pure : StrikeQuality.Solid;

            bool wedge = family == SoundId.StrikeWedge;
            float launch = shot.launchAngle / Mathf.Max(1f, club.shot.launchAngle);
            float speed = shot.ballSpeed / Mathf.Max(1f, club.shot.ballSpeed);
            float axis = shot.TotalSpin > 100f ? Mathf.Abs(shot.SpinAxis) : 0f;

            if (launch < 0.55f) return StrikeQuality.Thin;
            if (axis > 20f || offLine > 8f) return StrikeQuality.Toe;
            // A wedge is often swung softly on purpose (and then launches higher): only a real chunk is fat.
            if (wedge ? speed < 0.5f && launch > 1.6f : speed < 0.72f && launch > 1.25f) return StrikeQuality.Fat;
            if (axis < 7f && offLine < 4f && launch is > 0.75f and < 1.3f && speed >= (wedge ? 0.6f : 0.92f)) return StrikeQuality.Pure;
            return StrikeQuality.Solid;
        }
    }
}
