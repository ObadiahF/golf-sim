using System.Collections;
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
        const float Yards = ShotData.YardsPerMeter;
        const float Width = 330f;

        public bool showPanel = true;
        [Tooltip("Put the camera behind the ball on each shot, chase it in flight, then line up behind it for the next shot.")]
        public bool followBall = true;
        [Tooltip("Seconds to watch the ball at rest before the camera lines up the next shot.")]
        public float lineUpDelay = 1.5f;

        float mph = 167f, launch = 10.9f, direction, backspin = 2686f, sidespin, windMph, windFrom;
        GolfBall ball;
        HoleFlyCamera flyCam;
        Vector2 scroll;

        void Awake()
        {
            ball = GetComponent<GolfBall>();
            ball.ShotFinished += _ => { if (flyCam && followBall) StartCoroutine(LineUpNextShot()); };
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

        public void Hit() => Hit(SliderShot);

        ShotData SliderShot => ShotData.FromMph(mph, launch, direction, backspin, sidespin);

        /// <summary>Hits with these launch conditions (e.g. from a remote launch monitor) using the panel's wind and follow camera.</summary>
        public void Hit(ShotData shot)
        {
            // Blocked: e.g. a round between turns. Moving: ignored entirely, so the shot in the air keeps its wind.
            if (Shots.Blocked != null || ball.InMotion) return;
            ball.windSpeed = windMph * ShotData.MetersPerSecondPerMph;
            ball.windHeading = windFrom + 180f; // sliders say where the wind comes from
            if (ball.Status is BallStatus.Holed or BallStatus.InWater or BallStatus.OutOfBounds) ball.ResetToTee();
            if (followBall && flyCam)
            {
                flyCam.JumpTo(BehindBall());
                flyCam.trackTarget = ball.transform;
                flyCam.followOffset = Offset(Aim(), back: 14f, side: 3f, up: 5f);
            }
            ball.Hit(shot);
        }

        void ResetBall()
        {
            ball.ResetToTee();
            LineUp();
        }

        /// <summary>Puts the camera behind the ball, facing along the aim, ready for the next shot (and stops chasing the last one).</summary>
        public void LineUp()
        {
            StopAllCoroutines();
            if (!flyCam) Start();
            if (!flyCam) return;
            flyCam.StopFollowing();
            flyCam.followOffset = Vector3.zero;
            flyCam.JumpTo(BehindBall());
        }

        /// <summary>Loads a club's typical shot into the sliders (see Clubs.Bag).</summary>
        public void SelectClub(string clubName) => Apply(Clubs.Find(clubName).shot);

        /// <summary>Watches the ball settle, then glides behind it facing the pin, ready for the next shot.</summary>
        IEnumerator LineUpNextShot()
        {
            flyCam.followOffset = Vector3.zero; // keep looking at the ball, stop chasing
            yield return new WaitForSecondsRealtime(lineUpDelay);
            if (ball.Status == BallStatus.Stopped && flyCam.trackTarget == ball.transform) flyCam.GlideTo(BehindBall());
        }

        Vector3 Aim() => ball.AimDirection;

        /// <summary>Behind the ball and a little to the side (so the tracer arc reads as a curve), relative to the aim.</summary>
        static Vector3 Offset(Vector3 aim, float back, float side, float up) =>
            -aim * back + Vector3.Cross(Vector3.up, aim) * side + Vector3.up * up;

        Pose BehindBall()
        {
            var aim = Aim();
            var pos = ball.transform.position + Offset(aim, back: 6f, side: 2f, up: 2f);
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
            foreach (var club in Clubs.Bag)
            {
                if (GUILayout.Button(club.name)) Apply(club.shot);
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
            if (!ball.InMotion && ball.Status is BallStatus.Ready or BallStatus.Stopped) Row("Lie", ball.LieEffectFor(SliderShot).Label);
            if (ball.Status == BallStatus.Ready) return;
            Row("Hit from", r.LieLabel);
            Row("Carry", $"{r.carry * Yards:0.0} yd");
            Row("Total", ball.InMotion ? "…" : $"{r.total * Yards:0.0} yd");
            Row("Apex", $"{r.apex * Yards:0.0} yd");
            Row("Offline", ball.InMotion ? "…" : Side(r.offline * Yards, " yd"));
            Row("Land angle", ball.InMotion || r.landed ? $"{r.landAngle:0.0}°" : "n/a");
            Row("Hang time", $"{r.flightTime:0.00} s");
            if (r.hitTree || r.hitRock) Row("Hit", r.hitTree && r.hitRock ? "Tree, rock" : r.hitTree ? "Tree" : "Rock");
            if (!ball.InMotion) Row("Finished", r.restingSurface);
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
