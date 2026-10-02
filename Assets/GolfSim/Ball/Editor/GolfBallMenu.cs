using GolfSim.Course;
using GolfSim.CourseEditor;
using GolfSim.Course;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GolfSim.Ball.Editor
{
    /// <summary>Golf > Add Golf Ball: a regulation ball with the shot tracer and on-screen shot panel.</summary>
    public static class GolfBallMenu
    {
        const string SettingsPath = "Assets/GolfSim/Ball/Settings/BallPhysics.asset";

        [MenuItem("Golf/Add Golf Ball")]
        public static void AddGolfBall()
        {
            var existing = Object.FindAnyObjectByType<GolfBall>();
            if (existing)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[GolfBall] The scene already has a golf ball; selected it.");
                return;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Golf Ball";
            Object.DestroyImmediate(go.GetComponent<Collider>()); // flight and roll are simulated by GolfBall
            go.transform.localScale = Vector3.one * BallPhysicsSettings.Radius * 2f;
            var mat = GeneratedAssets.ColorMaterial("GolfBall", Color.white);
            mat.SetFloat("_Smoothness", 0.7f);
            go.GetComponent<Renderer>().sharedMaterial = mat;

            var ball = go.AddComponent<GolfBall>();
            ball.settings = GeneratedAssets.LoadOrCreate(SettingsPath, ScriptableObject.CreateInstance<BallPhysicsSettings>);
            go.AddComponent<BallTracer>();
            go.AddComponent<ShotPanel>();

            Undo.RegisterCreatedObjectUndo(go, "Add Golf Ball");
            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;
        }
    }
}
