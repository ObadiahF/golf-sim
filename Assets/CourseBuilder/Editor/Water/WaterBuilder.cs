using UnityEngine;
using UnityEngine.Rendering;

namespace GolfSim.CourseEditor
{
    /// <summary>Turns hole.json water bodies into flat water meshes shaped like the OSM ponds.</summary>
    public static class WaterBuilder
    {
        const string FallbackMaterialPath = GeneratedAssets.Root + "/Materials/PondWater.mat";
        static readonly Color FallbackColor = new Color(0.08f, 0.20f, 0.18f, 0.88f); // murky pond green

        public static void Create(Transform parent, HolePackage pkg, Material waterMaterial)
        {
            var material = waterMaterial ? waterMaterial : FallbackMaterial();
            for (int i = 0; i < pkg.water.Length; i++)
            {
                var body = pkg.water[i];
                var mesh = BuildMesh(body.triangles);
                mesh.name = $"Hole{pkg.holeRef}_Water{i}";
                GeneratedAssets.SaveFresh($"{pkg.Folder}/Water{i}.asset", mesh);

                var go = new GameObject($"Water {i}");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = new Vector3(0f, body.level - pkg.MinElevation, 0f);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        /// <summary>Transparent glossy URP Lit, used until a proper water material is assigned in the layer set.</summary>
        static Material FallbackMaterial() => GeneratedAssets.LoadOrCreate(FallbackMaterialPath, () =>
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = FallbackColor };
            mat.SetFloat("_Surface", 1f); // transparent
            mat.SetFloat("_Blend", 0f);   // alpha
            mat.SetFloat("_Smoothness", 0.95f);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.renderQueue = (int)RenderQueue.Transparent;
            return mat;
        });

        static Mesh BuildMesh(PointList triangles)
        {
            int count = triangles.Count;
            var vertices = new Vector3[count];
            var uvs = new Vector2[count];
            var indices = new int[count];
            for (int i = 0; i < count; i++)
            {
                var p = triangles[i];
                vertices[i] = new Vector3(p.x, 0f, p.y);
                uvs[i] = p * 0.1f; // world-scaled UVs (10 m per tile) for water normal maps
            }
            // Triangulation winding isn't guaranteed: orient every triangle so its normal faces up.
            for (int t = 0; t + 2 < count; t += 3)
            {
                bool up = Vector3.Cross(vertices[t + 1] - vertices[t], vertices[t + 2] - vertices[t]).y > 0f;
                indices[t] = t;
                indices[t + 1] = up ? t + 1 : t + 2;
                indices[t + 2] = up ? t + 2 : t + 1;
            }
            var mesh = new Mesh { vertices = vertices, uv = uvs, triangles = indices };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
