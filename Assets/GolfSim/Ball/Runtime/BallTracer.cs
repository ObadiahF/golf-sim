using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GolfSim.Ball
{
    /// <summary>
    /// Toptracer-style shot trace: a glowing line drawn behind the ball from launch to rest.
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
        LineRenderer line;
        readonly List<Vector3> points = new List<Vector3>();
        readonly List<float> distances = new List<float>(); // cumulative length, for the width curve
        readonly AnimationCurve widthCurve = new AnimationCurve();

        void Awake()
        {
            ball = GetComponent<GolfBall>();
            line = new GameObject("Shot Tracer").AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.numCapVertices = 4;
            line.numCornerVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.textureMode = LineTextureMode.Stretch;
            line.sharedMaterial = material ? material : DefaultMaterial(color);
            line.colorGradient = Fade(); // vertex colours are 8-bit, so the HDR tint lives on the material
            line.positionCount = 0;
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
            if (line) Destroy(line.gameObject);
        }

        void OnShotStarted(GolfBall b)
        {
            Clear(b);
            Add(b.LaunchPoint);
        }

        void Clear(GolfBall b)
        {
            points.Clear();
            distances.Clear();
            if (line) line.positionCount = 0;
        }

        void LateUpdate()
        {
            bool tracing = ball.Status == BallStatus.Flying || (traceRoll && ball.Status == BallStatus.Rolling);
            if (tracing && points.Count > 0 && Vector3.Distance(points[points.Count - 1], ball.transform.position) >= spacing)
                Add(ball.transform.position);
            Draw(tracing);
        }

        void Add(Vector3 p)
        {
            distances.Add(points.Count == 0 ? 0f : distances[distances.Count - 1] + Vector3.Distance(points[points.Count - 1], p));
            points.Add(p);
        }

        void Draw(bool tracing)
        {
            int count = points.Count + (tracing ? 1 : 0); // live head follows the ball between samples
            if (count < 2) { line.positionCount = 0; return; }
            line.positionCount = count;
            for (int i = 0; i < points.Count; i++) line.SetPosition(i, points[i]);
            if (tracing) line.SetPosition(count - 1, ball.transform.position);

            var cam = Camera.main;
            if (!cam) return;
            float length = distances[distances.Count - 1] + (tracing ? Vector3.Distance(points[points.Count - 1], ball.transform.position) : 0f);
            var keys = new Keyframe[count];
            for (int i = 0; i < count; i++)
            {
                var p = i < points.Count ? points[i] : ball.transform.position;
                float t = length > 0f ? (i < points.Count ? distances[i] : length) / length : 0f;
                keys[i] = new Keyframe(t, Mathf.Max(minWidth, Vector3.Distance(cam.transform.position, p) * screenWidth));
            }
            widthCurve.keys = keys;
            line.widthCurve = widthCurve;
            line.widthMultiplier = 1f;
        }

        /// <summary>Slightly faded tail, full brightness at the head.</summary>
        static Gradient Fade()
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        /// <summary>Unlit, alpha-blended material for lines on the course (also used by AimLine).</summary>
        internal static Material DefaultMaterial(Color hdrColor)
        {
            // Alpha-blended (not additive: yellow added onto a bright sky turns white), slightly HDR so bloom glows.
            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetColor("_BaseColor", hdrColor);
            mat.SetFloat("_Surface", 1f);   // transparent
            mat.SetFloat("_Blend", 0f);     // alpha
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)RenderQueue.Transparent;
            return mat;
        }
    }
}
