using System;
using System.IO;
using UnityEngine;

namespace GolfSim.Course
{
    /// <summary>Object kinds in objects.bin. Append-only: values match Tools/course_prep/objects_bin.py.</summary>
    public enum ObjectKind : byte { Conifer, Deciduous, Palm, Cactus, Shrub, Boulder, Rock }

    public struct PlacedObject
    {
        public ObjectKind kind;
        public Vector2 position; // meters (x = east, y = north)
        public float height;     // meters, ground to top
        public float radius;     // meters, collision footprint
        public float rotation;   // degrees clockwise from north
        public float variant;    // 0..1, picks the model
    }

    /// <summary>Reads objects.bin (contract: Docs/hole-format/README.md, section 5).</summary>
    public static class ObjectsFile
    {
        const int HeaderBytes = 16, MinRecordBytes = 12, Version = 1;
        static readonly int KindCount = Enum.GetValues(typeof(ObjectKind)).Length;

        public static PlacedObject[] Read(string path, float sizeMeters, int expectedCount)
        {
            byte[] b = File.ReadAllBytes(path);
            if (b.Length < HeaderBytes || b[0] != 'G' || b[1] != 'O' || b[2] != 'B' || b[3] != 'J')
                throw new InvalidDataException($"{path}: not an objects.bin file");
            int version = BitConverter.ToUInt16(b, 4), recordSize = BitConverter.ToUInt16(b, 6);
            int count = (int)BitConverter.ToUInt32(b, 8);
            if (version != Version) throw new InvalidDataException($"{path}: version {version}, expected {Version}");
            if (recordSize < MinRecordBytes || count != expectedCount || b.Length != HeaderBytes + count * recordSize)
                throw new InvalidDataException($"{path}: {count} records of {recordSize} bytes in {b.Length} bytes; hole.json says {expectedCount}");

            var objects = new PlacedObject[count];
            int kept = 0, unknown = 0;
            for (int i = 0, o = HeaderBytes; i < count; i++, o += recordSize)
            {
                if (b[o] >= KindCount) { unknown++; continue; } // a kind newer than this reader
                objects[kept++] = new PlacedObject
                {
                    kind = (ObjectKind)b[o],
                    variant = b[o + 1] / 256f,
                    rotation = b[o + 2] * 360f / 256f,
                    position = new Vector2(BitConverter.ToUInt16(b, o + 4), BitConverter.ToUInt16(b, o + 6)) / 65535f * sizeMeters,
                    height = BitConverter.ToUInt16(b, o + 8) / 100f,
                    radius = BitConverter.ToUInt16(b, o + 10) / 100f,
                };
            }
            if (unknown > 0) Debug.LogWarning($"[CourseBuilder] {path}: skipped {unknown} objects of unknown kinds");
            Array.Resize(ref objects, kept);
            return objects;
        }
    }
}
