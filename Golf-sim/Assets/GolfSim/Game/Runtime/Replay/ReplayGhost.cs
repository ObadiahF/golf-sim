using GolfSim.Ball;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// What the replay draws instead of the real ball: a copy of the ball's look moved along the recording (never too
    /// small to see on a long lens) and the tracer drawn from the recording up to the replay time. A ball that goes
    /// into the water disappears at the splash, which ripples (ReplaySplash).
    /// </summary>
    public class ReplayGhost
    {
        const float MinPixels = 3f; // the ball's smallest on-screen size, like the dot at a broadcast tracer's head

        readonly GameObject root;
        readonly Transform look;
        readonly TracerLine tracer;
        readonly ReplaySplash splash;
        readonly float diameter;
        ShotRecording rec;
        int drawn;
        float spin;
        bool traced;

        public ReplayGhost(GolfBall ball, Transform parent)
        {
            root = new GameObject("Replay Ball");
            root.transform.SetParent(parent, false);
            look = new GameObject("Look").transform;
            look.SetParent(root.transform, false);
            foreach (var source in ball.GetComponentsInChildren<MeshRenderer>())
            {
                var filter = source.GetComponent<MeshFilter>();
                if (!filter) continue;
                var copy = new GameObject(source.name);
                copy.transform.SetParent(look, false);
                copy.transform.localPosition = ball.transform.InverseTransformPoint(source.transform.position);
                copy.transform.localRotation = Quaternion.Inverse(ball.transform.rotation) * source.transform.rotation;
                copy.transform.localScale = source.transform.lossyScale;
                copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var renderer = copy.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = source.sharedMaterials;
                foreach (var m in renderer.materials) // own copies, drawn after the cup like the ball
                    if (m.renderQueue < GolfBall.RenderQueue) m.renderQueue = GolfBall.RenderQueue;
                renderer.shadowCastingMode = source.shadowCastingMode;
            }
            diameter = BallPhysicsSettings.Radius * 2f;
            var traceStyle = ball.GetComponent<BallTracer>();
            tracer = new TracerLine("Replay Tracer", traceStyle ? traceStyle.material : null,
                                    traceStyle ? traceStyle.color : new Color(1.3f, 0.78f, 0.03f));
            tracer.gameObject.transform.SetParent(parent, false);
            splash = new ReplaySplash(parent);
            Show(false);
        }

        public void Begin(ShotRecording recording)
        {
            rec = recording;
            drawn = 0;
            traced = !recording.Rolled; // putts are shown without a tracer, as on TV: just the ball rolling
            tracer.Clear();
        }

        public void Show(bool show)
        {
            root.SetActive(show);
            tracer.Hidden = !show || !traced;
            if (!show) splash.Hide();
        }

        /// <summary>Moves the ball to recording time `time` and draws the tracer up to it.</summary>
        public void Draw(float time, Camera cam, float realDelta)
        {
            bool sunk = rec.SplashTime >= 0f && time >= rec.SplashTime;
            float t = sunk ? rec.SplashTime : time; // the ball and tracer stop at the water's surface
            var at = sunk ? rec.SplashPoint : rec.PositionAt(t);
            root.SetActive(!sunk); // a holed ball shows at the bottom of the cup (it draws after the cup: GolfBall.RenderQueue)
            splash.Draw(rec.SplashPoint, rec.SplashTime >= 0f ? time - rec.SplashTime : -1f, cam);
            root.transform.position = at;
            var v = rec.VelocityAt(t);
            spin += v.magnitude / (diameter * 0.5f) * Mathf.Rad2Deg * Mathf.Min(realDelta, 0.05f) * 0.15f;
            look.localRotation = Quaternion.Euler(spin, 0f, 0f);
            if (cam)
            {
                float pixelAngle = cam.fieldOfView * Mathf.Deg2Rad / Mathf.Max(1, cam.pixelHeight);
                float onScreen = Vector3.Distance(cam.transform.position, at) * pixelAngle * MinPixels;
                root.transform.localScale = Vector3.one * Mathf.Max(1f, onScreen / diameter);
                root.transform.rotation = Quaternion.LookRotation(v.sqrMagnitude > 0.01f ? v : Vector3.forward);
            }

            // The tracer: every sample up to now, thinned to the tracer spacing, plus the head at the ball.
            int upTo = Mathf.Clamp(Mathf.FloorToInt(t / ShotRecording.SampleTime), 0, rec.samples.Count - 1);
            if (upTo < drawn) { tracer.Clear(); drawn = 0; }
            if (tracer.Count == 0 && t >= 0f) tracer.Add(rec.Launch);
            for (; drawn <= upTo; drawn++) tracer.Extend(rec.samples[drawn]);
            tracer.Draw(t >= 0f ? at : (Vector3?)null, cam);
        }

        public void Destroy()
        {
            if (root) Object.Destroy(root);
            tracer.Destroy();
            splash.Destroy();
        }
    }
}
