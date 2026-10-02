// Dev helper, run from the shell with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/UdpShotReceiverCheck.cs --entry UdpShotReceiverCheck.Run
// Checks SwingRemote message parsing, then runs a UdpShotReceiver in edit mode on a test port
// (42420, so it never clashes with a Play session on 4242) on a hidden, unsaved ball and talks to
// it over loopback like the phone would: discover, shot, retry, busy, result. The ball plays on a
// hidden in-memory flat fairway, so the open scene is never touched.
using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using GolfSim.Ball;
using GolfSim.Course;
using UnityEngine;

public static class UdpShotReceiverCheck
{
    const int Port = 42420;
    static StringBuilder log;
    static int failures;

    public static string Run()
    {
        log = new StringBuilder();
        failures = 0;
        CheckParsing();
        CheckRoundTrip();
        log.AppendLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
        return log.ToString();
    }

    static void Check(string name, bool ok, string detail = "")
    {
        if (!ok) failures++;
        log.AppendLine($"{(ok ? "pass" : "FAIL")}  {name}{(detail.Length > 0 ? "  " + detail : "")}");
    }

    static void CheckParsing()
    {
        bool ok = RemoteShotMessage.TryParse("{\"speed\":60,\"launch\":12,\"azimuth\":-1.5,\"back\":2600,\"side\":-300}", out var v1, out _);
        Check("v1 shot (no type) parses", ok && v1.IsShot && v1.ToShotData().ballSpeed == 60f && v1.ToShotData().sidespin == -300f);

        ok = RemoteShotMessage.TryParse("{\"v\":2,\"type\":\"shot\",\"id\":12,\"club\":\"7 Iron\",\"speed\":45.5,\"launch\":17,\"azimuth\":2.25,\"back\":6500,\"side\":540}", out var v2, out _);
        Check("v2 shot parses", ok && v2.id == 12 && v2.club == "7 Iron" && Mathf.Approximately(v2.ToShotData().launchDirection, 2.25f));

        ok = RemoteShotMessage.TryParse("{\"v\":2,\"type\":\"discover\",\"app\":\"SwingRemote\"}", out var discover, out _);
        Check("discover parses", ok && discover.IsDiscover);

        Check("garbage rejected", !RemoteShotMessage.TryParse("hello", out _, out _));
        Check("zero speed rejected", !RemoteShotMessage.TryParse("{\"speed\":0}", out _, out _));
        Check("unknown type rejected", !RemoteShotMessage.TryParse("{\"type\":\"dance\",\"speed\":3}", out _, out _));
        RemoteShotMessage.TryParse("{\"speed\":500,\"launch\":5,\"azimuth\":90}", out var wild, out _);
        Check("wild values clamped", wild.ToShotData().ballSpeed == 100f && wild.ToShotData().launchDirection == 45f);
    }

    static void CheckRoundTrip()
    {
        var go = new GameObject("UdpShotReceiverCheck") { hideFlags = HideFlags.HideAndDontSave };
        var hole = CreateFlatHole();
        try
        {
            var ball = go.AddComponent<GolfBall>();
            var panel = go.AddComponent<ShotPanel>();
            var rx = go.AddComponent<UdpShotReceiver>();
            rx.port = Port;
            rx.logMessages = false;
            typeof(GolfBall).GetField("hole", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ball, hole);
            Call(panel, "Awake");
            Call(rx, "OnEnable");
            Check("receiver listening", rx.Listening);
            ball.ResetToTee();
            bool haveHole = ball.Status == BallStatus.Ready;
            log.AppendLine(haveHole ? "(hole found: ball on the tee)" : "(no hole in the open scene: expecting error acks)");

            using (var phone = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            {
                phone.Client.ReceiveTimeout = 1000;
                var hello = Exchange(phone, rx, "{\"v\":2,\"type\":\"discover\"}");
                Check("discover -> hello", hello != null && hello.type == "hello" && !string.IsNullOrEmpty(hello.name), Json(hello));

                const string shot = "{\"v\":2,\"type\":\"shot\",\"id\":501,\"club\":\"Driver\",\"speed\":65,\"launch\":12,\"azimuth\":1,\"back\":2600,\"side\":200}";
                var ack = Exchange(phone, rx, shot);
                string expected = haveHole ? "ok" : "error";
                Check($"shot -> ack {expected}", ack != null && ack.type == "ack" && ack.id == 501 && ack.status == expected, Json(ack));

                var again = Exchange(phone, rx, shot);
                Check("retry of same id -> same ack", again != null && again.id == 501 && again.status == expected, Json(again));

                if (haveHole)
                {
                    var busy = Exchange(phone, rx, shot.Replace("501", "502"));
                    Check("shot while ball moving -> busy", busy != null && busy.id == 502 && busy.status == "busy", Json(busy));

                    for (int i = 0; i < 600 && ball.InMotion; i++) ball.Advance(0.1f);
                    var result = Receive(phone);
                    Check("ball stopped -> result", result != null && result.type == "result" && result.id == 501 && result.carry > 50f, Json(result));
                }

                var bad = Exchange(phone, rx, "{\"type\":\"dance\"}");
                Check("bad datagram -> error ack", bad != null && bad.status == "error", Json(bad));
            }

            Call(rx, "OnDisable");
            Check("receiver closed", !rx.Listening);
            // The port must be free again (no leaked socket or thread).
            using (var probe = new UdpClient(new IPEndPoint(IPAddress.Any, Port))) Check("port released", true);
        }
        catch (Exception e)
        {
            Check("round trip threw", false, e.ToString());
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
            var terrain = hole.GetComponentInChildren<Terrain>();
            UnityEngine.Object.DestroyImmediate(terrain.terrainData.terrainLayers[0]);
            UnityEngine.Object.DestroyImmediate(terrain.terrainData);
            UnityEngine.Object.DestroyImmediate(hole.gameObject);
        }
    }

    /// <summary>A 100 x 500 m flat fairway with the tee near one end and the pin near the other.</summary>
    static HoleInfo CreateFlatHole()
    {
        var root = new GameObject("UdpShotReceiverCheckHole") { hideFlags = HideFlags.HideAndDontSave };
        var hole = root.AddComponent<HoleInfo>();
        hole.teePosition = new Vector3(50f, 0f, 20f);
        hole.pinPosition = new Vector3(50f, 0f, 480f);
        hole.terrainLayerSurfaces = new[] { "fairway" };
        var data = new TerrainData { hideFlags = HideFlags.HideAndDontSave, heightmapResolution = 33, alphamapResolution = 16 };
        data.size = new Vector3(100f, 10f, 500f);
        data.terrainLayers = new[] { new TerrainLayer { name = "fairway", hideFlags = HideFlags.HideAndDontSave } };
        var alpha = new float[16, 16, 1];
        for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++) alpha[z, x, 0] = 1f;
        data.SetAlphamaps(0, 0, alpha);
        var terrainGo = new GameObject("Terrain") { hideFlags = HideFlags.HideAndDontSave };
        terrainGo.transform.SetParent(root.transform, false);
        terrainGo.AddComponent<Terrain>().terrainData = data;
        return hole;
    }

    static RemoteReply Exchange(UdpClient phone, UdpShotReceiver rx, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        phone.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, Port));
        for (int i = 0; i < 20; i++) // the receive thread needs a moment
        {
            Thread.Sleep(10);
            rx.ProcessInbox();
            if (phone.Available > 0) break;
        }
        return Receive(phone);
    }

    static RemoteReply Receive(UdpClient phone)
    {
        try
        {
            var from = new IPEndPoint(IPAddress.Any, 0);
            return JsonUtility.FromJson<RemoteReply>(Encoding.UTF8.GetString(phone.Receive(ref from)));
        }
        catch (SocketException) { return null; }
    }

    static string Json(RemoteReply r) => r == null ? "(no reply)" : r.ToJson();

    static void Call(object target, string method) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(target, null);
}
