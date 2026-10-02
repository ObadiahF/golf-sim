using System.Collections.Generic;
using GolfSim.Ball;
using GolfSim.Course;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// What the replay takes over and gives back: the main camera's exact pose and lens with its fly-camera follow
    /// state (the component is paused, so its target, offset and glide resume untouched), and the game's own visuals
    /// (the real ball, its tracer and aim line, the shot panel), hidden while the replay is on. The round HUD hides
    /// itself on the "replay" screen (RoundHud.Render), so it comes back with the next state whatever ends the replay.
    /// </summary>
    public struct ScreenState
    {
        public Camera camera;
        public GolfBall ball;
        Vector3 position;
        Quaternion rotation;
        float fov, near;
        Behaviour[] paused;
        bool[] wasEnabled;
        List<Renderer> renderers;
        BallTracer tracer;

        public static ScreenState Hide(Camera cam, GolfBall ball)
        {
            var s = new ScreenState
            {
                camera = cam, ball = ball, position = cam.transform.position, rotation = cam.transform.rotation,
                fov = cam.fieldOfView, near = cam.nearClipPlane, tracer = ball.GetComponent<BallTracer>(),
                renderers = new List<Renderer>(),
            };
            s.paused = new Behaviour[] { cam.GetComponent<HoleFlyCamera>(), ball.GetComponent<ShotPanel>(), ball.GetComponent<AimLine>() };
            s.wasEnabled = new bool[s.paused.Length];
            for (int i = 0; i < s.paused.Length; i++)
            {
                if (!s.paused[i]) continue;
                s.wasEnabled[i] = s.paused[i].enabled;
                s.paused[i].enabled = false;
            }
            foreach (var r in ball.GetComponentsInChildren<Renderer>())
                if (r.enabled) { r.enabled = false; s.renderers.Add(r); }
            if (s.tracer) s.tracer.Hidden = true;
            cam.nearClipPlane = Mathf.Min(s.near, 0.1f); // the cup camera sits close to the ground
            return s;
        }

        public void Restore()
        {
            if (camera)
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
                camera.nearClipPlane = near;
            }
            for (int i = 0; paused != null && i < paused.Length; i++)
                if (paused[i]) paused[i].enabled = wasEnabled[i];
            if (renderers != null)
                foreach (var r in renderers)
                    if (r) r.enabled = true;
            if (tracer) tracer.Hidden = false;
        }
    }
}
