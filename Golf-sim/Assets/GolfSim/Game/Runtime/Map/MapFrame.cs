using GolfSim.Course;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// The ground a hole map shows, like a yardage book: a rectangle turned so the line from the tee to the pin points
    /// up, fitting the tee, the pin and the hole's path (doglegs included) with room around them. Maps world points to
    /// map coordinates 0..1 (x right, y down, as UI Toolkit draws) and back.
    /// </summary>
    public readonly struct MapFrame
    {
        const float Margin = 0.08f;      // of the fitted length, on every side
        const float MinBeyondPin = 45f;  // m of ground shown past the pin (a long carry still lands on the map, the flag fits)
        const float MinWidth = 120f;     // m across, for a short straight hole

        /// <summary>World point at the map's centre (y unused).</summary>
        public readonly Vector3 center;
        /// <summary>Flat unit vectors: up the map (tee to pin) and to its right.</summary>
        public readonly Vector3 up, right;
        /// <summary>Metres the map covers across (x) and up (y).</summary>
        public readonly Vector2 size;

        MapFrame(Vector3 center, Vector3 up, Vector2 size)
        {
            this.center = center;
            this.up = up;
            right = new Vector3(up.z, 0f, -up.x);
            this.size = size;
        }

        public bool IsValid => size.x > 0f && size.y > 0f;

        /// <summary>The frame for a hole on a view `aspect` (width / height) wide.</summary>
        public static MapFrame Fit(HoleInfo hole, float aspect)
        {
            Vector3 tee = hole.TeeWorld, pin = hole.PinWorld;
            var line = Vector3.ProjectOnPlane(pin - tee, Vector3.up);
            var up = line.sqrMagnitude > 1f ? line.normalized : Vector3.forward;
            var right = new Vector3(up.z, 0f, -up.x);

            // Extent of the hole along (y) and across (x) the line from the tee.
            float minX = 0f, maxX = 0f, minY = 0f, maxY = Vector3.Dot(line, up);
            foreach (var local in hole.holePath)
            {
                var d = hole.transform.TransformPoint(local) - tee;
                float x = Vector3.Dot(d, right), y = Vector3.Dot(d, up);
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
            float length = maxY - minY;
            float pad = Margin * Mathf.Max(length, 100f);
            minY -= pad;
            maxY = Mathf.Max(maxY + pad, Vector3.Dot(line, up) + MinBeyondPin);
            float width = Mathf.Max(maxX - minX + 2f * pad, MinWidth);
            float height = maxY - minY;
            // Grow the short side to the view's aspect so the scale is the same both ways.
            if (width / height < aspect) width = height * aspect;
            else height = width / aspect;

            float midX = (minX + maxX) * 0.5f, midY = (minY + maxY) * 0.5f;
            var center = tee + right * midX + up * midY;
            return new MapFrame(center, up, new Vector2(width, height));
        }

        /// <summary>Map coordinates (0..1, x right, y down) of a world point; outside 0..1 when off the map.</summary>
        public Vector2 ToMap(Vector3 world)
        {
            var d = world - center;
            return new Vector2(0.5f + Vector3.Dot(d, right) / size.x, 0.5f - Vector3.Dot(d, up) / size.y);
        }

        /// <summary>The world point (at the tee's height) at map coordinates (0..1, x right, y down).</summary>
        public Vector3 ToWorld(Vector2 map) =>
            center + right * ((map.x - 0.5f) * size.x) + up * ((0.5f - map.y) * size.y);

        /// <summary>A flat world direction as a map direction (y down), unit length.</summary>
        public Vector2 Direction(Vector3 world) => new Vector2(Vector3.Dot(world, right), -Vector3.Dot(world, up)).normalized;
    }
}
