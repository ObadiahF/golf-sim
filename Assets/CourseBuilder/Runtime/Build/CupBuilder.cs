using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GolfSim.Course
{
    /// <summary>
    /// Builds a regulation cup without cutting the terrain: an invisible disc marks the opening in the
    /// stencil buffer (GolfSim/CupMask) and the interior is drawn through it (GolfSim/CupInterior).
    /// The rim follows the green's slope so the opening sits flush on sloped greens.
    /// </summary>
    public static class CupBuilder
    {
        public const float Radius = 0.054f; // 4.25 in hole
        public const float Depth = 0.127f;  // liner rim 1 in below the surface + 4 in cup
        const float SoilDepth = 0.025f;     // root zone visible above the liner
        const float MaskLift = 0.003f;      // keeps the mask just above the terrain surface
        const int Segments = 32;

        static readonly Color Soil = new Color(0.20f, 0.15f, 0.10f);
        static readonly Color Liner = new Color(0.90f, 0.90f, 0.88f);
        static readonly Color Floor = new Color(0.30f, 0.28f, 0.26f);

        public static void Create(Transform pin, Terrain terrain, HoleAssets assets)
        {
            var rim = RimHeights(pin, terrain);
            Child(pin, "Cup Mask", assets.PerHole("CupMask.asset", MaskMesh(rim)), assets.ShaderMaterial("CupMask", "GolfSim/CupMask"));
            Child(pin, "Cup", assets.PerHole("CupInterior.asset", InteriorMesh(rim)), assets.ShaderMaterial("CupInterior", "GolfSim/CupInterior"));
        }

        /// <summary>Terrain height around the rim, relative to the pin.</summary>
        static float[] RimHeights(Transform pin, Terrain terrain)
        {
            var heights = new float[Segments];
            for (int i = 0; i < Segments; i++)
            {
                var world = pin.position + RimOffset(i);
                heights[i] = terrain.SampleHeight(world) + terrain.transform.position.y - pin.position.y;
            }
            return heights;
        }

        static Vector3 RimOffset(int i)
        {
            float a = i * Mathf.PI * 2f / Segments;
            return new Vector3(Mathf.Cos(a) * Radius, 0f, Mathf.Sin(a) * Radius);
        }

        static Mesh MaskMesh(float[] rim)
        {
            var b = new MeshBuilder();
            float centre = 0f;
            foreach (float h in rim) centre += h / Segments;
            int c = b.Vertex(new Vector3(0f, centre + MaskLift, 0f), Color.black);
            int first = b.Count;
            for (int i = 0; i < Segments; i++) b.Vertex(RimOffset(i) + Vector3.up * (rim[i] + MaskLift), Color.black);
            for (int i = 0; i < Segments; i++) b.Triangle(c, first + i, first + (i + 1) % Segments, Vector3.up);
            return b.Build("CupMask");
        }

        static Mesh InteriorMesh(float[] rim)
        {
            var b = new MeshBuilder();
            // Rings top to bottom; the liner rings are duplicated so the soil/plastic boundary stays sharp.
            int soilTop = Ring(b, rim, 0f, Soil, useRim: true);
            int soilBottom = Ring(b, rim, -SoilDepth, Soil * 0.6f, useRim: true);
            int linerTop = Ring(b, rim, -SoilDepth, Liner * 0.8f, useRim: true);
            int linerBottom = Ring(b, rim, -Depth, Liner * 0.4f, useRim: false);
            int floorRing = Ring(b, rim, -Depth, Floor * 0.6f, useRim: false);
            int floorCentre = b.Vertex(new Vector3(0f, -Depth, 0f), Floor * 0.5f);

            for (int i = 0; i < Segments; i++)
            {
                int n = (i + 1) % Segments;
                var inward = -RimOffset(i).normalized;
                Quad(b, soilTop + i, soilTop + n, soilBottom + n, soilBottom + i, inward);
                Quad(b, linerTop + i, linerTop + n, linerBottom + n, linerBottom + i, inward);
                b.Triangle(floorCentre, floorRing + i, floorRing + n, Vector3.up);
            }
            return b.Build("CupInterior");
        }

        /// <summary>A ring of vertices at a depth below the rim (useRim) or below the pin (flat cup bottom).</summary>
        static int Ring(MeshBuilder b, float[] rim, float depth, Color color, bool useRim)
        {
            int first = b.Count;
            for (int i = 0; i < Segments; i++)
                b.Vertex(RimOffset(i) + Vector3.up * ((useRim ? rim[i] : 0f) + depth), color);
            return first;
        }

        static void Quad(MeshBuilder b, int a, int c, int d, int e, Vector3 facing)
        {
            b.Triangle(a, c, d, facing);
            b.Triangle(a, d, e, facing);
        }

        static void Child(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>Small vertex/colour/triangle accumulator that orients each triangle toward a facing.</summary>
        class MeshBuilder
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Color> colors = new List<Color>();
            readonly List<int> triangles = new List<int>();

            public int Count => vertices.Count;

            public int Vertex(Vector3 position, Color color)
            {
                vertices.Add(position);
                colors.Add(color);
                return vertices.Count - 1;
            }

            public void Triangle(int a, int b, int c, Vector3 facing)
            {
                bool aligned = Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]), facing) > 0f;
                triangles.Add(a);
                triangles.Add(aligned ? b : c);
                triangles.Add(aligned ? c : b);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.SetColors(colors);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
