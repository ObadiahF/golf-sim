using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GolfSim.Ball
{
    /// <summary>
    /// Receives shots from the SwingRemote phone app over UDP (port 4242) and hits the ball with them.
    /// A background thread only reads datagrams into a queue; parsing, hitting and replying happen on
    /// the main thread in Update. Replies: "hello" to discovery, "ack" (ok / busy / error) to each
    /// shot, and "result" (carry, total, lie) when the ball comes to rest. If a ShotPanel sits on the
    /// same object, shots go through it so wind and the follow camera behave like its Hit button.
    /// Attaches itself to the scene's GolfBall at runtime; nothing to wire up.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GolfBall))]
    public class UdpShotReceiver : MonoBehaviour
    {
        public const int DefaultPort = 4242;
        const int MaxQueued = 64;

        [Tooltip("UDP port to listen on (the phone app sends to 4242).")]
        public int port = DefaultPort;
        [Tooltip("Name shown on the phone; blank uses the computer's name.")]
        public string displayName = "";
        public bool logMessages = true;

        public bool Listening => udp != null;

        readonly ConcurrentQueue<(string json, IPEndPoint from)> inbox = new ConcurrentQueue<(string, IPEndPoint)>();
        UdpClient udp;
        Thread thread;
        volatile bool running;
        GolfBall ball;
        string hostName;

        // The shot in flight, which gets the result when the ball stops (a busy shot doesn't replace it).
        int resultId;
        IPEndPoint resultTo;

        // ---- auto-attach ----

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallAutoAttach()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded; // idempotent when domain reload is disabled
            SceneManager.sceneLoaded += OnSceneLoaded;
            AttachToBall();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AttachToBall();

        /// <summary>Adds a receiver to the scene's ball unless one already exists (one port, one listener).</summary>
        static void AttachToBall()
        {
            if (FindAnyObjectByType<UdpShotReceiver>()) return;
            var target = FindAnyObjectByType<GolfBall>();
            if (target) target.gameObject.AddComponent<UdpShotReceiver>();
        }

        // ---- lifecycle ----

        void OnEnable()
        {
            ball = GetComponent<GolfBall>();
            hostName = string.IsNullOrWhiteSpace(displayName) ? SystemInfo.deviceName : displayName;
            ball.ShotFinished += OnShotFinished;
            Open();
        }

        void OnDisable()
        {
            if (ball) ball.ShotFinished -= OnShotFinished;
            Close();
        }

        void OnDestroy() => Close();

        void OnApplicationQuit() => Close();

        void Open()
        {
            if (udp != null) return;
            try
            {
                udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
                IgnoreConnectionResets(udp);
            }
            catch (SocketException e)
            {
                udp = null;
                Debug.LogWarning($"[UdpShotReceiver] Can't listen on UDP {port} ({e.SocketErrorCode}). Is another sim running?");
                return;
            }
            running = true;
            var client = udp;
            thread = new Thread(() => ReceiveLoop(client)) { IsBackground = true, Name = "UdpShotReceiver" };
            thread.Start();
            if (logMessages) Debug.Log($"[UdpShotReceiver] Listening for SwingRemote on UDP {port} as '{hostName}'.");
        }

        void Close()
        {
            running = false;
            var client = udp;
            udp = null;
            client?.Close(); // unblocks Receive on the thread
            if (thread != null && thread != Thread.CurrentThread) thread.Join(500);
            thread = null;
            while (inbox.TryDequeue(out _)) { }
        }

        /// <summary>
        /// Windows reports an ICMP "port unreachable" from an earlier send (phone app closed) as a
        /// reset on the next Receive; switch that off so the listener keeps running.
        /// </summary>
        static void IgnoreConnectionResets(UdpClient client)
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor) return;
            const int SioUdpConnReset = -1744830452;
            try { client.Client.IOControl(SioUdpConnReset, new byte[] { 0 }, null); }
            catch (Exception) { /* not supported: the catch in ReceiveLoop still copes */ }
        }

        // ---- background thread: read only ----

        void ReceiveLoop(UdpClient client)
        {
            while (running)
            {
                try
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    var bytes = client.Receive(ref from);
                    if (inbox.Count < MaxQueued) inbox.Enqueue((Encoding.UTF8.GetString(bytes), from));
                }
                catch (SocketException) when (running) { /* connection reset or similar: keep listening */ }
                catch (SocketException) { break; }
                catch (ObjectDisposedException) { break; }
            }
        }

        // ---- main thread ----

        void Update() => ProcessInbox();

        /// <summary>Handles everything received since the last frame (public for tests and tools).</summary>
        public void ProcessInbox()
        {
            while (inbox.TryDequeue(out var item)) Handle(item.json, item.from);
        }

        void Handle(string json, IPEndPoint from)
        {
            if (!RemoteShotMessage.TryParse(json, out var msg, out var error))
            {
                if (logMessages) Debug.LogWarning($"[UdpShotReceiver] Ignored datagram from {from.Address}: {error}");
                Send(RemoteReply.Ack(msg?.id ?? 0, "error", error), from);
                return;
            }
            if (msg.IsDiscover) Send(RemoteReply.Hello(hostName, ball.InMotion), from);
            else HandleShot(msg, from);
        }

        void HandleShot(RemoteShotMessage msg, IPEndPoint from)
        {
            var ack = Shots.Submit(msg, $"UDP {from.Address}", out bool duplicate, ball);
            if (ack.status == "ok" && !duplicate) { resultId = msg.id; resultTo = from; }
            Send(ack, from);
        }

        void OnShotFinished(GolfBall finished)
        {
            if (resultTo == null) return;
            Send(RemoteReply.Result(resultId, finished.Result, finished.Status), resultTo);
            resultTo = null;
        }

        void Send(RemoteReply reply, IPEndPoint to)
        {
            if (udp == null || to == null) return;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(reply.ToJson());
                udp.Send(bytes, bytes.Length, to);
            }
            catch (Exception e) when (e is SocketException || e is ObjectDisposedException)
            {
                if (logMessages) Debug.LogWarning($"[UdpShotReceiver] Reply to {to} failed: {e.Message}");
            }
        }
    }
}
