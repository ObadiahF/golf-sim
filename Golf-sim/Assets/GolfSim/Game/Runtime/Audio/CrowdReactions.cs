using System.Collections;
using GolfSim.Ball;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// The gallery reacts to each finished shot (from its recording and the round's score): a roar for a hole-in-one
    /// or an eagle, a cheer for a birdie, a chip-in or a long putt, polite applause for a par or a shot close to the
    /// pin, an "ooh" for a lip-out, a near miss or a tree, a groan for the water.
    /// </summary>
    public class CrowdReactions : MonoBehaviour
    {
        const float Delay = 0.35f;        // s; a crowd reacts a moment after the ball stops
        const float LipOut = 0.15f;       // m; passed this close to the hole without dropping
        const float LongPutt = 6f;        // m
        const float LongDriveYards = 250f;

        ShotRecorder recorder;

        void OnEnable()
        {
            recorder = GetComponent<ShotRecorder>();
            if (recorder) recorder.Recorded += OnRecorded;
        }

        void OnDisable()
        {
            if (recorder) recorder.Recorded -= OnRecorded;
        }

        void OnRecorded(ShotRecording rec) => StartCoroutine(React(rec));

        IEnumerator React(ShotRecording rec)
        {
            // Wait until the round has scored the shot (it listens to the same ball).
            yield return new WaitForSecondsRealtime(Delay);
            var (id, volume) = Reaction(rec);
            if (volume > 0f) GameAudio.Play(id, null, volume);
        }

        /// <summary>The crowd's reaction to this shot and how loud (0 = none).</summary>
        public static (SoundId, float) Reaction(ShotRecording rec)
        {
            bool putt = CourseSurface.IsGreen(rec.setup.lie);
            if (rec.Water) return (SoundId.CrowdGroan, 1f);
            if (rec.end == BallStatus.OutOfBounds) return (SoundId.CrowdGroan, 0.6f);
            if (rec.Holed) return Holed(rec, putt);
            if (rec.closestToPin < LipOut) return (SoundId.CrowdOoh, 1f);
            if (rec.ObstacleTime >= 0f) return (SoundId.CrowdOoh, 0.55f);
            if (!putt && rec.StartToPin > 30f)
            {
                if (rec.RestToPin < 1f) return (SoundId.CrowdCheer, 0.8f);
                if (rec.RestToPin < 3f) return (SoundId.CrowdApplause, 0.9f);
                if (rec.RestToPin < 8f && rec.Carry > 20f) return (SoundId.CrowdApplause, 0.45f);
            }
            if (putt && rec.RestToPin < 0.5f && rec.StartToPin > LongPutt) return (SoundId.CrowdApplause, 0.6f); // lag putt to tap-in
            if (rec.Total * ShotData.YardsPerMeter >= LongDriveYards && rec.result.restingSurface == "fairway")
                return (SoundId.CrowdApplause, 0.7f);
            return (SoundId.CrowdApplause, 0f);
        }

        static (SoundId, float) Holed(ShotRecording rec, bool putt)
        {
            var round = RoundDirector.Instance ? RoundDirector.Instance.Round : null;
            var player = round?.CurrentBall;
            if (player != null && player.holed)
            {
                int toPar = player.strokes - round.Par;
                if (player.strokes == 1 || toPar <= -2) return (SoundId.CrowdRoar, 1f);
                if (toPar == -1 || !putt || rec.StartToPin >= LongPutt) return (SoundId.CrowdCheer, 1f);
                return (SoundId.CrowdApplause, toPar == 0 ? 0.85f : 0.5f);
            }
            // Practice (no score): by the shot alone.
            if (!putt && rec.setup.lie == "tee") return (SoundId.CrowdRoar, 1f);
            if (!putt || rec.StartToPin >= LongPutt) return (SoundId.CrowdCheer, 1f);
            return (SoundId.CrowdApplause, 0.7f);
        }
    }
}
