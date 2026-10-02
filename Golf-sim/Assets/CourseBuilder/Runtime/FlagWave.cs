using UnityEngine;

namespace GolfSim.Course
{
    /// <summary>
    /// Ripples a cloth flag in the wind during Play mode. The pose is pure math (no physics),
    /// so the editor bakes a still frame of the same shape when it builds the pin.
    /// Local space: the flag hangs from the stick at x = 0 and flies along +x; its top edge is y = 0.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    public class FlagWave : MonoBehaviour
    {
        public const float Width = 0.5f;   // 20 in
        public const float Height = 0.35f; // 14 in
        const int Cols = 14, Rows = 8;

        [Tooltip("Direction the wind blows toward, in degrees clockwise from north (+z).")]
        public float windHeading = 45f;
        [Range(0, 1)] public float windStrength = 0.6f;

        Mesh mesh;
        Vector3[] vertices;

        void Start()
        {
            mesh = BuildMesh(0f, windStrength);
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh; // runtime copy; never touch the saved mesh asset
            vertices = mesh.vertices;
        }

        void Update()
        {
            ApplyHeading();
            Pose(vertices, Time.time, windStrength);
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
        }

        void OnDestroy()
        {
            if (mesh) Destroy(mesh);
        }

        /// <summary>Turns the flag so it flies downwind.</summary>
        public void ApplyHeading()
        {
            var downwind = Quaternion.Euler(0f, windHeading, 0f) * Vector3.forward;
            transform.rotation = Quaternion.LookRotation(downwind) * Quaternion.Euler(0f, -90f, 0f); // +x -> downwind
        }

        public static Mesh BuildMesh(float time, float strength)
        {
            var verts = new Vector3[(Cols + 1) * (Rows + 1)];
            var uvs = new Vector2[verts.Length];
            var tris = new int[Cols * Rows * 6];
            for (int r = 0, i = 0; r <= Rows; r++)
                for (int c = 0; c <= Cols; c++, i++)
                    uvs[i] = new Vector2((float)c / Cols, 1f - (float)r / Rows);
            for (int r = 0, t = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    int a = r * (Cols + 1) + c, b = a + 1, d = a + Cols + 1, e = d + 1;
                    tris[t++] = a; tris[t++] = b; tris[t++] = d;
                    tris[t++] = b; tris[t++] = e; tris[t++] = d;
                }
            Pose(verts, time, strength);
            var mesh = new Mesh { name = "Flag", vertices = verts, uv = uvs, triangles = tris };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Travelling wave that grows toward the free edge; a weak wind lets the flag droop.</summary>
        public static void Pose(Vector3[] verts, float time, float strength)
        {
            float amplitude = 0.02f + 0.06f * strength;
            float speed = 1.5f + 3f * strength;
            float droop = (1f - strength) * 0.12f;
            for (int r = 0, i = 0; r <= Rows; r++)
                for (int c = 0; c <= Cols; c++, i++)
                {
                    float u = (float)c / Cols, v = (float)r / Rows;
                    float wave = Mathf.Sin(2f * Mathf.PI * (u * 1.6f - time * speed * 0.5f) + v * 0.6f);
                    verts[i] = new Vector3(u * Width, -v * Height - droop * u * u, amplitude * Mathf.Pow(u, 1.3f) * wave);
                }
        }
    }
}
