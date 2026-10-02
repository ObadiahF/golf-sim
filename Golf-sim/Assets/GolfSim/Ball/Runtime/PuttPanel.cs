using GolfSim.Course;
using UnityEngine;

namespace GolfSim.Ball
{
    /// <summary>
    /// Keyboard putting on the on-screen ShotPanel: with the putter preset loaded it adds a distance slider in metres
    /// that sets the ball speed for that roll on a flat green of this Stimp (PuttModel), a button that loads the
    /// preview's "plays as" distance, and the assist level (P).
    /// </summary>
    [RequireComponent(typeof(ShotPanel), typeof(PuttPreview))]
    public class PuttPanel : MonoBehaviour
    {
        const float MinDistance = 0.3f, MaxDistance = 30f;

        ShotPanel panel;
        PuttPreview preview;
        GolfBall ball;
        Vector3 setFor = Vector3.positiveInfinity;
        float distance = 3f;

        void Awake()
        {
            panel = GetComponent<ShotPanel>();
            preview = GetComponent<PuttPreview>();
            ball = GetComponent<GolfBall>();
            panel.DrawExtras += Draw;
        }

        void OnDestroy()
        {
            if (panel) panel.DrawExtras -= Draw;
        }

        bool Putter => panel.SelectedClub == Clubs.Putter;

        /// <summary>
        /// With the putter loaded the slider owns the ball speed (Space hits with it even while the panel is hidden).
        /// A new spot or a fresh putter pick starts at the flat distance to the pin plus the overshoot.
        /// </summary>
        void Update()
        {
            if (!Putter)
            {
                setFor = Vector3.positiveInfinity;
                return;
            }
            if (transform.position != setFor && !ball.InMotion)
            {
                setFor = transform.position;
                distance = FlatDistanceToPin() + PuttModel.Overshoot;
            }
            SetDistance(distance);
        }

        void Draw()
        {
            if (!Putter) return;

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Putt", GUILayout.Width(90));
            float picked = GUILayout.HorizontalSlider(distance, MinDistance, MaxDistance, GUILayout.Width(130));
            GUILayout.Label($"{distance:0.0} m roll", GUILayout.Width(95));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(picked, distance)) SetDistance(picked);

            GUILayout.BeginHorizontal();
            if (preview.active && GUILayout.Button($"Use the read ({preview.PlaysAs:0.0} m)")) SetDistance(preview.PlaysAs);
            if (GUILayout.Button($"Assist: {PuttPreview.Assist} (P)")) PuttPreview.CycleAssist();
            GUILayout.EndHorizontal();
        }

        /// <summary>Sets the slider and the panel's ball speed to roll this far on a flat green.</summary>
        void SetDistance(float metres)
        {
            distance = Mathf.Clamp(Mathf.Round(metres * 10f) / 10f, MinDistance, MaxDistance);
            panel.BallSpeed = PuttModel.SpeedFor(distance, preview.Stimp);
        }

        float FlatDistanceToPin()
        {
            var hole = FindAnyObjectByType<HoleInfo>();
            return hole ? Vector3.ProjectOnPlane(hole.PinWorld - transform.position, Vector3.up).magnitude : distance;
        }
    }
}
