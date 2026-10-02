using UnityEngine;

namespace GolfSim.Course
{
    public enum HoleView { Tee, Green, Overview }

    /// <summary>Preset camera poses for a hole, shared by the play-mode camera and the editor buttons.</summary>
    public static class HoleViews
    {
        public static Pose Get(HoleInfo hole, HoleView view, out Vector3 target)
        {
            Vector3 tee = hole.TeeWorld, pin = hole.PinWorld;
            Vector3 forward = pin - tee;
            forward.y = 0;
            float length = Mathf.Max(forward.magnitude, 1f);
            forward = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;

            Vector3 position;
            switch (view)
            {
                case HoleView.Tee:
                    position = tee - forward * 6f + Vector3.up * 2.5f;
                    target = Vector3.Lerp(tee, pin, 0.7f);
                    break;
                case HoleView.Green:
                    position = pin - forward * 22f + Vector3.up * 7f;
                    target = pin;
                    break;
                default:
                    target = Vector3.Lerp(tee, pin, 0.5f);
                    position = target - forward * length * 0.55f + Vector3.up * length * 0.5f;
                    break;
            }
            return new Pose(position, Quaternion.LookRotation(target - position));
        }

        public static Pose Get(HoleInfo hole, HoleView view) => Get(hole, view, out _);
    }
}
