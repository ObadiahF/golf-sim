using GolfSim.Ball;
using GolfSim.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    // Putting mode: with the putter on the green (or just off it) the camera drops low behind the ball looking at the
    // cup, the break preview (PuttPreview) draws on the green, the HUD shows the putt card, and "state" carries the
    // putting fields the phone's power meter needs. After a putt the HUD shows how far it went against the target.
    public partial class RoundDirector
    {
        const float FringeReach = 3f;      // m: the putter from this close to the green is putting mode
        const float PuttCameraBack = 2.6f; // m behind the ball
        const float PuttCameraUp = 1.5f;   // m above the ball (the fly camera's minimum ground clearance)

        PuttPreview preview;
        PuttingHud puttingHud;
        bool wasPutting;
        PuttingHud.Stroke stroke; // the putt in progress, for the result bar

        void BindPutting()
        {
            preview = ball.GetComponent<PuttPreview>();
            if (!preview) preview = ball.gameObject.AddComponent<PuttPreview>();
            if (panel)
            {
                if (!ball.GetComponent<PuttPanel>()) ball.gameObject.AddComponent<PuttPanel>();
                panel.ClubPicked += OnPanelClub;
                panel.lineUpPose = PuttCameraPose;
            }
            if (puttingHud == null && hud != null) puttingHud = new PuttingHud(GetComponentInChildren<UIDocument>().rootVisualElement);
            ball.ShotStarted += OnPuttStarted;
            ball.ShotFinished += OnPuttFinished;
            PuttPreview.AssistChanged += OnAssistChanged;
            TurnStarted += OnPuttingTurn;
            wasPutting = false;
            stroke = null;
        }

        void UnbindPutting()
        {
            PuttPreview.AssistChanged -= OnAssistChanged;
            TurnStarted -= OnPuttingTurn;
            if (panel)
            {
                panel.ClubPicked -= OnPanelClub;
                panel.lineUpPose = null;
            }
            if (ball)
            {
                ball.ShotStarted -= OnPuttStarted;
                ball.ShotFinished -= OnPuttFinished;
            }
            preview = null;
            puttingHud?.Hide();
        }

        bool IsPutting(string lie) =>
            preview && Clubs.Find(club).IsPutter && (lie == "green" || (lie != "tee" && preview.GreenWithin(FringeReach)));

        /// <summary>The putting fields of "state" (called by BuildState).</summary>
        void FillPutting(StateMessage s)
        {
            s.puttingAssist = PuttPreview.AssistName(PuttPreview.Assist);
            s.putting = IsPutting(s.lie);
            if (!s.putting) return;
            preview.Refresh();
            s.puttDistance = Centimetres(Round.FlatDistance(ball.transform.position, hole.PinWorld));
            s.elevation = Centimetres(preview.ElevationToPin);
            s.stimp = System.Math.Round(preview.Stimp, 1);
            s.puttPlaysAs = Centimetres(preview.PlaysAs);
        }

        /// <summary>Applies putting mode to the scene (called by PublishState): preview, camera and HUD card.</summary>
        void ShowPutting(StateMessage s)
        {
            if (!preview) return;
            bool playing = phase == Phase.Playing && s.screen == StateMessage.Game;
            bool changed = s.putting != wasPutting;
            wasPutting = s.putting; // before LineUp: PuttCameraPose reads it
            preview.active = s.putting && playing;
            if (aimLine) aimLine.enabled = !preview.active || PuttPreview.Assist == PuttingAssist.Off; // the dots start along the aim
            if (changed && playing && panel && !ball.InMotion) panel.LineUp();
            puttingHud?.Render(s.putting && (s.screen is StateMessage.Game or StateMessage.Paused) ? s : null, stroke);
        }

        /// <summary>Low behind the ball, looking at the cup (only in putting mode; otherwise the panel's own line-up).</summary>
        Pose? PuttCameraPose()
        {
            if (!wasPutting || !ball || !hole) return null;
            var at = ball.transform.position;
            var toPin = Vector3.ProjectOnPlane(hole.PinWorld - at, Vector3.up);
            var back = toPin.sqrMagnitude > 1e-4f ? toPin.normalized : ball.AimDirection;
            var position = at - back * PuttCameraBack + Vector3.up * PuttCameraUp;
            return new Pose(position, Quaternion.LookRotation(hole.PinWorld - position));
        }

        void OnPanelClub(string name) => SetClub(name);

        void OnAssistChanged()
        {
            if (ball) PublishState();
        }

        /// <summary>A new player is up: the last putt's result bar was someone else's.</summary>
        void OnPuttingTurn(TurnMessage turn)
        {
            stroke = null;
            puttingHud?.ShowStroke(null);
        }

        void OnPuttStarted(GolfBall b)
        {
            stroke = null;
            puttingHud?.ShowStroke(null);
            if (!wasPutting) return;
            float stimp = preview.Stimp;
            stroke = new PuttingHud.Stroke
            {
                target = Round.FlatDistance(b.LaunchPoint, hole.PinWorld),
                hitFor = PuttModel.RollDistance(b.LastShot.ballSpeed, stimp),
                playsAs = preview.PlaysAs,
            };
        }

        void OnPuttFinished(GolfBall b)
        {
            if (stroke == null) return;
            stroke.rolled = b.Result.total;
            stroke.holed = b.Status == BallStatus.Holed;
            stroke.done = true;
            puttingHud?.ShowStroke(stroke);
            // Replaces OnShotFinished's "0 yd carry" toast (holing out, penalties and picking up keep theirs).
            if (b.Status == BallStatus.Stopped && round?.CurrentBall?.pickedUp != true) hud?.Toast(round?.CurrentBall is { } p ? $"{p.player}: {stroke.Summary.ToLowerInvariant()}" : stroke.Summary);
        }

        static double Centimetres(float metres) => System.Math.Round(metres, 2); // double: JSON shows 5.03, not 5.0300002
    }
}
