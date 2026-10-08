using GolfSim.Ball;
using GolfSim.Course;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// Celebrations when a ball drops: a banner ("BIRDIE!", "EAGLE!", "HOLE IN ONE!"), a jingle, and for the big ones
    /// confetti and fireworks over the green (CupFireworks). Chip-ins and long putts get a smaller one even for par.
    /// The next turn and the automatic replay wait until it is over (Celebrating).
    /// </summary>
    public partial class RoundDirector
    {
        const float LongPuttMeters = 8f;
        static readonly Color Gold = new Color32(255, 214, 92, 255);

        float celebrationUntil;

        /// <summary>A celebration is on screen: the round and the replay hold off meanwhile.</summary>
        public static bool Celebrating => Instance && Time.realtimeSinceStartup < Instance.celebrationUntil;

        /// <summary>What a holed shot deserves; null for an ordinary one.</summary>
        public class Celebration
        {
            public string headline, detail;
            public SoundId jingle;
            public int fireworks;
            public bool confetti;
            public float seconds;
            public Color color;
        }

        /// <summary>
        /// The celebration for a holed shot: by the score (strokes and par; par 0 = practice, no score), the lie it was
        /// hit from and its distance (m).
        /// </summary>
        public static Celebration CelebrationFor(string player, int strokes, int par, string fromLie, float meters, Color accent)
        {
            bool putt = CourseSurface.IsGreen(fromLie);
            bool fromTee = fromLie == "tee";
            string from = putt ? $"{meters:0.0} m putt" : $"from {meters * ShotData.YardsPerMeter:0} yd";
            if (strokes == 1 || (par == 0 && fromTee))
                return Big("HOLE IN ONE!", $"{player} · {from}", SoundId.JingleAce, 6, 5.5f, Gold);
            if (par == 0) return null; // practice: only an ace
            int toPar = strokes - par;
            if (toPar <= -3) return Big("ALBATROSS!", $"{player} · {from}", SoundId.JingleAce, 5, 5f, Gold);
            if (toPar == -2) return Big("EAGLE!", $"{player} · {from}", SoundId.JingleEagle, 3, 4f, Gold);
            bool chipIn = !putt && !fromTee;
            bool longPutt = putt && meters >= LongPuttMeters;
            if (toPar == -1)
                return Small(chipIn ? "CHIP-IN BIRDIE!" : longPutt ? "BIRDIE BOMB!" : "BIRDIE!", $"{player} · {from}", 3f, accent, confetti: chipIn || longPutt);
            if (chipIn) return Small("CHIP-IN!", $"{player} · {from}", 2.6f, accent);
            if (longPutt) return Small("WHAT A PUTT!", $"{player} · {from}", 2.6f, accent);
            return null;
        }

        static Celebration Big(string headline, string detail, SoundId jingle, int fireworks, float seconds, Color color) =>
            new Celebration { headline = headline, detail = detail, jingle = jingle, fireworks = fireworks, confetti = true, seconds = seconds, color = color };

        static Celebration Small(string headline, string detail, float seconds, Color color, bool confetti = false) =>
            new Celebration { headline = headline, detail = detail, jingle = SoundId.JingleBirdie, seconds = seconds, color = color, confetti = confetti };

        /// <summary>Celebrates the shot that just went in, if it deserves it; true if it did (the toast then stays quiet).</summary>
        bool CelebrateHoled(GolfBall finished, string player, int strokes, int par, int playerIndex)
        {
            if (!hole) return false;
            float meters = Vector3.ProjectOnPlane(finished.LaunchPoint - hole.PinWorld, Vector3.up).magnitude;
            var c = CelebrationFor(player, strokes, par, finished.Result.lie, meters, TurnBanner.AccentFor(playerIndex));
            if (c == null) return false;
            Play(c);
            return true;
        }

        /// <summary>Puts on a celebration (also for tools and tests).</summary>
        public void Play(Celebration c)
        {
            celebrationUntil = Time.realtimeSinceStartup + c.seconds;
            GameAudio.Play(c.jingle);
            hud?.Banner.Shout(c.headline, c.detail, c.color, c.seconds, c.confetti);
            if (c.fireworks > 0 && hole) CupFireworks.Launch(hole.PinWorld, c.fireworks, Sky is TimeOfDay.Night or TimeOfDay.Dusk);
        }

        /// <summary>Leaving the hole or the round: no celebration holds anything up.</summary>
        void EndCelebration() => celebrationUntil = 0f;
    }
}
