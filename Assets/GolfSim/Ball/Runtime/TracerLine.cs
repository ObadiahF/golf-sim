using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GolfSim.Ball
{
    /// <summary>
    /// The Toptracer-style glowing line, without a ball: points added along a path plus an optional live head, its
    /// width recomputed from the camera distance each frame so it keeps a steady on-screen thickness. Used by the
    /// live BallTracer and by the instant replay (drawn from a recorded shot).
    /// </summary>
    public class TracerLine
    {
        public float screenWidth = 0.012f;
        public float minWidth = 0.02f;
        public float spacing = 0.75f;

        readonly LineRenderer line;
        readonly List<Vector3> points = new List<Vector3>();
        readonly List<float> distances = new List<float>(); // cumulative length, for the width curve
        readonly AnimationCurve widthCurve = new AnimationCurve();
        bool hidden;

        public TracerLine(string name, Material material, Color hdrColor)
        {
            line = new GameObject(name).AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.numCapVertices = 4;
            line.numCornerVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.textureMode = LineTextureMode.Stretch;
            line.sharedMaterial = material ? material : BallTracer.DefaultMaterial(hdrColor);
            line.colorGradient = Fade(); // vertex colours are 8-bit, so the HDR tint lives on the material
            line.positionCount = 0;
        }

        public GameObject gameObject => line ? line.gameObject : null;
        public int Count => points.Count;
        public Vector3 Last => points[points.Count - 1];

        /// <summary>Hidden lines keep their points but draw nothing (e.g. the live trace during a replay).</summary>
        public bool Hidden
        {
            get => hidden;
            set
            {
                hidden = value;
                if (line) line.enabled = !value;
            }
        }

        public void Clear()
        {
            points.Clear();
            distances.Clear();
            if (line) line.positionCount = 0;
        }

        public void Add(Vector3 p)
        {
            distances.Add(points.Count == 0 ? 0f : distances[distances.Count - 1] + Vector3.Distance(points[points.Count - 1], p));
            points.Add(p);
        }

        /// <summary>Adds the point once it is `spacing` past the last one.</summary>
        public void Extend(Vector3 p)
        {
            if (points.Count == 0 || Vector3.Distance(points[points.Count - 1], p) >= spacing) Add(p);
        }

        /// <summary>Draws the points, plus the moving head when there is one (it follows the ball between samples).</summary>
        public void Draw(Vector3? head, Camera cam)
        {
            if (!line) return;
            int count = points.Count + (head.HasValue ? 1 : 0);
            if (count < 2) { line.positionCount = 0; return; }
            line.positionCount = count;
            for (int i = 0; i < points.Count; i++) line.SetPosition(i, points[i]);
            if (head is Vector3 h) line.SetPosition(count - 1, h);

            if (!cam) return;
            float length = distances[distances.Count - 1] + (head is Vector3 hd ? Vector3.Distance(points[points.Count - 1], hd) : 0f);
            var keys = new Keyframe[count];
            for (int i = 0; i < count; i++)
            {
                var p = i < points.Count ? points[i] : head.Value;
                float t = length > 0f ? (i < points.Count ? distances[i] : length) / length : 0f;
                keys[i] = new Keyframe(t, Mathf.Max(minWidth, Vector3.Distance(cam.transform.position, p) * screenWidth));
            }
            widthCurve.keys = keys;
            line.widthCurve = widthCurve;
            line.widthMultiplier = 1f;
        }

        public void Destroy()
        {
            if (line) Object.Destroy(line.gameObject);
        }

        /// <summary>Slightly faded tail, full brightness at the head.</summary>
        static Gradient Fade()
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }
}
