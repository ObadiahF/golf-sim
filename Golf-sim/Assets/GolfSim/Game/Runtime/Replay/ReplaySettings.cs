using System;
using GolfSim.Ball;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>Tuning for the instant replay: which shots replay by themselves, and how they are cut and paced.</summary>
    [Serializable]
    public class ReplaySettings
    {
        [Header("Which shots replay by themselves")]
        public bool autoReplay = true;
        [Tooltip("Drives (total distance) at least this long, yards.")]
        public float longDriveYards = 250f;
        [Tooltip("Approaches from at least this far (m) that finish within approachFinish (m) of the pin.")]
        public float approachFrom = 30f;
        public float approachFinish = 3f;
        [Tooltip("Holed putts at least this long, m (holed shots from off the green always replay).")]
        public float holedPuttMeters = 6f;
        public bool treeHits = true;
        public bool water = true;

        [Header("Pacing")]
        [Tooltip("Real seconds to watch the ball at rest before the replay starts.")]
        public float leadIn = 1.1f;
        [Tooltip("Recording seconds shown before the strike.")]
        public float preRoll = 0.7f;
        [Tooltip("Recording seconds held on the ball at rest.")]
        public float hold = 1.3f;
        [Tooltip("Slow-motion playback speed (landings, trees, the cup, splashes).")]
        [Range(0.1f, 1f)] public float slowMotion = 0.3f;
        [Tooltip("Recording seconds of slow motion before the first landing.")]
        public float slowLead = 0.6f;
        [Tooltip("Playback speed of a long roll-out once the landing has been seen.")]
        [Range(1f, 3f)] public float rollFastForward = 2f;
        [Tooltip("Show a high overhead of the whole tracer after shots at least this long, m.")]
        public float overheadMeters = 170f;

        [Header("Lenses (vertical field of view, degrees)")]
        public float downTheLineFov = 22f;
        public float trackingFov = 28f;
        public float landingFov = 26f;
        public float cupFov = 32f;

        /// <summary>Why this shot deserves a replay by itself, or null.</summary>
        public string Reason(ShotRecording rec)
        {
            if (!autoReplay) return null;
            bool putt = CourseSurface.IsGreen(rec.setup.lie);
            if (rec.Holed && !putt) return "holed from off the green";
            if (rec.Holed && rec.StartToPin >= holedPuttMeters) return "long putt holed";
            if (water && rec.Water) return "water";
            if (treeHits && rec.ObstacleTime >= 0f) return "hit a tree";
            if (rec.Total * ShotData.YardsPerMeter >= longDriveYards && !rec.Water && rec.end != BallStatus.OutOfBounds) return "long drive";
            if (!putt && rec.StartToPin >= approachFrom && rec.RestToPin <= approachFinish) return "close approach";
            return null;
        }
    }
}
