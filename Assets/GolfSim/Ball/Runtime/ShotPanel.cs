using GolfSim.Course;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GolfSim.Ball
{
    /// <summary>
    /// On-screen shot controls for testing without a launch monitor: club presets, launch sliders,
    /// wind, Hit (Space) and Reset (R), plus a launch-monitor style results readout.
    /// A camera-based launch monitor will call GolfBall.Hit(ShotData) the same way.
    /// </summary>
    [RequireComponent(typeof(GolfBall))]
    public class ShotPanel : MonoBehaviour
    {
        const float Yards = 1.0936f;
        const float Width = 330f;

        public bool showPanel = true;
        [Tooltip("Put the camera behind the ball on each shot and track it in flight.")]
        public bool followBall = true;

        float mph = 167f, launch = 10.9f, direction, backspin = 2686f, sidespin, windMph, windFrom;
        GolfBall ball;
        HoleFlyCamera flyCam;
        Vector2 scroll;

        void Awake()
        {
            ball = GetComponent<GolfBall>();
            ball.ShotFinished += _ => { if (flyCam) flyCam.trackTarget = null; };
        }

        void Start() => flyCam = Camera.main ? Camera.main.GetComponent<HoleFlyCamera>() : null;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.spaceKey.wasPressedThisFrame) Hit();
            if (kb.rKey.wasPressedThisFrame) ResetBall();
            if (kb.tabKey.wasPressedThisFrame) showPanel = !showPanel;
        }

        public void Hit()
        {
            ball.windSpeed = windMph * ShotData.MetersPerSecondPerMph;
            ball.windHeading = windFrom + 180f; // sliders say where the wind comes from
            if (ball.Status is BallStatus.Holed or BallStatus.InWater or BallStatus.OutOfBounds) ball.ResetToTee();
            if (followBall && flyCam && !ball.InMotion)
            {
                flyCam.JumpTo(BehindBall());
                flyCam.trackTarget = ball.transform;
            }
            ball.Hit(ShotData.FromMph(mph, launch, direction, backspin, sidespin));
        }

        void ResetBall()
        {
            ball.ResetToTee();
            if (flyCam) flyCam.JumpTo(BehindBall());
        }

        Pose BehindBall()
        {
            var hole = FindAnyObjectByType<HoleInfo>();
            var aim = hole ? Vector3.ProjectOnPlane(hole.PinWorld - ball.transform.position, Vector3.up).normalized : Vector3.forward;
            // Behind and a little to the side, like a broadcast tracer camera, so the arc reads as a curve.
            var pos = ball.transform.position - aim * 6f + Vector3.Cross(Vector3.up, aim) * 2f + Vector3.up * 2f;
            var lookAt = ball.transform.position + aim * 40f + Vector3.up * 6f;
            return new Pose(pos, Quaternion.LookRotation(lookAt - pos));
        }

        void OnGUI()
        {
            if (!showPanel)
            {
                GUI.Label(new Rect(Screen.width - 160, 10, 150, 22), "Tab: shot panel");
                return;
            }
            var area = new Rect(Screen.width - Width - 10, 10, Width, Screen.height - 20);
            GUILayout.BeginArea(area, GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);

            GUILayout.Label("<b>Club presets</b> (tour averages)", Rich());
            int column = 0;
            GUILayout.BeginHorizontal();
            foreach (var (name, shot) in ShotData.Presets)
            {
                if (GUILayout.Button(name)) Apply(shot);
                if (++column % 4 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            mph = Slider("Ball speed", mph, 2f, 200f, $"{mph:0} mph");
            launch = Slider("Launch angle", launch, -5f, 60f, $"{launch:0.0}°");
            direction = Slider("Direction", direction, -15f, 15f, Side(direction, "°"));
            backspin = Slider("Backspin", backspin, 0f, 12000f, $"{backspin:0} rpm");
            sidespin = Slider("Sidespin", sidespin, -3000f, 3000f, sidespin == 0 ? "0 rpm" : $"{Mathf.Abs(sidespin):0} rpm {(sidespin > 0 ? "fade" : "draw")}");
            windMph = Slider("Wind", windMph, 0f, 30f, $"{windMph:0} mph");
            windFrom = Slider("Wind from", windFrom, 0f, 359f, Compass(windFrom));
            followBall = GUILayout.Toggle(followBall, " Camera follows ball");

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUI.enabled = !ball.InMotion;
            if (GUILayout.Button("HIT  (Space)", GUILayout.Height(34))) Hit();
            GUI.enabled = true;
            if (GUILayout.Button("Reset (R)", GUILayout.Height(34), GUILayout.Width(90))) ResetBall();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            DrawResults();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawResults()
        {
            var r = ball.Result;
            GUILayout.Label($"<b>{StatusText(ball.Status)}</b>", Rich());
            if (ball.Status == BallStatus.Ready) return;
            Row("Carry", $"{r.carry * Yards:0.0} yd");
            Row("Total", ball.InMotion ? "…" : $"{r.total * Yards:0.0} yd");
            Row("Apex", $"{r.apex * Yards:0.0} yd");
            Row("Offline", ball.InMotion ? "…" : Side(r.offline * Yards, " yd"));
            Row("Land angle", $"{r.landAngle:0.0}°");
            Row("Hang time", $"{r.flightTime:0.00} s");
            if (!ball.InMotion) Row("Lie", r.restingSurface);
            var hole = FindAnyObjectByType<HoleInfo>();
            if (hole && !ball.InMotion)
                Row("To pin", $"{Vector3.ProjectOnPlane(hole.PinWorld - ball.transform.position, Vector3.up).magnitude * Yards:0.0} yd");
        }

        void Apply(ShotData s)
        {
            mph = s.BallSpeedMph;
            launch = s.launchAngle;
            direction = s.launchDirection;
            backspin = s.backspin;
            sidespin = s.sidespin;
        }

        static float Slider(string label, float value, float min, float max, string readout)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(90));
            value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(130));
            GUILayout.Label(readout, GUILayout.Width(95));
            GUILayout.EndHorizontal();
            return value;
        }

        static void Row(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(90));
            GUILayout.Label(value);
            GUILayout.EndHorizontal();
        }

        static string Side(float v, string unit) =>
            Mathf.Abs(v) < 0.05f ? $"0{unit}" : $"{Mathf.Abs(v):0.0}{unit} {(v > 0 ? "R" : "L")}";

        static string Compass(float degrees) =>
            new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" }[Mathf.RoundToInt(degrees / 45f) % 8] + $" ({degrees:0}°)";

        static string StatusText(BallStatus s) => s switch
        {
            BallStatus.Ready => "Ready: pick a club or set the sliders, then hit",
            BallStatus.Flying => "In the air…",
            BallStatus.Rolling => "Rolling…",
            BallStatus.Holed => "IN THE HOLE!",
            BallStatus.InWater => "In the water",
            BallStatus.OutOfBounds => "Out of bounds",
            _ => "Shot result",
        };

        static GUIStyle rich;
        static GUIStyle Rich() => rich ??= new GUIStyle(GUI.skin.label) { richText = true };
    }
}
