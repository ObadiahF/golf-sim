using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GolfSim.Ball
{
    /// <summary>How much putting help the sim draws: the whole break line, the first part of it, or nothing.</summary>
    public enum PuttingAssist { Full, Partial, Off }

    /// <summary>
    /// The drawing side of the putt preview: a world-space mesh of flat dots (the predicted line) and slope arrows
    /// (pointing downhill, stronger where it's steeper) lying on the green. One alpha-blended, vertex-coloured mesh,
    /// so the whole read is a single draw call.
    /// </summary>
    public class GreenReading
    {
        const int DotSides = 10;
        const float Lift = 0.03f; // m above the turf so it doesn't z-fight

        readonly Mesh mesh;
        readonly MeshRenderer renderer;
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> triangles = new List<int>();

        public GreenReading(Transform parent)
        {
            var go = new GameObject("Putt Preview") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            mesh = new Mesh { name = "Putt Preview" };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = BallTracer.DefaultMaterial(Color.white);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
        }

        public bool Visible
        {
            get => renderer && renderer.enabled;
            set { if (renderer) renderer.enabled = value; }
        }

        public void Destroy()
        {
            if (renderer) Object.Destroy(renderer.gameObject);
            Object.Destroy(mesh);
        }

        public void Begin()
        {
            vertices.Clear();
            colors.Clear();
            triangles.Clear();
        }

        public void End()
        {
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>A flat disc lying on the ground at this point.</summary>
        public void Dot(TerrainSurfaceMap map, Vector3 at, float radius, Color color)
        {
            var normal = map.NormalAt(at);
            var u = Vector3.Cross(normal, Vector3.forward).normalized;
            var v = Vector3.Cross(normal, u);
            var center = Ground(map, at);
            int first = vertices.Count;
            Add(center, color);
            for (int i = 0; i < DotSides; i++)
            {
                float a = i * Mathf.PI * 2f / DotSides;
                Add(center + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius, color);
                triangles.Add(first); // clockwise seen from above: front-facing
                triangles.Add(first + 1 + i);
                triangles.Add(first + 1 + (i + 1) % DotSides);
            }
        }

        /// <summary>A flat chevron arrow centred here, pointing along dir (flat), each corner on the ground.</summary>
        public void Arrow(TerrainSurfaceMap map, Vector3 at, Vector3 dir, float length, Color color)
        {
            var side = Vector3.Cross(Vector3.up, dir) * (length * 0.35f);
            var tip = at + dir * (length * 0.5f);
            var tail = at - dir * (length * 0.5f);
            var notch = at - dir * (length * 0.15f);
            int first = vertices.Count;
            Add(Ground(map, tip), color);
            Add(Ground(map, tail + side), color);
            Add(Ground(map, notch), color);
            Add(Ground(map, tail - side), color);
            triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
        }

        void Add(Vector3 p, Color c)
        {
            vertices.Add(p);
            colors.Add(c);
        }

        static Vector3 Ground(TerrainSurfaceMap map, Vector3 p)
        {
            p.y = map.HeightAt(p) + Lift;
            return p;
        }

        /// <summary>Downhill direction (flat, unit) and slope in percent (rise over run) at this point.</summary>
        public static (Vector3 downhill, float percent) SlopeAt(TerrainSurfaceMap map, Vector3 at)
        {
            var n = map.NormalAt(at);
            var flat = new Vector3(n.x, 0f, n.z);
            float run = Mathf.Max(n.y, 1e-3f);
            return (flat.sqrMagnitude > 1e-8f ? flat.normalized : Vector3.zero, flat.magnitude / run * 100f);
        }
    }
}
