using UnityEngine;

namespace GolfSim.Course
{
    /// <summary>Tee markers (placeholder spheres) and a regulation pin: flagstick, cloth flag and cup.</summary>
    public static class HoleMarkers
    {
        const float FlagstickHeight = 2.13f;   // 7 ft above the green
        const float FlagstickRadius = 0.0095f; // 3/4 in fiberglass
        static readonly Color StickColor = new Color(0.95f, 0.95f, 0.92f);
        static readonly Color FlagColor = new Color(0.80f, 0.06f, 0.06f);

        public static void Create(Transform parent, Vector3 teeLocal, Vector3 pinLocal, Terrain terrain, HoleAssets assets)
        {
            var tee = new GameObject("Tee").transform;
            tee.SetParent(parent, false);
            tee.localPosition = teeLocal;
            var teeColor = assets.ColorMaterial("TeeMarker", Color.white);
            Primitive(PrimitiveType.Sphere, tee, "Marker L", new Vector3(-1.5f, 0.08f, 0), Vector3.one * 0.16f, teeColor);
            Primitive(PrimitiveType.Sphere, tee, "Marker R", new Vector3(1.5f, 0.08f, 0), Vector3.one * 0.16f, teeColor);

            var pin = new GameObject("Pin").transform;
            pin.SetParent(parent, false);
            pin.localPosition = pinLocal;

            // The stick stands on the cup bottom, so it runs down into the hole like the real thing.
            float bottom = -CupBuilder.Depth, length = FlagstickHeight - bottom, d = FlagstickRadius * 2f;
            Primitive(PrimitiveType.Cylinder, pin, "Flagstick", new Vector3(0, bottom + length / 2f, 0),
                new Vector3(d, length / 2f, d), assets.ColorMaterial("FlagstickWhite", StickColor));

            var flag = new GameObject("Flag");
            flag.transform.SetParent(pin, false);
            flag.transform.localPosition = new Vector3(0f, FlagstickHeight - 0.03f, 0f);
            flag.AddComponent<MeshFilter>().sharedMesh =
                assets.Shared("Meshes/Flag.asset", () => FlagWave.BuildMesh(0.8f, 0.6f));
            flag.AddComponent<MeshRenderer>().sharedMaterial = assets.ColorMaterial("FlagCloth", FlagColor, doubleSided: true);
            flag.AddComponent<FlagWave>().ApplyHeading();

            CupBuilder.Create(pin, terrain, assets);
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
