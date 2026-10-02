using UnityEngine;

namespace GolfSim.Course
{
    /// <summary>Gameplay-facing description of a generated hole. Positions are local to this transform.</summary>
    public class HoleInfo : MonoBehaviour
    {
        public string course;
        public string holeRef;
        public int par;
        public int handicap;

        public Vector3 teePosition;
        public Vector3 pinPosition;
        public Vector3[] holePath = new Vector3[0];

        [Tooltip("Surface name for each terrain layer index (green, fairway, rough...), for ball physics.")]
        public string[] terrainLayerSurfaces = new string[0];

        [Tooltip("Asset path of the hole.json this hole was generated from.")]
        public string sourcePackage;

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
