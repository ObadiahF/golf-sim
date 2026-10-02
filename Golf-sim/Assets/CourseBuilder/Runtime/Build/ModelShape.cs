using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GolfSim.Course
{
    /// <summary>
    /// A tree, shrub or rock model's size at scale 1, measured once per prefab from its LOD0 meshes: its height, and
    /// the drawn crown (the foliage: every submesh but the bark) as fractions of that height, fitted with a cone or an
    /// ellipsoid around the pivot. Ball collisions and replay cameras use the crown, so they match what's drawn.
    /// </summary>
    public readonly struct ModelShape
    {
        const float Coverage = 0.85f;  // the fitted crown holds this share of the foliage vertices (leaf cards overhang)
        const float MinProfile = 0.3f; // a shape's width at its tips counts as at least this, so tip foliage can't blow it up
        const float BaseShare = 0.05f; // the crown starts above the lowest 5% of the foliage (stray low twigs)

        /// <summary>Model height in meters (pivots sit at the ground, so the top is the height).</summary>
        public readonly float height;
        /// <summary>Crown bottom, top and widest radius (a cone's at its base), fractions of the height; radius 0 = no crown.</summary>
        public readonly float crownBottom, crownTop, crownRadius;
        /// <summary>The crown narrows to the top like a cone; otherwise an ellipsoid.</summary>
        public readonly bool cone;

        public bool HasCrown => crownRadius > 0f && crownTop > crownBottom;

        ModelShape(float height, float bottom, float top, float radius, bool cone)
        {
            this.height = height;
            crownBottom = bottom;
            crownTop = top;
            crownRadius = radius;
            this.cone = cone;
        }

        public static ModelShape Measure(GameObject prefab)
        {
            var copy = Object.Instantiate(prefab);
            copy.hideFlags = HideFlags.HideAndDontSave;
            copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            try
            {
                var lods = copy.GetComponent<LODGroup>()?.GetLODs();
                var renderers = lods != null && lods.Length > 0 ? lods[0].renderers.Where(r => r).ToArray()
                                                                : copy.GetComponentsInChildren<Renderer>();
                float top = renderers.Length > 0 ? renderers.Max(r => r.bounds.max.y) : 0f;
                float height = top > 0.01f ? top : 1f;
                var foliage = Foliage(renderers);
                return foliage.Count < 8 ? new ModelShape(height, 0f, 0f, 0f, false) : Fit(foliage, height);
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
        }

        /// <summary>Foliage vertices in model space: every submesh whose material isn't bark (all of a single-material mesh).</summary>
        static List<Vector3> Foliage(Renderer[] renderers)
        {
            var points = new List<Vector3>();
            foreach (var r in renderers)
            {
                var filter = r.GetComponent<MeshFilter>();
                var mesh = filter ? filter.sharedMesh : null;
                if (!mesh || !mesh.isReadable) continue;
                var vertices = mesh.vertices;
                var materials = r.sharedMaterials;
                var used = new HashSet<int>();
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var material = s < materials.Length ? materials[s] : null;
                    bool bark = mesh.subMeshCount > 1 && material && material.name.ToLowerInvariant().Contains("bark");
                    if (!bark) used.UnionWith(mesh.GetIndices(s));
                }
                foreach (int i in used) points.Add(r.localToWorldMatrix.MultiplyPoint3x4(vertices[i]));
            }
            return points;
        }

        /// <summary>Fits a cone and an ellipsoid holding `Coverage` of the foliage, and keeps the smaller one.</summary>
        static ModelShape Fit(List<Vector3> foliage, float height)
        {
            var ys = foliage.Select(p => p.y).OrderBy(y => y).ToArray();
            float bottom = ys[(int)(ys.Length * BaseShare)], top = ys[ys.Length - 1];
            float span = Mathf.Max(top - bottom, 0.01f);
            float cone = Needed(foliage, bottom, span, u => 1f - u);
            float ellipsoid = Needed(foliage, bottom, span, u => Mathf.Sqrt(Mathf.Max(0f, 1f - (2f * u - 1f) * (2f * u - 1f))));
            bool isCone = cone * cone / 3f < ellipsoid * ellipsoid * 2f / 3f; // volumes, without π and the span
            return new ModelShape(height, bottom / height, top / height, (isCone ? cone : ellipsoid) / height, isCone);
        }

        /// <summary>Widest radius a shape with this profile (width 0..1 at fraction u of the crown) needs to hold the foliage.</summary>
        static float Needed(List<Vector3> foliage, float bottom, float span, System.Func<float, float> profile)
        {
            var needed = new List<float>(foliage.Count);
            foreach (var p in foliage)
            {
                float u = (p.y - bottom) / span;
                if (u < 0f || u > 1f) continue;
                needed.Add(new Vector2(p.x, p.z).magnitude / Mathf.Max(profile(u), MinProfile));
            }
            if (needed.Count == 0) return 0f;
            needed.Sort();
            return needed[Mathf.Min(needed.Count - 1, (int)(needed.Count * Coverage))];
        }
    }
}
