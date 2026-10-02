using UnityEngine;
using UnityEngine.Rendering;

namespace GolfSim.Ball
{
    /// <summary>
    /// An arrow on the ground from the ball along GolfBall.AimDirection, shown while the ball is waiting to
    /// be hit. Its length is the planned carry (set by the game, e.g. the club's carry), capped near the pin.
    /// </summary>
    [RequireComponent(typeof(GolfBall))]
    public class AimLine : MonoBehaviour
    {
        const int Points = 64;
        const float Lift = 0.12f; // m above the ground so the line doesn't z-fight the grass

        [Tooltip("Planned carry in metres; the arrow stops a little past the pin.")]
        public float length = 60f;
        public float width = 0.35f;
        public Color color = new Color(1f, 1f, 1f, 0.85f);

        GolfBall ball;
        LineRenderer line;
        Vector3 drawnFrom, drawnDir;
        float drawnLength;

        void Awake()
        {
            ball = GetComponent<GolfBall>();
            var go = new GameObject("Aim Line") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform.parent, false);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // face up: the line lies flat on the ground
            line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.alignment = LineAlignment.TransformZ;
            line.positionCount = Points;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.material = BallTracer.DefaultMaterial(Color.white);
            // Constant shaft, then a wide arrow head over the last 12 %.
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.87f, 0.6f), new Keyframe(0.875f, 1.6f), new Keyframe(1f, 0f));
            line.widthMultiplier = width;
        }

        void OnDestroy()
        {
            if (line) Destroy(line.gameObject);
        }

        void OnDisable()
        {
            if (line) line.enabled = false;
        }

        void LateUpdate()
        {
            bool show = ball.Status == BallStatus.Ready || ball.Status == BallStatus.Stopped;
            line.enabled = show;
            if (!show) return;
            var from = ball.transform.position;
            var dir = ball.AimDirection;
            if (from == drawnFrom && dir == drawnDir && Mathf.Approximately(length, drawnLength)) return;
            drawnFrom = from;
            drawnDir = dir;
            drawnLength = length;
            Draw(from, dir);
        }

        void Draw(Vector3 from, Vector3 dir)
        {
            var terrain = Terrain.activeTerrain;
            for (int i = 0; i < Points; i++)
            {
                var p = from + dir * (Mathf.Max(1f, length) * i / (Points - 1));
                if (terrain) p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                line.SetPosition(i, p + Vector3.up * Lift);
            }
            line.startColor = new Color(color.r, color.g, color.b, color.a * 0.5f);
            line.endColor = color;
        }
    }
}
