using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GolfSim.Ball
{
    /// <summary>
    /// One surface's live overrides from the game server's physics profile (GET /api/physics, the "physics" message).
    /// A field the server leaves out stays NaN: use the settings asset's value. The server never sends null here
    /// (JsonUtility would read it as 0).
    /// </summary>
    [Serializable]
    public class SurfaceOverride
    {
        public string surface;
        public float rolling = float.NaN, restitution = float.NaN, friction = float.NaN;

        public bool IsEmpty => float.IsNaN(rolling) && float.IsNaN(restitution) && float.IsNaN(friction);
    }

    /// <summary>The server's ball-physics profile: overrides per surface (Game-server docs/PROTOCOL-physics.md).</summary>
    [Serializable]
    public class PhysicsProfile
    {
        public SurfaceOverride[] surfaces = new SurfaceOverride[0];

        public bool IsEmpty => surfaces == null || Array.TrueForAll(surfaces, s => s == null || s.IsEmpty);
    }

    /// <summary>
    /// Live tuning of the ground response on top of the settings asset, so friction can be iterated from the phone
    /// without a rebuild. The asset is never modified: Resolve hands out a runtime copy with the overrides applied
    /// (one per asset per profile; a new profile makes new copies), and a ball keeps the copy it was hit with until
    /// the shot is over (GolfBall.ShotSettings), so a change applies from the next shot. No profile (offline, or the
    /// server has none) means the asset itself.
    /// </summary>
    public static class BallPhysicsProfile
    {
        /// <summary>The profile in use, or null for the asset's own values.</summary>
        public static PhysicsProfile Current { get; private set; }
        /// <summary>Raised on the main thread after a different profile is applied.</summary>
        public static event Action Changed;

        static readonly Dictionary<BallPhysicsSettings, BallPhysicsSettings> tuned = new Dictionary<BallPhysicsSettings, BallPhysicsSettings>();
        static string currentJson = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Changed = null;
            Current = null;
            currentJson = "";
            tuned.Clear();
        }

        /// <summary>Uses this profile from the next shot; null or empty goes back to the asset's values. Logs the change.</summary>
        public static void Apply(PhysicsProfile profile)
        {
            if (profile != null && profile.IsEmpty) profile = null;
            string json = profile == null ? "" : JsonUtility.ToJson(profile);
            if (json == currentJson) return; // the same profile again (every reconnect's hello)
            currentJson = json;
            Current = profile;
            tuned.Clear(); // copies handed out earlier stay valid for the shots (and replays) that hold them
            Debug.Log($"[BallPhysics] Physics profile applied from the next shot: {Describe(profile)}");
            Changed?.Invoke();
        }

        /// <summary>
        /// The settings a new shot uses: the asset with the current profile on top, or the asset itself without one.
        /// A runtime copy passed in is returned as it is (a replay re-simulating with the settings of its shot).
        /// </summary>
        public static BallPhysicsSettings Resolve(BallPhysicsSettings asset)
        {
            if (Current == null || !asset || asset.IsRuntimeCopy) return asset;
            if (!tuned.TryGetValue(asset, out var copy) || !copy)
                tuned[asset] = copy = asset.WithOverrides(Current);
            return copy;
        }

        /// <summary>"green rolling 0.07 (Stimp 8.0 ft), rough friction 0.6", or "built-in values".</summary>
        public static string Describe(PhysicsProfile profile)
        {
            if (profile == null || profile.IsEmpty) return "built-in values";
            var sb = new StringBuilder();
            foreach (var s in profile.surfaces)
            {
                if (s == null || s.IsEmpty) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(s.surface);
                if (!float.IsNaN(s.rolling))
                {
                    sb.Append($" rolling {s.rolling:0.###}");
                    if (s.surface == "green") sb.Append($" (Stimp {PuttModel.StimpFeet(s.rolling):0.0} ft)");
                }
                if (!float.IsNaN(s.restitution)) sb.Append($" restitution {s.restitution:0.###}");
                if (!float.IsNaN(s.friction)) sb.Append($" friction {s.friction:0.###}");
            }
            return sb.ToString();
        }
    }
}
