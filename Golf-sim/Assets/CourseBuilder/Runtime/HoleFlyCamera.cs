using UnityEngine;
using UnityEngine.InputSystem;

namespace GolfSim.Course
{
    /// <summary>
    /// Play-mode camera for exploring a hole. Right mouse = look, WASD = move, Q/E = down/up,
    /// Shift = fast, scroll = speed, 1/2/3 = tee/green/overview, H = toggle help.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class HoleFlyCamera : MonoBehaviour
    {
        public float moveSpeed = 20f;
        public float fastMultiplier = 4f;
        public float lookSensitivity = 0.15f;
        public Vector2 speedRange = new Vector2(2f, 200f);
        [Tooltip("Keeps the camera at least this high above the terrain.")]
        public float minGroundClearance = 1.5f;
        public bool showHelp = true;
        [Tooltip("While set, the camera turns to keep this in view (e.g. the ball in flight). Right-click looking cancels it.")]
        public Transform trackTarget;
        [Tooltip("How quickly tracking catches up with the target.")]
        public float trackSharpness = 6f;
        [Tooltip("While tracking, also follow the target, staying at this world-space offset from it (zero = only turn to look).")]
        public Vector3 followOffset;
        [Tooltip("How quickly following and gliding catch up.")]
        public float followSharpness = 2.5f;

        const string Help =
            "Right mouse: look   WASD: move   Q/E: down/up\n" +
            "Shift: fast   Scroll: speed   1 Tee  2 Green  3 Overview   H: hide";

        HoleInfo hole;
        float yaw, pitch;
        Pose? glide;

        void Start()
        {
            hole = FindAnyObjectByType<HoleInfo>();
            if (hole) JumpTo(HoleView.Tee); else SyncAngles();
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (hole)
            {
                if (kb.digit1Key.wasPressedThisFrame) JumpTo(HoleView.Tee);
                if (kb.digit2Key.wasPressedThisFrame) JumpTo(HoleView.Green);
                if (kb.digit3Key.wasPressedThisFrame) JumpTo(HoleView.Overview);
            }
            if (kb.hKey.wasPressedThisFrame) showHelp = !showHelp;

            Look(mouse);
            Move(kb, mouse);
        }

        void Look(Mouse mouse)
        {
            bool looking = mouse.rightButton.isPressed;
            Cursor.lockState = looking ? CursorLockMode.Locked : CursorLockMode.None;
            if (looking) StopFollowing(); // the user takes over
            else
            {
                Track();
                Glide();
                return;
            }

            Vector2 delta = mouse.delta.ReadValue() * lookSensitivity;
            yaw += delta.x;
            pitch = Mathf.Clamp(pitch - delta.y, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        void Track()
        {
            if (!trackTarget) return;
            var pos = transform.position;
            if (followOffset != Vector3.zero)
                pos = KeepAboveGround(Vector3.Lerp(pos, trackTarget.position + followOffset, Blend(followSharpness)));
            // Rise smoothly to look over whatever hides the ball (a bunker's lip, a dip in the fairway).
            pos.y = Mathf.Lerp(pos.y, SeeOverTerrain(pos, trackTarget.position).y, Blend(followSharpness * 2f));
            transform.position = pos;
            var wanted = Quaternion.LookRotation(trackTarget.position - transform.position);
            transform.rotation = Quaternion.Slerp(transform.rotation, wanted, Blend(trackSharpness));
            SyncAngles();
        }

        /// <summary>Smoothly flies the camera to this pose (cancelled by looking or moving).</summary>
        public void GlideTo(Pose pose)
        {
            trackTarget = null;
            glide = pose;
        }

        /// <summary>Stops tracking, following and gliding; the camera stays where it is.</summary>
        public void StopFollowing()
        {
            trackTarget = null;
            glide = null;
        }

        void Glide()
        {
            if (glide is not Pose target) return;
            float k = Blend(followSharpness);
            transform.SetPositionAndRotation(KeepAboveGround(Vector3.Lerp(transform.position, target.position, k)),
                                             Quaternion.Slerp(transform.rotation, target.rotation, k));
            SyncAngles();
            if ((transform.position - target.position).sqrMagnitude < 0.01f) glide = null;
        }

        static float Blend(float sharpness) => 1f - Mathf.Exp(-sharpness * Time.unscaledDeltaTime);

        void Move(Keyboard kb, Mouse mouse)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f)
                moveSpeed = Mathf.Clamp(moveSpeed * (scroll > 0 ? 1.2f : 1f / 1.2f), speedRange.x, speedRange.y);

            var input = new Vector3(
                Axis(kb.dKey, kb.aKey),
                Axis(kb.eKey, kb.qKey),
                Axis(kb.wKey, kb.sKey));
            if (input != Vector3.zero) StopFollowing(); // the user takes over
            float speed = moveSpeed * (kb.leftShiftKey.isPressed ? fastMultiplier : 1f) * Time.unscaledDeltaTime;

            Vector3 pos = transform.position
                + transform.TransformDirection(new Vector3(input.x, 0f, input.z)) * speed
                + Vector3.up * (input.y * speed);
            transform.position = KeepAboveGround(pos);
        }

        const float ViewNear = 0.8f;        // m off the subject where the sightline starts to count (the ground it sits on)
        const float ViewClearance = 0.12f;  // m the sightline keeps above the terrain
        const float MaxViewLift = 12f;      // m above the subject at most

        /// <summary>
        /// The eye raised (never lowered) until it sees the subject over the terrain: the sightline clears the ground
        /// from ViewNear off the subject back to the eye. A ball down in a bunker is otherwise hidden behind its lip.
        /// </summary>
        public static Vector3 SeeOverTerrain(Vector3 eye, Vector3 subject)
        {
            var terrain = Terrain.activeTerrain;
            if (!terrain) return eye;
            subject += Vector3.up * 0.05f; // the top half of a ball
            var back = new Vector3(eye.x - subject.x, 0f, eye.z - subject.z);
            float length = back.magnitude;
            if (length <= ViewNear) return eye;
            float need = eye.y;
            for (float d = ViewNear; d < length; d += 0.5f)
            {
                float t = d / length;
                var p = subject + back * t;
                float ground = terrain.SampleHeight(p) + terrain.transform.position.y + ViewClearance;
                // The sightline is at subject.y + (eye.y - subject.y) * t here: the eye height that puts it on the ground.
                need = Mathf.Max(need, subject.y + (ground - subject.y) / t);
            }
            eye.y = Mathf.Min(need, Mathf.Max(eye.y, subject.y + MaxViewLift));
            return eye;
        }

        Vector3 KeepAboveGround(Vector3 pos)
        {
            var terrain = Terrain.activeTerrain;
            if (!terrain) return pos;
            float ground = terrain.SampleHeight(pos) + terrain.transform.position.y;
            pos.y = Mathf.Max(pos.y, ground + minGroundClearance);
            return pos;
        }

        static float Axis(UnityEngine.InputSystem.Controls.KeyControl positive, UnityEngine.InputSystem.Controls.KeyControl negative) =>
            (positive.isPressed ? 1f : 0f) - (negative.isPressed ? 1f : 0f);

        public void JumpTo(HoleView view) => JumpTo(HoleViews.Get(hole, view));

        public void JumpTo(Pose pose)
        {
            glide = null;
            transform.SetPositionAndRotation(pose.position, pose.rotation);
            SyncAngles();
        }

        void SyncAngles()
        {
            Vector3 e = transform.eulerAngles;
            yaw = e.y;
            pitch = e.x > 180f ? e.x - 360f : e.x;
        }

        void OnDisable() => Cursor.lockState = CursorLockMode.None;

        void OnGUI()
        {
            if (!showHelp) return;
            var text = $"{Help}\nSpeed {moveSpeed:0} m/s";
            if (hole) text = $"Hole {hole.holeRef}  Par {hole.par}  {hole.TeeToPinMeters:0} m\n{text}";
            GUI.Box(new Rect(10, 10, 470, hole ? 62 : 46), text);
        }
    }
}
