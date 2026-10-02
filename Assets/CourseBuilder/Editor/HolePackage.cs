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
        public string sourceId;
        public PointList[] rings = new PointList[0]; // first = outer boundary, rest = holes
    }

    [Serializable]
    public class WaterBody
    {
        public float level; // surface elevation in meters (same datum as the heightmap's min/maxElevation)
        public PointList triangles = new PointList(); // flat triangle list, 3 points per triangle
    }

    [Serializable]
    public class HeightmapInfo
    {
        public string file;
        public int resolution;
        public float minElevation; // meters at raw value 0
        public float maxElevation; // meters at raw value 65535
    }

    [Serializable]
    public class ObjectsInfo
    {
        public string file;
        public int count;
    }

    [Serializable]
    public class SourceInfo
    {
        public string kind; // "osm" or "generated"
        public string crs;
        public double originEasting;
        public double originNorthing;
    }

    /// <summary>Mirror of hole.json, format version 2. The contract: Docs/hole-format/README.md.</summary>
    [Serializable]
    public class HolePackage
    {
        public const string FileName = "hole.json";
        const int SupportedVersion = 2;

        public int version;
        public string id;
        public string course;
        public string holeRef;
        public int par;
        public int handicap;
        [Tooltip("CourseTheme name that dresses the hole.")]
        public string theme;
        public SourceInfo source;
        public float sizeMeters;
        public HeightmapInfo heightmap;
        public PointList holePath;
        public Vector2 tee; // (x = east, y = north)
        public Vector2 pin;
        public HoleArea[] areas = new HoleArea[0];
        public WaterBody[] water = new WaterBody[0];
        public ObjectsInfo objects;

        [NonSerialized] public string assetPath;

        public string Folder => Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
        public float MinElevation => heightmap.minElevation;
        public float HeightRange => Mathf.Max(heightmap.maxElevation - heightmap.minElevation, 0.01f);
        public string DisplayName => $"{(string.IsNullOrEmpty(course) ? "Unknown course" : course)} / Hole {holeRef} (par {par})";

        public static HolePackage Load(string jsonAssetPath)
        {
            // Read from disk, not the AssetDatabase: a package rewritten by the Python tools may not be reimported yet.
            if (!File.Exists(jsonAssetPath)) throw new FileNotFoundException($"No hole package at {jsonAssetPath}");
            var pkg = JsonUtility.FromJson<HolePackage>(File.ReadAllText(jsonAssetPath));
            if (pkg.version != SupportedVersion)
                throw new InvalidDataException($"{jsonAssetPath}: package version {pkg.version}, expected {SupportedVersion}. " +
                                               "Upgrade it with: Tools/course_prep/.venv/bin/python Tools/course_prep/prep_hole.py migrate <folder>");
            pkg.assetPath = jsonAssetPath;
            return pkg;
        }

        /// <summary>Normalised heights [z, x] in 0..1, ready for TerrainData.SetHeights.</summary>
        public float[,] LoadHeights()
        {
            int n = heightmap.resolution;
            byte[] bytes = File.ReadAllBytes(Path.Combine(Folder, heightmap.file));
            if (bytes.Length != n * n * 2)
                throw new InvalidDataException($"{heightmap.file}: expected {n * n * 2} bytes for {n}x{n}, got {bytes.Length}");

            var heights = new float[n, n];
            for (int z = 0, i = 0; z < n; z++)
                for (int x = 0; x < n; x++, i += 2)
                    heights[z, x] = (bytes[i] | (bytes[i + 1] << 8)) / 65535f;
            return heights;
        }

        /// <summary>Every tree, shrub and rock, exactly as the package places them.</summary>
        public PlacedObject[] LoadObjects() =>
            ObjectsFile.Read(Path.Combine(Folder, objects.file), sizeMeters, objects.count);

        /// <summary>Local (x, z) meters -> position relative to the terrain origin, y in terrain-local meters.</summary>
        public Vector3 ToLocal(Vector2 xz, Terrain terrain) =>
            new Vector3(xz.x, terrain.SampleHeight(terrain.transform.position + new Vector3(xz.x, 0, xz.y)), xz.y);
    }
}
