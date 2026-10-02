using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>Placeholder tee and pin markers built from primitives. Swap for real models later.</summary>
    public static class HoleMarkers
    {
        const float FlagstickHeight = 2.13f; // 7 ft regulation
        const float FlagstickRadius = 0.03f;

        public static void Create(Transform parent, Vector3 teeLocal, Vector3 pinLocal)
        {
            var tee = new GameObject("Tee").transform;
            tee.SetParent(parent, false);
            tee.localPosition = teeLocal;
            var teeColor = GeneratedAssets.ColorMaterial("TeeMarker", Color.white);
            Primitive(PrimitiveType.Sphere, tee, "Marker L", new Vector3(-1.5f, 0.08f, 0), Vector3.one * 0.16f, teeColor);
            Primitive(PrimitiveType.Sphere, tee, "Marker R", new Vector3(1.5f, 0.08f, 0), Vector3.one * 0.16f, teeColor);

            var pin = new GameObject("Pin").transform;
            pin.SetParent(parent, false);
            pin.localPosition = pinLocal;
            float d = FlagstickRadius * 2;
            Primitive(PrimitiveType.Cylinder, pin, "Flagstick", new Vector3(0, FlagstickHeight / 2, 0),
                new Vector3(d, FlagstickHeight / 2, d), GeneratedAssets.ColorMaterial("Flagstick", new Color(0.95f, 0.85f, 0.2f)));
            Primitive(PrimitiveType.Cube, pin, "Flag", new Vector3(0.25f, FlagstickHeight - 0.18f, 0),
                new Vector3(0.5f, 0.35f, 0.01f), GeneratedAssets.ColorMaterial("Flag", new Color(0.85f, 0.1f, 0.1f)));
        }

        /// <summary>Faces the tee markers toward the pin so they sit across the line of play.</summary>
        public static void AimTee(Transform parent, Vector3 teeLocal, Vector3 pinLocal)
        {
            var tee = parent.Find("Tee");
            var flat = new Vector3(pinLocal.x - teeLocal.x, 0, pinLocal.z - teeLocal.z);
            if (tee && flat.sqrMagnitude > 0.01f) tee.localRotation = Quaternion.LookRotation(flat);
        }

        static void Primitive(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>()); // markers must not interfere with ball physics
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }
}
