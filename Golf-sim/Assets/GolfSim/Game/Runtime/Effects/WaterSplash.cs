using GolfSim.Ball;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// A ball going into the water throws up a splash where it went through the surface: a crown of droplets, a
    /// column of spray, a little mist, and rings spreading out over the water. Live from the shot's recording (which
    /// knows the exact splash point) and again when the replay reaches it. The sound is ShotSounds'.
    /// </summary>
    public class WaterSplash : MonoBehaviour
    {
        static readonly Color Droplet = new Color(0.86f, 0.93f, 1f, 0.95f);
        static readonly Color Spray = new Color(0.92f, 0.96f, 1f, 0.7f);

        ShotRecorder recorder;
        ReplayDirector replay;

        void OnEnable()
        {
            recorder = GetComponent<ShotRecorder>();
            if (recorder) recorder.Recorded += OnRecorded;
            replay = GetComponent<ReplayDirector>();
            if (replay) replay.EventPlayed += OnReplayEvent;
        }

        void OnDisable()
        {
            if (recorder) recorder.Recorded -= OnRecorded;
            if (replay) replay.EventPlayed -= OnReplayEvent;
        }

        void OnRecorded(ShotRecording rec)
        {
            if (rec.Water && rec.SplashTime >= 0f) Play(rec.SplashPoint);
        }

        void OnReplayEvent(ShotEvent e, ShotRecording rec)
        {
            if (e.kind == ShotEventKind.Water) Play(rec.SplashTime >= 0f ? rec.SplashPoint : e.position);
        }

        /// <summary>A splash at this point of the water's surface.</summary>
        public static void Play(Vector3 point)
        {
            var spark = Particles.Load(Particles.SparkDay);
            var ring = Particles.Load(Particles.Ripple);
            if (!spark || !ring) return;
            var root = new GameObject("Water Splash").transform;
            root.position = point;

            // Droplets thrown up and out in a crown, falling back.
            var crown = Particles.Make("Crown", root, point, spark, Droplet, 1.1f);
            var main = crown.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 8.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.22f);
            main.gravityModifier = 1f;
            Upward(crown, 28f, 0.25f);
            Particles.Burst(crown, 80);
            Particles.FadeOut(crown, 0.7f);

            // The column: bigger blobs of spray shooting straight up and spreading.
            var column = Particles.Make("Column", root, point, spark, Spray, 1.1f);
            main = column.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 11f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
            main.gravityModifier = 0.9f;
            Upward(column, 7f, 0.1f);
            Particles.Burst(column, 30);
            Particles.Grow(column, 1f, 2.4f);
            Particles.FadeOut(column, 0.4f);

            // A puff of mist hanging over it.
            var mist = Particles.Make("Mist", root, point + Vector3.up * 0.6f, spark, Spray, 1f);
            main = mist.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(1f, 1.8f);
            main.gravityModifier = -0.05f;
            Particles.Burst(mist, 12);
            Particles.Grow(mist, 1f, 2.2f);
            Particles.FadeOut(mist, 0.2f, 0.35f);

            // Rings spreading over the surface, one after another.
            var rings = Particles.Make("Rings", root, point + Vector3.up * 0.03f, ring, Color.white, 1f);
            main = rings.main;
            main.startLifetime = 2.4f;
            main.startSpeed = 0f;
            main.startSize = 7f;
            rings.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            var emission = rings.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1), new ParticleSystem.Burst(0.3f, 1), new ParticleSystem.Burst(0.6f, 1) });
            Particles.Grow(rings, 0.08f, 1f, 0.5f);
            Particles.FadeOut(rings, 0.1f, 0.85f);

            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>()) ps.Play();
            Destroy(root.gameObject, 3.5f);
        }

        /// <summary>Emits upward from a small disc, within `angle` of straight up.</summary>
        static void Upward(ParticleSystem ps, float angle, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = radius;
            shape.rotation = new Vector3(-90f, 0f, 0f);
        }
    }
}
