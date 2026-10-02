using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GolfSim.Course
{
    /// <summary>
    /// Places the package's objects.bin (every tree, shrub and rock) as Terrain tree instances, exactly where
    /// the file says, scaled so each model's top is at the object's height. Only the model choice is ours, so the
    /// drawn crown of each object (ModelShape) is written to its obstacle for collisions.
    /// </summary>
    public class TreeScatterer
    {
        readonly TerrainData data;
        readonly float size;
        readonly List<GameObject> prototypes = new List<GameObject>();
        readonly List<TreeInstance> instances = new List<TreeInstance>();
        readonly Dictionary<GameObject, ModelShape> shapes = new Dictionary<GameObject, ModelShape>();

        TreeScatterer(TerrainData data, float size)
        {
            this.data = data;
            this.size = size;
        }

        /// <param name="objects">The package's objects (pkg.LoadObjects()).</param>
        /// <param name="obstacles">The same objects as obstacles (same order): each drawn one gets its model's crown.</param>
        /// <returns>Number of instances placed.</returns>
        public static int Apply(TerrainData data, HolePackage pkg, PlacedObject[] objects, ScatterSet scatter, Obstacle[] obstacles)
        {
            var s = new TreeScatterer(data, pkg.sizeMeters);
            var missing = new HashSet<ObjectKind>();
            for (int i = 0; i < objects.Length; i++)
            {
                var obj = objects[i];
                var rule = scatter.RuleFor(obj.kind);
                if (rule == null) { missing.Add(obj.kind); continue; }
                var proto = ScatterSet.Pick(rule.prototypes, obj.variant);
                if (proto == null) continue;
                var shape = s.Add(proto, obj);
                if (shape.HasCrown && obj.kind < ObjectKind.Boulder) SetCrown(ref obstacles[i], shape, obj.height, rule.kind);
            }
            if (missing.Count > 0)
                Debug.LogWarning($"[CourseBuilder] No models for {string.Join(", ", missing)} in {scatter.name}: those objects were skipped. " +
                                 "Add assets with Golf > Catalog > Add Selected Assets.");

            data.treePrototypes = s.prototypes.Select(p => new TreePrototype { prefab = p }).ToArray();
            data.SetTreeInstances(s.instances.ToArray(), true);
            return s.instances.Count;
        }

        static void SetCrown(ref Obstacle o, in ModelShape shape, float height, ObjectKind drawnAs)
        {
            o.crownRadius = shape.crownRadius * height; // instances scale uniformly (width = height scale)
            o.crownBottom = shape.crownBottom * height;
            o.crownTop = shape.crownTop * height;
            o.crownCone = shape.cone;
            o.crownKind = (byte)drawnAs;
        }

        ModelShape Add(ScatterSet.Prototype proto, PlacedObject obj)
        {
            int index = prototypes.IndexOf(proto.prefab);
            if (index < 0)
            {
                index = prototypes.Count;
                prototypes.Add(proto.prefab);
            }

            var shape = Shape(proto.prefab);
            float scale = obj.height / shape.height;
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
            return shape;
        }

        /// <summary>The model's height and crown at scale 1, measured once per prefab.</summary>
        ModelShape Shape(GameObject prefab)
        {
            if (!shapes.TryGetValue(prefab, out var shape)) shapes[prefab] = shape = ModelShape.Measure(prefab);
            return shape;
        }
    }
}
