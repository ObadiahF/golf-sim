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

        const string Help =
            "Right mouse: look   WASD: move   Q/E: down/up\n" +
            "Shift: fast   Scroll: speed   1 Tee  2 Green  3 Overview   H: hide";

        HoleInfo hole;
        float yaw, pitch;

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
            if (!looking) return;

            Vector2 delta = mouse.delta.ReadValue() * lookSensitivity;
            yaw += delta.x;
            pitch = Mathf.Clamp(pitch - delta.y, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        void Move(Keyboard kb, Mouse mouse)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f)
                moveSpeed = Mathf.Clamp(moveSpeed * (scroll > 0 ? 1.2f : 1f / 1.2f), speedRange.x, speedRange.y);

            var input = new Vector3(
                Axis(kb.dKey, kb.aKey),
                Axis(kb.eKey, kb.qKey),
                Axis(kb.wKey, kb.sKey));
            float speed = moveSpeed * (kb.leftShiftKey.isPressed ? fastMultiplier : 1f) * Time.unscaledDeltaTime;

            Vector3 pos = transform.position
                + transform.TransformDirection(new Vector3(input.x, 0f, input.z)) * speed
                + Vector3.up * (input.y * speed);
            transform.position = KeepAboveGround(pos);
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

        public void JumpTo(HoleView view)
        {
            var pose = HoleViews.Get(hole, view);
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
