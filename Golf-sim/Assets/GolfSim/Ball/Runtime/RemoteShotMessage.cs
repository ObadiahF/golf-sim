using System;
using UnityEngine;

namespace GolfSim.Ball
{
    /// <summary>
    /// A shot from the SwingRemote phone app: a JSON datagram over UDP 4242, or the same fields in a
    /// "shot" message relayed by the game server's WebSocket. v1 senders only send the
    /// shot fields (speed, launch, azimuth, back, side) with no type; v2 adds v, type, id and club.
    /// type "discover" asks the sim to say hello; type "shot" (or none) is a shot.
    /// </summary>
    [Serializable]
    public class RemoteShotMessage
    {
        public const int Version = 2;

        public int v;
        public string type;
        public int id;
        public string club;
        [Tooltip("Ball speed, m/s.")] public float speed;
        [Tooltip("Launch angle, degrees.")] public float launch;
        [Tooltip("Start direction, degrees, + right.")] public float azimuth;
        [Tooltip("Backspin, rpm.")] public float back;
        [Tooltip("Sidespin, rpm, + curves right.")] public float side;

        public bool IsDiscover => type == "discover";
        public bool IsShot => string.IsNullOrEmpty(type) || type == "shot";

        /// <summary>Parses a datagram; false (with a reason) for anything that isn't a usable message.</summary>
        public static bool TryParse(string json, out RemoteShotMessage message, out string error)
        {
            message = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json) || json.TrimStart()[0] != '{') { error = "not JSON"; return false; }
            try { message = JsonUtility.FromJson<RemoteShotMessage>(json); }
            catch (Exception e) { error = "bad JSON: " + e.Message; return false; }
            if (message == null) { error = "empty message"; return false; }
            if (message.IsDiscover) return true;
            if (!message.IsShot) { error = $"unknown type '{message.type}'"; return false; }
            if (!IsFinite(message.speed) || !IsFinite(message.launch) || !IsFinite(message.azimuth) || !IsFinite(message.back) || !IsFinite(message.side))
            {
                error = "non-numeric shot values";
                return false;
            }
            if (message.speed <= 0f) { error = "ball speed must be above 0"; return false; }
            return true;
        }

        /// <summary>The shot as launch conditions, clamped to what a real ball can do.</summary>
        public ShotData ToShotData() => new ShotData
        {
            ballSpeed = Mathf.Clamp(speed, 0f, 100f), // 100 m/s = 224 mph, beyond any long-drive record
            launchAngle = Mathf.Clamp(launch, -10f, 70f),
            launchDirection = Mathf.Clamp(azimuth, -45f, 45f),
            backspin = Mathf.Clamp(back, -3000f, 15000f),
            sidespin = Mathf.Clamp(side, -6000f, 6000f),
            club = Clubs.Normalize(club),
        };

        static bool IsFinite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
    }

    /// <summary>
    /// The sim's reply to the phone. type "hello" (name, status), "ack" (id, status ok/busy/error,
    /// message) or "result" (id, carry/total/offline in yards, surface, outcome).
    /// </summary>
    [Serializable]
    public class RemoteReply
    {
        public int v = RemoteShotMessage.Version;
        public string type;
        public int id;
        public string status;
        public string name;
        public string message;
        public double carry, total, offline; // double so JSON shows 259.8, not 259.79998
        public string surface;
        public string outcome;

        public static RemoteReply Hello(string name, bool busy) =>
            new RemoteReply { type = "hello", name = name, status = busy ? "busy" : "ready" };

        public static RemoteReply Ack(int id, string status, string message = "") =>
            new RemoteReply { type = "ack", id = id, status = status, message = message };

        public static RemoteReply Result(int id, ShotResult result, BallStatus outcome) => new RemoteReply
        {
            type = "result",
            id = id,
            status = "ok",
            carry = Round(result.carry * ShotData.YardsPerMeter),
            total = Round(result.total * ShotData.YardsPerMeter),
            offline = Round(result.offline * ShotData.YardsPerMeter),
            surface = result.restingSurface ?? "",
            outcome = outcome.ToString(),
        };

        public string ToJson() => JsonUtility.ToJson(this);

        static double Round(float x) => Math.Round(x, 1);
    }
}
