using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GolfSim.CourseEditor
{
    /// <summary>Flat [x0, z0, x1, z1, ...] list in local terrain meters (x = east, z = north).</summary>
    [Serializable]
    public class PointList
    {
        public float[] points = new float[0];

        public int Count => points.Length / 2;
        public Vector2 this[int i] => new Vector2(points[2 * i], points[2 * i + 1]);
    }

    [Serializable]
    public class HoleArea
    {
        public string surface;
        public string osmId;
        public PointList[] rings = new PointList[0]; // first = outer boundary, rest = holes
    }

    [Serializable]
    public class WaterBody
    {
        public float level; // surface elevation in meters (same datum as min/maxElevation)
        public PointList triangles = new PointList(); // flat triangle list, 3 points per triangle
    }

    /// <summary>Mirror of hole.json written by Tools/course_prep/package.py.</summary>
    [Serializable]
    public class HolePackage
    {
        public const string FileName = "hole.json";
        const int SupportedVersion = 1;

        public int version;
        public string course;
        public string holeRef;
        public int par;
        public int handicap;
        public string crs;
        public double originEasting;
        public double originNorthing;
        public float sizeMeters;
        public string heightmapFile;
        public int heightmapResolution;
        public float minElevation;
        public float maxElevation;
        public PointList holePath;
        public Vector2 tee; // (x, z)
        public Vector2 pin; // (x, z)
        public HoleArea[] areas = new HoleArea[0];
        public Vector2[] trees = new Vector2[0]; // individually mapped trees (x, z)
        public WaterBody[] water = new WaterBody[0];
        [Tooltip("CourseTheme name for generated holes; empty for real courses (catalog default theme).")]
        public string theme;

        [NonSerialized] public string assetPath;

        public string Folder => Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
        public float HeightRange => Mathf.Max(maxElevation - minElevation, 0.01f);
        public string DisplayName => $"{(string.IsNullOrEmpty(course) ? "Unknown course" : course)} / Hole {holeRef} (par {par})";

        public static HolePackage Load(string jsonAssetPath)
        {
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(jsonAssetPath);
            if (text == null) throw new FileNotFoundException($"No hole package at {jsonAssetPath}");

            var pkg = JsonUtility.FromJson<HolePackage>(text.text);
            if (pkg.version != SupportedVersion)
                throw new InvalidDataException($"{jsonAssetPath}: package version {pkg.version}, expected {SupportedVersion}. Rebuild it with prep_hole.py.");
            pkg.assetPath = jsonAssetPath;
            return pkg;
        }

        /// <summary>Normalised heights [z, x] in 0..1, ready for TerrainData.SetHeights.</summary>
        public float[,] LoadHeights()
        {
            int n = heightmapResolution;
            byte[] bytes = File.ReadAllBytes(Path.Combine(Folder, heightmapFile));
            if (bytes.Length != n * n * 2)
                throw new InvalidDataException($"{heightmapFile}: expected {n * n * 2} bytes for {n}x{n}, got {bytes.Length}");

            var heights = new float[n, n];
            for (int z = 0, i = 0; z < n; z++)
                for (int x = 0; x < n; x++, i += 2)
                    heights[z, x] = (bytes[i] | (bytes[i + 1] << 8)) / 65535f;
            return heights;
        }

        /// <summary>Local (x, z) meters -> position relative to the terrain origin, y in terrain-local meters.</summary>
        public Vector3 ToLocal(Vector2 xz, Terrain terrain) =>
            new Vector3(xz.x, terrain.SampleHeight(terrain.transform.position + new Vector3(xz.x, 0, xz.y)), xz.y);
    }
}
