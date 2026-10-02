using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GolfSim.Ball
{
    /// <summary>
    /// The one path every shot takes: phone shots over UDP (UdpShotReceiver) or over the game server's
    /// WebSocket, and the on-screen ShotPanel. A game mode can close the Gate (e.g. a round between
    /// turns); retries of the same shot id are answered with the first ack instead of hitting twice.
    /// </summary>
    public static class Shots
    {
        const float DuplicateWindow = 10f; // s; the phone retries unacknowledged shots with the same id

        /// <summary>Returns null when a shot may be hit now, otherwise the reason it can't. Set by game modes.</summary>
        public static Func<string> Gate;

        /// <summary>A remote shot passed the gate and is about to be hit (e.g. to show the club it was hit with).</summary>
        public static event Action<RemoteShotMessage> Accepted;

        static int lastId;
        static float lastTime;
        static RemoteReply lastAck;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Gate = null;
            Accepted = null;
            lastId = 0;
            lastAck = null;
        }

        /// <summary>Why a shot can't be hit right now, or null if it can.</summary>
        public static string Blocked => Gate?.Invoke();

        /// <summary>
        /// Hits a remote shot with this ball (default: the scene's) and returns the ack (ok / busy / error). A retry
        /// of the last shot id within 10 s gets the original ack again (duplicate = true) and is not hit twice.
        /// </summary>
        public static RemoteReply Submit(RemoteShotMessage msg, string source, out bool duplicate, GolfBall ball = null)
        {
            duplicate = msg.id != 0 && msg.id == lastId && lastAck != null && Time.realtimeSinceStartup - lastTime < DuplicateWindow;
            if (duplicate) return lastAck;
            var ack = Hit(msg, source, ball ? ball : Object.FindAnyObjectByType<GolfBall>());
            lastId = msg.id;
            lastTime = Time.realtimeSinceStartup;
            lastAck = ack;
            return ack;
        }

        static RemoteReply Hit(RemoteShotMessage msg, string source, GolfBall ball)
        {
            if (!ball) return RemoteReply.Ack(msg.id, "error", "No hole loaded in the sim");
            if (ball.InMotion) return RemoteReply.Ack(msg.id, "busy", "Ball is still moving");
            string blocked = Blocked;
            if (blocked != null) return RemoteReply.Ack(msg.id, "error", blocked);

            Accepted?.Invoke(msg);
            var shot = msg.ToShotData();
            var panel = ball.GetComponent<ShotPanel>();
            if (panel && panel.isActiveAndEnabled) panel.Hit(shot);
            else ball.Hit(shot);
            if (!ball.InMotion) return RemoteReply.Ack(msg.id, "error", "No hole loaded in the sim");

            Debug.Log($"[Shots] {source} {(string.IsNullOrEmpty(msg.club) ? "shot" : msg.club)} #{msg.id}: {shot.BallSpeedMph:0} mph, " +
                      $"launch {shot.launchAngle:0.0}°, dir {shot.launchDirection:+0.0;-0.0}°, spin {shot.backspin:0}/{shot.sidespin:+0;-0} rpm");
            return RemoteReply.Ack(msg.id, "ok");
        }
    }
}
