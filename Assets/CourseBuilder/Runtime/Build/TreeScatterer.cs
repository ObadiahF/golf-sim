using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GolfSim.Course
{
    /// <summary>
    /// Places the package's objects.bin (every tree, shrub and rock) as Terrain tree instances, exactly where
    /// the file says, scaled so each model's top is at the object's height. Only the model choice is ours.
    /// </summary>
    public class TreeScatterer
    {
        readonly TerrainData data;
        readonly float size;
        readonly List<GameObject> prototypes = new List<GameObject>();
        readonly List<TreeInstance> instances = new List<TreeInstance>();
        readonly Dictionary<GameObject, float> modelHeights = new Dictionary<GameObject, float>();

        TreeScatterer(TerrainData data, float size)
        {
            this.data = data;
            this.size = size;
        }

        /// <returns>Number of instances placed.</returns>
        public static int Apply(TerrainData data, HolePackage pkg, ScatterSet scatter)
        {
            var s = new TreeScatterer(data, pkg.sizeMeters);
            var missing = new HashSet<ObjectKind>();
            foreach (var obj in pkg.LoadObjects())
            {
                var options = scatter.PrototypesFor(obj.kind);
                if (options == null) { missing.Add(obj.kind); continue; }
                s.Add(ScatterSet.Pick(options, obj.variant), obj);
            }
            if (missing.Count > 0)
                Debug.LogWarning($"[CourseBuilder] No models for {string.Join(", ", missing)} in {scatter.name}: those objects were skipped. " +
                                 "Add assets with Golf > Catalog > Add Selected Assets.");

            data.treePrototypes = s.prototypes.Select(p => new TreePrototype { prefab = p }).ToArray();
            data.SetTreeInstances(s.instances.ToArray(), true);
            return s.instances.Count;
        }

        void Add(ScatterSet.Prototype proto, PlacedObject obj)
        {
            if (proto == null) return;
            int index = prototypes.IndexOf(proto.prefab);
            if (index < 0)
            {
                index = prototypes.Count;
                prototypes.Add(proto.prefab);
            }

            float scale = obj.height / ModelHeight(proto.prefab);
            instances.Add(new TreeInstance
            {
                prototypeIndex = index,
                position = new Vector3(obj.position.x / size, 0f, obj.position.y / size), // y is snapped to the terrain
                widthScale = scale,
                heightScale = scale,
                rotation = obj.rotation * Mathf.Deg2Rad, // Unity's yaw is clockwise from +z (north)
                color = Color.white,
                lightmapColor = Color.white,
            });
        }

        /// <summary>The model's own height in meters at scale 1 (LOD0 if it has LODs), measured once per prefab.</summary>
        float ModelHeight(GameObject prefab)
        {
            if (modelHeights.TryGetValue(prefab, out float h)) return h;
            var copy = Object.Instantiate(prefab);
            copy.hideFlags = HideFlags.HideAndDontSave;
            copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            try
            {
                var lods = copy.GetComponent<LODGroup>()?.GetLODs();
                var renderers = lods != null && lods.Length > 0 ? lods[0].renderers.Where(r => r).ToArray()
                                                                : copy.GetComponentsInChildren<Renderer>();
                float top = renderers.Length > 0 ? renderers.Max(r => r.bounds.max.y) : 0f;
                h = top > 0.01f ? top : 1f; // pivots sit at the ground, so the top is the height
            }
            finally
            {
                Object.DestroyImmediate(copy);
            }
            modelHeights[prefab] = h;
            return h;
        }
    }
}
