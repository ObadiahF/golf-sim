using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GolfSim.Ball;
using UnityEngine;

namespace GolfSim.Net
{
    /// <summary>
    /// The sim's WebSocket to the game server (role=sim). A background task connects, reads frames into a
    /// queue, sends what the main thread queued, pings, and reconnects with backoff; Update (or Pump) raises
    /// one C# event per message type on the main thread. Lives across scenes (DontDestroyOnLoad).
    /// </summary>
    public class SimConnection : MonoBehaviour
    {
        public enum Status { Offline, Connecting, Connected }

        const int ReceiveBuffer = 16 * 1024;

        public ServerConfig config;
        public bool logMessages;

        public static SimConnection Instance { get; private set; }
        public Status State => (Status)status;
        public bool IsConnected => State == Status.Connected;

        public event Action Connected, Disconnected;
        public event Action<HelloMessage> HelloReceived;
        public event Action<string> NavReceived, ClubReceived, ServerError;
        public event Action<float> AimReceived;
        public event Action AimResetReceived, MulliganReceived, SkipReceived;
        public event Action<RemoteShotMessage> ShotReceived;
        public event Action<GameView> GameStarted, ScorecardReceived, GameFinished;

        enum Kind { Message, Opened, Closed }

        readonly ConcurrentQueue<(Kind kind, string text)> inbox = new ConcurrentQueue<(Kind, string)>();
        readonly ConcurrentQueue<string> outbox = new ConcurrentQueue<string>();
        readonly SemaphoreSlim outboxSignal = new SemaphoreSlim(0);
        readonly List<string> unsentReliable = new List<string>(); // main thread only
        CancellationTokenSource cts;
        volatile int status;
        long lastReceivedTicks;

        /// <summary>Creates the persistent connection object (once).</summary>
        public static SimConnection Create(ServerConfig config)
        {
            if (Instance) return Instance;
            var go = new GameObject("Sim Connection");
            DontDestroyOnLoad(go);
            go.SetActive(false);
            var connection = go.AddComponent<SimConnection>();
            connection.config = config;
            go.SetActive(true);
            return connection;
        }

        void Awake() => Instance = this;

        void OnEnable()
        {
            if (!config) config = ServerConfig.Load();
            if (config.autoConnect) Connect();
        }

        void OnDisable() => Disconnect();

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Connect()
        {
            if (cts != null) return;
            cts = new CancellationTokenSource();
            string url = config.SocketUrl("sim");
            var token = cts.Token;
            Task.Run(() => RunAsync(url, token));
        }

        public void Disconnect()
        {
            cts?.Cancel();
            cts = null;
            status = (int)Status.Offline;
        }

        /// <summary>
        /// Queues a message for the server. Messages sent while offline are dropped, except reliable ones
        /// (scores), which wait for the next connection.
        /// </summary>
        public void Send(SimMessage message, bool reliable = false)
        {
            string json = message.ToJson();
            if (logMessages) Debug.Log($"[SimConnection] -> {json}");
            if (IsConnected) Enqueue(json);
            else if (reliable) unsentReliable.Add(json);
        }

        void Enqueue(string json)
        {
            outbox.Enqueue(json);
            outboxSignal.Release();
        }

        void Update() => Pump();

        /// <summary>Raises the events for everything received since the last call (public for tests and tools).</summary>
        public void Pump()
        {
            while (inbox.TryDequeue(out var item))
            {
                switch (item.kind)
                {
                    case Kind.Opened:
                        Debug.Log($"[SimConnection] Connected to {config.ActiveUrl} as '{config.DeviceName}'.");
                        foreach (var json in unsentReliable) Enqueue(json);
                        unsentReliable.Clear();
                        Connected?.Invoke();
                        break;
                    case Kind.Closed:
                        Debug.LogWarning($"[SimConnection] Not connected to {config.ActiveUrl}: {item.text}. Retrying.");
                        Disconnected?.Invoke();
                        break;
                    default:
                        Dispatch(item.text);
                        break;
                }
            }
        }

        void Dispatch(string json)
        {
            if (logMessages) Debug.Log($"[SimConnection] <- {json}");
            SimMessage head;
            try { head = JsonUtility.FromJson<SimMessage>(json); }
            catch (Exception e) { Debug.LogWarning($"[SimConnection] Unreadable message ({e.Message}): {json}"); return; }
            switch (head?.type)
            {
                case MessageType.Hello: HelloReceived?.Invoke(JsonUtility.FromJson<HelloMessage>(json)); break;
                case MessageType.Nav: NavReceived?.Invoke(JsonUtility.FromJson<NavMessage>(json).key); break;
                case MessageType.Club: ClubReceived?.Invoke(JsonUtility.FromJson<ClubMessage>(json).club); break;
                case MessageType.Aim: AimReceived?.Invoke(JsonUtility.FromJson<AimMessage>(json).delta); break;
                case MessageType.AimReset: AimResetReceived?.Invoke(); break;
                case MessageType.Mulligan: MulliganReceived?.Invoke(); break;
                case MessageType.Skip: SkipReceived?.Invoke(); break;
                case MessageType.Shot:
                    if (RemoteShotMessage.TryParse(json, out var shot, out var error)) ShotReceived?.Invoke(shot);
                    else Debug.LogWarning($"[SimConnection] Ignored shot: {error}");
                    break;
                case MessageType.GameStarted: GameStarted?.Invoke(JsonUtility.FromJson<GameMessage>(json).game); break;
                case MessageType.Scorecard: ScorecardReceived?.Invoke(JsonUtility.FromJson<GameMessage>(json).game); break;
                case MessageType.GameFinished: GameFinished?.Invoke(JsonUtility.FromJson<GameMessage>(json).game); break;
                case MessageType.Error:
                    string message = JsonUtility.FromJson<ErrorMessage>(json).message;
                    Debug.LogWarning($"[SimConnection] Server error: {message}");
                    ServerError?.Invoke(message);
                    break;
            }
        }

        // ---- background ----

        async Task RunAsync(string url, CancellationToken stop)
        {
            float delay = 1f;
            while (!stop.IsCancellationRequested)
            {
                string reason = "closed";
                using (var socket = new ClientWebSocket())
                using (var connection = CancellationTokenSource.CreateLinkedTokenSource(stop))
                using (connection.Token.Register(socket.Abort))
                {
                    try
                    {
                        status = (int)Status.Connecting;
                        await socket.ConnectAsync(new Uri(url), connection.Token);
                        while (outbox.TryDequeue(out _)) { } // stale updates from the last connection
                        Interlocked.Exchange(ref lastReceivedTicks, DateTime.UtcNow.Ticks);
                        status = (int)Status.Connected;
                        inbox.Enqueue((Kind.Opened, null));
                        delay = 1f;
                        var receiving = ReceiveLoop(socket, connection);
                        var sending = SendLoop(socket, connection.Token);
                        var first = await Task.WhenAny(receiving, sending);
                        connection.Cancel();
                        reason = first.Exception?.GetBaseException().Message ?? "connection closed";
                        try { await Task.WhenAll(receiving, sending); }
                        catch (Exception) { /* the other loop was cancelled */ }
                    }
                    catch (Exception e) { reason = e.GetBaseException().Message; }
                }
                bool wasConnected = status == (int)Status.Connected;
                status = (int)Status.Offline;
                if (stop.IsCancellationRequested) return;
                if (wasConnected || delay <= 1f) inbox.Enqueue((Kind.Closed, reason)); // log once per outage
                try { await Task.Delay(TimeSpan.FromSeconds(delay), stop); }
                catch (OperationCanceledException) { return; }
                delay = Mathf.Min(delay * 2f, config.maxReconnectDelay);
            }
        }

        async Task ReceiveLoop(ClientWebSocket socket, CancellationTokenSource connection)
        {
            var buffer = new byte[ReceiveBuffer];
            using var message = new MemoryStream();
            while (!connection.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), connection.Token);
                if (result.MessageType == WebSocketMessageType.Close) return;
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;
                Interlocked.Exchange(ref lastReceivedTicks, DateTime.UtcNow.Ticks);
                inbox.Enqueue((Kind.Message, Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length)));
                message.SetLength(0);
            }
        }

        /// <summary>Sends queued messages, pings when idle, and gives up when the server has gone quiet.</summary>
        async Task SendLoop(ClientWebSocket socket, CancellationToken token)
        {
            var interval = TimeSpan.FromSeconds(Mathf.Max(1f, config.pingInterval));
            string ping = new SimMessage(MessageType.Ping).ToJson();
            while (!token.IsCancellationRequested)
            {
                bool signalled = await outboxSignal.WaitAsync(interval, token);
                if (!signalled)
                {
                    var silent = DateTime.UtcNow - new DateTime(Interlocked.Read(ref lastReceivedTicks));
                    if (silent > interval + interval + interval) throw new TimeoutException("server stopped answering");
                    await SendText(socket, ping, token);
                    continue;
                }
                if (outbox.TryDequeue(out var json)) await SendText(socket, json, token);
            }
        }

        static Task SendText(ClientWebSocket socket, string json, CancellationToken token) =>
            socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, token);
    }
}
