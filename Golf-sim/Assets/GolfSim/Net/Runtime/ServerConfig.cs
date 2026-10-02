using UnityEngine;

namespace GolfSim.Net
{
    /// <summary>
    /// Where the game server is and how to authenticate. Lives at Resources/GolfServer.asset; without it the
    /// defaults below are used: the hosted server. Tick useLocalServer to play against a server on this PC or
    /// the LAN (docker compose). The token must match GOLF_API_TOKEN on the server and AppConfig in the app.
    /// </summary>
    [CreateAssetMenu(menuName = "Golf/Server Config", fileName = "GolfServer")]
    public class ServerConfig : ScriptableObject
    {
        public const string ResourceName = "GolfServer";

        public const string HostedServer = "wss://golf-server.obadiahfusco.xyz";

        [Tooltip("Base WebSocket URL of the hosted game server (the /ws path is added).")]
        public string serverUrl = HostedServer;
        [Tooltip("Use localServerUrl instead, e.g. a docker compose server on this PC.")]
        public bool useLocalServer;
        [Tooltip("Base WebSocket URL of a local or LAN server, e.g. ws://localhost:8080 or ws://192.168.1.20:8080.")]
        public string localServerUrl = "ws://localhost:8080";
        [Tooltip("Shared secret; must match the server's GOLF_API_TOKEN.")]
        public string token = "golf-sim-dev-token";
        [Tooltip("Name shown to the phones; blank uses the computer's name.")]
        public string deviceName = "";
        [Tooltip("Connect when Play starts and keep reconnecting.")]
        public bool autoConnect = true;
        [Tooltip("Seconds between keepalive pings.")]
        public float pingInterval = 20f;
        [Tooltip("Longest wait between reconnect attempts, in seconds.")]
        public float maxReconnectDelay = 5f;

        [Header("Course Trainer (top-rated holes)")]
        [Tooltip("Rounds play the trainer's most-liked holes, downloaded when the round starts.")]
        public bool useTopHoles = true;
        [Tooltip("Base URL of the Course Trainer.")]
        public string trainerUrl = "https://golf-trainer.obadiahfusco.xyz";

        /// <summary>
        /// The trainer's read-only game key (TRAINER_GAME_KEY on the server). Kept out of git: put it in
        /// Assets/GolfSim/Net/Resources/TrainerKey.txt (gitignored) or the GOLF_TRAINER_KEY environment variable.
        /// </summary>
        public string TrainerKey
        {
            get
            {
                var env = System.Environment.GetEnvironmentVariable("GOLF_TRAINER_KEY");
                if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
                var file = Resources.Load<TextAsset>("TrainerKey");
                return file ? file.text.Trim() : "";
            }
        }

        static ServerConfig loaded;

        /// <summary>The project's config, or the defaults.</summary>
        public static ServerConfig Load()
        {
            if (!loaded) loaded = Resources.Load<ServerConfig>(ResourceName);
            if (!loaded) loaded = CreateInstance<ServerConfig>();
            return loaded;
        }

        /// <summary>The name the phones see (the server allows 40 characters).</summary>
        public string DeviceName
        {
            get
            {
                string name = (string.IsNullOrWhiteSpace(deviceName) ? SystemInfo.deviceName : deviceName).Trim();
                return name.Length > 40 ? name.Substring(0, 40) : name;
            }
        }

        /// <summary>The base URL in use: the local override or the hosted server.</summary>
        public string ActiveUrl => (useLocalServer && !string.IsNullOrWhiteSpace(localServerUrl) ? localServerUrl : serverUrl).Trim().TrimEnd('/');

        /// <summary>The same server for REST: https://host (or http://host:port for a ws:// URL).</summary>
        public string HttpUrl => ActiveUrl.StartsWith("wss://") ? "https://" + ActiveUrl.Substring(6)
            : ActiveUrl.StartsWith("ws://") ? "http://" + ActiveUrl.Substring(5) : ActiveUrl;

        /// <summary>wss://host/ws?token=...&amp;role=sim&amp;name=...</summary>
        public string SocketUrl(string role) => $"{ActiveUrl}/ws?token={Esc(token)}&role={role}&name={Esc(DeviceName)}";

        static string Esc(string s) => System.Uri.EscapeDataString(s ?? "");
    }
}
