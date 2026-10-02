using GolfSim.Course;
using UnityEngine;

namespace GolfSim.Ball
{
    /// <summary>
    /// Toptracer-style shot trace: a glowing line (TracerLine) drawn behind the ball from launch to rest.
    /// Its width is recomputed from camera distance so it keeps a steady on-screen thickness,
    /// and its HDR colour lets bloom give it a glow.
    /// </summary>
    [RequireComponent(typeof(GolfBall))]
    public class BallTracer : MonoBehaviour
    {
        // Mildly HDR: enough for bloom to glow, not so bright that tonemapping bleaches it to white.
        [ColorUsage(false, true)] public Color color = new Color(1.3f, 0.78f, 0.03f);
        [Tooltip("Line thickness as a fraction of camera distance (about 0.012 = Toptracer weight).")]
        public float screenWidth = 0.012f;
        public float minWidth = 0.02f;
        [Tooltip("Meters between recorded points.")] public float spacing = 0.75f;
        [Tooltip("Keep tracing while the ball rolls.")] public bool traceRoll = true;
        public Material material;

        GolfBall ball;
        TracerLine line;

        /// <summary>Hides the trace without losing it (e.g. while the instant replay draws its own).</summary>
        public bool Hidden
        {
            get => line != null && line.Hidden;
            set { if (line != null) line.Hidden = value; }
        }

        void Awake()
        {
            ball = GetComponent<GolfBall>();
            line = new TracerLine("Shot Tracer", material, color) { screenWidth = screenWidth, minWidth = minWidth, spacing = spacing };
            ball.ShotStarted += OnShotStarted;
            ball.Placed += Clear; // reset to the tee, a drop, the next turn: the old trace goes
        }

        void OnDestroy()
        {
            if (ball)
            {
                ball.ShotStarted -= OnShotStarted;
                ball.Placed -= Clear;
            }
            line?.Destroy();
        }

        void OnShotStarted(GolfBall b)
        {
            Clear(b);
            line.Add(b.LaunchPoint);
        }

        void Clear(GolfBall b) => line.Clear();

        void LateUpdate()
        {
            bool tracing = ball.Status == BallStatus.Flying || (traceRoll && ball.Status == BallStatus.Rolling);
            if (tracing && line.Count > 0) line.Extend(ball.transform.position);
            line.Draw(tracing ? ball.transform.position : (Vector3?)null, Camera.main);
        }

        /// <summary>Unlit, alpha-blended material for lines on the course (also used by AimLine).</summary>
        public static Material DefaultMaterial(Color hdrColor)
        {
            // Alpha-blended (not additive: yellow added onto a bright sky turns white), slightly HDR so bloom glows.
            var mat = RuntimeMaterials.Create(m => m.line, "Line");
            mat.SetColor("_BaseColor", hdrColor);
            return mat;
        }
    }
}
