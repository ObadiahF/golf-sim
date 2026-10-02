using UnityEngine;

namespace GolfSim.Net
{
    /// <summary>
    /// Where the game server is and how to authenticate. Lives at Resources/GolfServer.asset; without it the
    /// defaults below are used: the hosted server. Tick useLocalServer to play against a server on this PC or
    /// the LAN (docker compose). The token must match GOLF_API_TOKEN on the server and the app's ServerToken.txt.
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
        [Tooltip("Fallback shared secret for a local dev server; the real one comes from Resources/ServerToken.txt (see Token).")]
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
        public string TrainerKey => Secret("GOLF_TRAINER_KEY", "TrainerKey", "");

        /// <summary>
        /// The game server's token (GOLF_API_TOKEN on the server). Kept out of git: put it in
        /// Assets/GolfSim/Net/Resources/ServerToken.txt (gitignored) or the GOLF_API_TOKEN environment variable;
        /// without either, the dev token in <see cref="token"/> (what a local docker compose server accepts).
        /// </summary>
        public string Token => Secret("GOLF_API_TOKEN", "ServerToken", token);

        /// <summary>A secret from the environment variable, else the Resources text file, else the fallback.</summary>
        static string Secret(string envVar, string resource, string fallback)
        {
            var env = System.Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
            var file = Resources.Load<TextAsset>(resource);
            return file && !string.IsNullOrWhiteSpace(file.text) ? file.text.Trim() : fallback;
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
        public string SocketUrl(string role) => $"{ActiveUrl}/ws?token={Esc(Token)}&role={role}&name={Esc(DeviceName)}";

        static string Esc(string s) => System.Uri.EscapeDataString(s ?? "");
    }
}
