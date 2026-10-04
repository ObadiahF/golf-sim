using UnityEngine;

namespace GolfSim.Course
{
    /// <summary>A solid object the ball can hit: a trunk, rock or shrub as an upright cylinder, plus a canopy for trees.</summary>
    [System.Serializable]
    public struct Obstacle
    {
        public Vector3 position;   // local to the hole, at the base on the ground
        public float radius;       // solid trunk / body radius, meters
        public float height;       // meters, ground to top
        public byte kind;          // objects.bin kind (0 conifer .. 6 rock)
        // The crown as drawn (measured from the model at build time), meters above the base; radius 0 = not measured.
        public float crownRadius;  // widest radius (a cone's at its base)
        public float crownBottom, crownTop;
        public bool crownCone;     // narrows to the top; otherwise an ellipsoid
        public byte crownKind;     // the tree kind whose model draws it (its leaf density)

        public bool IsTree => kind <= 3;
        public bool HasCrown => crownRadius > 0f && crownTop > crownBottom;
    }

    /// <summary>Gameplay-facing description of a generated hole. Positions are local to this transform.</summary>
    public class HoleInfo : MonoBehaviour
    {
        public string course;
        public string holeRef;
        public int par;
        public int handicap;
        [Tooltip("CourseTheme name the hole was dressed with (hole.json \"theme\"); picks its scenery (ThemeScenery).")]
        public string theme;

        public Vector3 teePosition;
        public Vector3 pinPosition;
        public Vector3[] holePath = new Vector3[0];

        [Tooltip("Surface name for each terrain layer index (green, fairway, rough...), for ball physics.")]
        public string[] terrainLayerSurfaces = new string[0];

        [Tooltip("Asset path of the hole.json this hole was generated from.")]
        public string sourcePackage;

        [Tooltip("Every tree, shrub and rock from the package (local positions at the ground), for ball collisions.")]
        public Obstacle[] obstacles = new Obstacle[0];

        public Vector3 TeeWorld => transform.TransformPoint(teePosition);
        public Vector3 PinWorld => transform.TransformPoint(pinPosition);
        public float TeeToPinMeters => Vector3.Distance(teePosition, pinPosition);

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            for (int i = 1; i < holePath.Length; i++)
                Gizmos.DrawLine(transform.TransformPoint(holePath[i - 1]), transform.TransformPoint(holePath[i]));
        }
    }
}
