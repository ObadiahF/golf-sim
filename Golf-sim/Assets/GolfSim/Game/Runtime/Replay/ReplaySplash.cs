using GolfSim.Ball;
using UnityEngine;
using UnityEngine.Rendering;

namespace GolfSim.Game
{
    /// <summary>
    /// Where a replayed ball goes into the water: rings rippling out from the splash point, so the replay holds on
    /// something once the ball has sunk. Driven by recording time like the ghost ball.
    /// </summary>
    public class ReplaySplash
    {
        const int Rings = 3, Segments = 48;
        const float Stagger = 0.3f;   // s between rings
        const float Life = 2.4f;      // s a ring lasts
        const float Spread = 2.4f;    // m a ring has grown by the end of its life
        const float ScreenWidth = 0.004f;

        readonly LineRenderer[] rings = new LineRenderer[Rings];

        public ReplaySplash(Transform parent)
        {
            var material = BallTracer.DefaultMaterial(new Color(1.6f, 1.7f, 1.8f));
            for (int i = 0; i < Rings; i++)
            {
                var line = new GameObject($"Replay Ripple {i}").AddComponent<LineRenderer>();
                line.transform.SetParent(parent, false);
                line.useWorldSpace = true;
                line.loop = true;
                line.alignment = LineAlignment.View;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.sharedMaterial = material;
                line.positionCount = Segments;
                line.enabled = false;
                rings[i] = line;
            }
        }

        /// <summary>Draws the ripples `age` recording seconds after the splash at `point` (negative age: none).</summary>
        public void Draw(Vector3 point, float age, Camera cam)
        {
            for (int i = 0; i < Rings; i++)
            {
                var line = rings[i];
                float u = (age - i * Stagger) / Life;
                line.enabled = u >= 0f && u < 1f;
                if (!line.enabled) continue;
                float radius = 0.12f + Spread * Mathf.Sqrt(u);
                for (int k = 0; k < Segments; k++)
                {
                    float a = k * Mathf.PI * 2f / Segments;
                    line.SetPosition(k, point + new Vector3(Mathf.Cos(a) * radius, 0.02f, Mathf.Sin(a) * radius));
                }
                float alpha = 0.9f * (1f - u);
                line.startColor = line.endColor = new Color(1f, 1f, 1f, alpha);
                line.widthMultiplier = cam ? Mathf.Max(0.03f, Vector3.Distance(cam.transform.position, point) * ScreenWidth) : 0.05f;
            }
        }

        public void Hide()
        {
            foreach (var line in rings)
                if (line) line.enabled = false;
        }

        public void Destroy()
        {
            foreach (var line in rings)
                if (line) Object.Destroy(line.gameObject);
        }
    }
}
