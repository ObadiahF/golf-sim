using System;
using System.Collections.Generic;
using GolfSim.Course;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GolfSim.Ball
{
    /// <summary>
    /// The break preview for putting: rolls a copy of the putt (PuttPredictor) along the current aim at the speed that
    /// finishes PuttModel.Overshoot past the hole, and draws the predicted line as dots on the green, fading out after
    /// revealFraction in Partial mode so it's a read rather than the answer. Slope arrows around the line point
    /// downhill. Re-solved only when the ball, aim or assist level changes. The game sets `active` (putting mode);
    /// P cycles the assist level (Full / Partial / Off), which is remembered across sessions.
    /// </summary>
    [RequireComponent(typeof(GolfBall))]
    public class PuttPreview : MonoBehaviour
    {
        const string AssistKey = "GolfSim.PuttingAssist";

        [Tooltip("Set by the game while the current player is putting.")]
        public bool active;
        [Tooltip("Partial mode: fraction of the predicted line drawn before it fades out.")]
        [Range(0.1f, 1f)] public float revealFraction = 0.6f;
        [Tooltip("Partial mode: fraction of the line over which it fades to nothing.")]
        [Range(0f, 0.5f)] public float fadeFraction = 0.15f;
        public float dotSpacing = 0.17f;
        public float dotRadius = 0.028f;
        public Color dotColor = new Color(1f, 1f, 1f, 0.95f);
        public Color endColor = new Color(1f, 0.82f, 0.25f, 1f);
        [Tooltip("Green reading: arrows around the line pointing downhill.")]
        public bool showSlopeArrows = true;
        public float arrowSpacing = 0.8f;
        [Tooltip("Slope (percent) at which arrows reach full length and colour.")]
        public float steepSlope = 4f;
        [Tooltip("Simulation step for the preview (coarser is cheaper).")]
        public float step = PuttPredictor.CoarseStep;

        public static event Action AssistChanged;

        static PuttingAssist? assist;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            AssistChanged = null;
            assist = null;
        }

        /// <summary>The player's assist level (saved in PlayerPrefs).</summary>
        public static PuttingAssist Assist
        {
            get => assist ??= (PuttingAssist)Mathf.Clamp(PlayerPrefs.GetInt(AssistKey, (int)PuttingAssist.Partial), 0, 2);
            set
            {
                if (value == Assist) return;
                assist = value;
                PlayerPrefs.SetInt(AssistKey, (int)value);
                AssistChanged?.Invoke();
            }
        }

        public static void CycleAssist() => Assist = (PuttingAssist)(((int)Assist + 1) % 3);

        public static string AssistName(PuttingAssist a) => a.ToString().ToLowerInvariant();

        /// <summary>Ball speed (m/s) that finishes Overshoot past the hole along the current aim.</summary>
        public float SolvedSpeed { get; private set; }
        /// <summary>How far that putt would roll on a flat green: the power meter's target, metres.</summary>
        public float PlaysAs { get; private set; }
        public PuttPrediction Prediction { get; private set; }
        /// <summary>The predicted line from the ball to where it stops (or drops).</summary>
        public IReadOnlyList<Vector3> Path => path;
        public float Stimp => PuttModel.GreenStimp(ball.Settings);
        public TerrainSurfaceMap Map => Bind() ? map : null;

        GolfBall ball;
        HoleInfo hole;
        TerrainSurfaceMap map;
        GreenReading reading;
        readonly List<Vector3> path = new List<Vector3>();
        Vector3 solvedFrom, solvedAim;
        BallPhysicsSettings solvedSettings;
        PuttingAssist drawnAssist;
        bool solved;

        void Awake()
        {
            ball = GetComponent<GolfBall>();
            reading = new GreenReading(transform.parent);
            AssistChanged += Redraw;
        }

        void OnDestroy()
        {
            AssistChanged -= Redraw;
            reading?.Destroy();
        }

        void OnDisable() => reading.Visible = false;

        void Update()
        {
            var kb = Keyboard.current;
            if (active && kb != null && kb.pKey.wasPressedThisFrame) CycleAssist();
        }

        void LateUpdate()
        {
            bool show = active && Assist != PuttingAssist.Off && !ball.InMotion && ball.Status is BallStatus.Ready or BallStatus.Stopped;
            if (show) Refresh();
            reading.Visible = show && solved;
        }

        /// <summary>Re-solves and redraws if the ball, aim or physics changed since the last solve (cheap otherwise).</summary>
        public void Refresh()
        {
            if (!Bind()) return;
            var from = ball.transform.position;
            var aim = ball.AimDirection;
            var settings = ball.Settings; // a new live physics profile is a new object
            if (solved && from == solvedFrom && aim == solvedAim && settings == solvedSettings) return;
            solvedFrom = from;
            solvedAim = aim;
            solvedSettings = settings;
            SolvedSpeed = PuttPredictor.SolveSpeed(map, settings, from, aim, hole.PinWorld, step: step);
            PlaysAs = PuttModel.RollDistance(SolvedSpeed, Stimp);
            Prediction = PuttPredictor.Simulate(map, settings, from, aim, PuttModel.PuttAt(SolvedSpeed), hole.PinWorld, path, step: step);
            solved = true;
            Draw();
        }

        /// <summary>Pin height above the ball's spot, metres (+ uphill).</summary>
        public float ElevationToPin => Bind() ? map.HeightAt(hole.PinWorld) - map.HeightAt(ball.transform.position) : 0f;

        /// <summary>True when the green is within `reach` metres of the ball toward the pin (fringe putts).</summary>
        public bool GreenWithin(float reach)
        {
            if (!Bind()) return false;
            var from = ball.transform.position;
            var toPin = Vector3.ProjectOnPlane(hole.PinWorld - from, Vector3.up);
            float length = Mathf.Min(reach, toPin.magnitude);
            for (float d = 0f; d <= length; d += 0.25f)
            {
                var p = from + toPin.normalized * d;
                if (map.Contains(p) && map.SurfaceAt(p) == "green") return true;
            }
            return false;
        }

        void Redraw()
        {
            if (solved) Draw();
        }

        void Draw()
        {
            drawnAssist = Assist;
            reading.Begin();
            DrawLine();
            if (showSlopeArrows) DrawSlopeArrows();
            reading.End();
        }

        void DrawLine()
        {
            float total = 0f;
            for (int i = 1; i < path.Count; i++) total += Vector3.Distance(path[i - 1], path[i]);
            bool full = drawnAssist == PuttingAssist.Full;
            float fadeStart = revealFraction * total, fadeEnd = Mathf.Min(total, (revealFraction + fadeFraction) * total);
            float along = 0f, next = dotSpacing; // the first dot sits a little ahead of the ball
            for (int i = 1; i < path.Count; i++)
            {
                float seg = Vector3.Distance(path[i - 1], path[i]);
                while (next <= along + seg)
                {
                    float alpha = full ? 1f : 1f - Mathf.InverseLerp(fadeStart, Mathf.Max(fadeEnd, fadeStart + 1e-3f), next);
                    if (alpha <= 0.01f) return;
                    var p = Vector3.Lerp(path[i - 1], path[i], (next - along) / Mathf.Max(seg, 1e-5f));
                    reading.Dot(map, p, dotRadius, new Color(dotColor.r, dotColor.g, dotColor.b, dotColor.a * alpha));
                    next += dotSpacing;
                }
                along += seg;
            }
            if (full) reading.Dot(map, path[path.Count - 1], dotRadius * 2f, endColor);
        }

        void DrawSlopeArrows()
        {
            var from = ball.transform.position;
            var forward = Vector3.ProjectOnPlane(hole.PinWorld - from, Vector3.up);
            float length = forward.magnitude + 1f;
            forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : ball.AimDirection;
            var right = Vector3.Cross(Vector3.up, forward);
            float width = 1.2f;
            foreach (var p in path) width = Mathf.Max(width, Mathf.Abs(Vector3.Dot(p - from, right)) + 1.2f);
            for (float d = arrowSpacing; d <= length; d += arrowSpacing)
                for (float x = -Mathf.Floor(width / arrowSpacing) * arrowSpacing; x <= width; x += arrowSpacing)
                {
                    var p = from + forward * d + right * x;
                    if (!map.Contains(p) || map.SurfaceAt(p) != "green" || NearPath(p, arrowSpacing * 0.35f)) continue;
                    var (downhill, percent) = GreenReading.SlopeAt(map, p);
                    if (percent < 0.3f) continue;
                    float k = Mathf.Clamp01(percent / steepSlope);
                    var color = Color.Lerp(new Color(0.7f, 0.9f, 1f), new Color(1f, 0.55f, 0.25f), k);
                    color.a = Mathf.Lerp(0.25f, 0.75f, k);
                    reading.Arrow(map, p, downhill, Mathf.Lerp(0.18f, 0.42f, k), color);
                }
        }

        bool NearPath(Vector3 p, float distance)
        {
            foreach (var q in path)
                if (Vector3.ProjectOnPlane(q - p, Vector3.up).sqrMagnitude < distance * distance) return true;
            return false;
        }

        /// <summary>Finds the hole and its terrain; rebuilds the surface map when the hole is regenerated.</summary>
        bool Bind()
        {
            if (!hole) hole = FindAnyObjectByType<HoleInfo>();
            var terrain = hole ? hole.GetComponentInChildren<Terrain>() : null;
            if (!terrain) return false;
            if (map == null || map.Terrain != terrain)
            {
                map = new TerrainSurfaceMap(terrain, hole);
                solved = false;
            }
            return true;
        }
    }
}
