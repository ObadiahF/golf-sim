using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// One camera shot of a replay, between two recording times: a camera that stands (or dollies slowly) somewhere
    /// and pans with the ball like a broadcast operator: damped, the ball held at a framing offset (e.g. a third of the
    /// frame, leading the motion), the lens zooming between two fields of view.
    /// </summary>
    public class ReplayShot
    {
        public string name;
        public float start, end;            // recording time
        public Vector3 from, to;            // camera position at the start and end (a slow dolly), or the same
        public float fovFrom = 25f, fovTo = 25f;
        /// <summary>How much the camera looks at the ball (1) rather than at `lookAt` (0).</summary>
        public float track = 1f;
        public Vector3 lookAt;
        /// <summary>Where the subject sits on screen: fractions of the frame from the centre (x right, y up).</summary>
        public Vector2 frame;
        /// <summary>Lead room: the ball sits this far (fraction of the frame width) behind the centre, against its motion.</summary>
        public float lead;
        /// <summary>How quickly the pan catches up with the ball (1/s); higher is tighter.</summary>
        public float sharpness = 4f;
        /// <summary>Operator wobble, degrees (long lenses on a tripod still breathe a little).</summary>
        public float wobble = 0.08f;
        /// <summary>Until this recording time the camera holds on the ball itself at `holdFrame` (the address and the
        /// strike), then pans to its own framing.</summary>
        public float holdUntil = float.NegativeInfinity;
        public Vector2 holdFrame = new Vector2(0f, -1f / 6f); // the lower third

        Quaternion rotation;
        float leadSide;
        bool started;

        public float Length => end - start;

        /// <summary>Puts the camera where this shot wants it at time t (the first call cuts to it).</summary>
        public void Apply(Camera cam, float t, Vector3 ball, Vector3 velocity, float realDelta)
        {
            float u = Smooth(Mathf.InverseLerp(start, end, t));
            var position = Vector3.Lerp(from, to, u);
            cam.fieldOfView = Mathf.Lerp(fovFrom, fovTo, u);
            bool holding = t < holdUntil;
            var subject = holding ? ball : Vector3.Lerp(lookAt, ball, track);

            // Which way the ball moves across the screen decides the side the lead room is on.
            var facing = started ? cam.transform.rotation : Quaternion.LookRotation(subject - position); // a cut: the new view's right
            float across = Vector3.Dot(velocity, facing * Vector3.right);
            if (!started) leadSide = across >= 0f ? 1f : -1f;
            else if (Mathf.Abs(across) > 2f) leadSide = Mathf.MoveTowards(leadSide, Mathf.Sign(across), realDelta * 1.5f);

            var wanted = holding ? Frame(position, subject, cam.fieldOfView, cam.aspect, holdFrame.x, holdFrame.y)
                                 : Frame(position, subject, cam.fieldOfView, cam.aspect, frame.x - lead * leadSide, frame.y);
            rotation = started ? Quaternion.Slerp(rotation, wanted, 1f - Mathf.Exp(-sharpness * realDelta)) : wanted;
            started = true;
            cam.transform.SetPositionAndRotation(position, rotation * Wobble(t));
        }

        /// <summary>Forgets the pan so the next Apply cuts to the shot again (a replay played twice).</summary>
        public void Reset() => started = false;

        /// <summary>The rotation that puts the subject at this screen offset (fractions of the frame from the centre).</summary>
        public static Quaternion Frame(Vector3 position, Vector3 subject, float fov, float aspect, float x, float y)
        {
            var dir = subject - position;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
            float hfov = Camera.VerticalToHorizontalFieldOfView(fov, aspect);
            return Quaternion.LookRotation(dir) * Quaternion.Euler(y * fov, -x * hfov, 0f);
        }

        Quaternion Wobble(float t)
        {
            if (wobble <= 0f) return Quaternion.identity;
            float time = t * 0.6f + start * 13.1f;
            return Quaternion.Euler((Mathf.PerlinNoise(time, 0.3f) - 0.5f) * 2f * wobble, (Mathf.PerlinNoise(0.7f, time) - 0.5f) * 2f * wobble, 0f);
        }

        static float Smooth(float u) => u * u * (3f - 2f * u);
    }
}
