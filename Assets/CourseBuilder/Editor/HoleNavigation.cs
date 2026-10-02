using GolfSim.Course;
using UnityEditor;
using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>Editor-side camera helpers: Scene View presets and the play-mode fly camera.</summary>
    public static class HoleNavigation
    {
        public static HoleInfo FindHole()
        {
            var selected = Selection.activeGameObject ? Selection.activeGameObject.GetComponentInParent<HoleInfo>() : null;
            return selected ? selected : Object.FindAnyObjectByType<HoleInfo>();
        }

        public static void ShowInSceneView(HoleInfo hole, HoleView view)
        {
            var sv = SceneView.lastActiveSceneView;
            if (!hole || !sv) return;

            var pose = HoleViews.Get(hole, view, out var target);
            // SceneView orbits a pivot; "size" maps to camera distance via the field of view.
            float distance = Vector3.Distance(pose.position, target);
            float size = distance * Mathf.Tan(sv.camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            sv.LookAt(target, pose.rotation, size, false, false);
        }

        /// <summary>Gives the scene's main camera a fly camera and parks it at the tee, so Play shows the hole.</summary>
        public static void SetUpPlayCamera(HoleInfo hole)
        {
            var cam = Camera.main;
            if (!cam) return;

            var fly = cam.GetComponent<HoleFlyCamera>();
            if (!fly) fly = Undo.AddComponent<HoleFlyCamera>(cam.gameObject);

            var pose = HoleViews.Get(hole, HoleView.Tee);
            Undo.RecordObject(cam.transform, "Move camera to tee");
            cam.transform.SetPositionAndRotation(pose.position, pose.rotation);
        }
    }
}
