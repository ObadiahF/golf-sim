// Dev checks for rooms (not compiled into the project), run with the Unity CLI:
//   unity command run_script --file Tools/unity_scripts/RoomsCheck.cs --entry RoomsCheck.<Entry>
//   Code       (Edit mode) the room code: 5 characters from SimRoom.Alphabet, kept in PlayerPrefs (same on every read),
//              normalizing / validating codes like the server; the install id: valid and kept too
//   Url        (Edit mode) the sim's WebSocket URL carries room and id, escaped; a blank room / id leaves them out
//   Rejection  (Edit mode) which server errors turn the sim away (and so wait SimConnection.RejectedRetryDelay)
//   Menu       (Edit mode) the main menu's and the pause menu's room badge: "Room CODE" and the status line
//   Live       (Play mode) the connection's room, state and rejection, and what both badges say right now
using System.Linq;
using System.Text;
using GolfSim.Game;
using GolfSim.Net;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class RoomsCheck
{
    const string Ui = "Assets/GolfSim/Game/UI/";

    static StringBuilder sb;
    static bool ok;

    static void Check(bool good, string what)
    {
        ok &= good;
        sb.AppendLine($"{(good ? "PASS" : "FAIL")} {what}");
    }

    static string Begin() { sb = new StringBuilder(); ok = true; return null; }
    static string End() => (ok ? "ALL PASS\n" : "SOME FAILED\n") + sb;

    public static string Code()
    {
        Begin();
        string code = SimRoom.Code, again = SimRoom.Code;
        Check(code.Length == SimRoom.CodeLength && code.All(c => SimRoom.Alphabet.IndexOf(c) >= 0), $"code {code}: {SimRoom.CodeLength} characters from the alphabet");
        Check(code == again && PlayerPrefs.GetString("GolfSim.Room.Code") == code, "the code is kept in PlayerPrefs (same on every read)");
        Check(SimRoom.IsValid(code), "the sim's own code is a valid room code");
        string id = SimRoom.InstallId;
        Check(SimRoom.IsValidId(id) && id.Length >= 16 && id == SimRoom.InstallId, $"install id {id}: valid, 16+ characters, kept");

        var fresh = Enumerable.Range(0, 2000).Select(_ => SimRoom.NewCode()).ToArray();
        Check(fresh.All(c => c.Length == 5 && c.All(ch => SimRoom.Alphabet.IndexOf(ch) >= 0)), "2000 new codes only use the alphabet (no 0/O/1/I/L)");
        Check(fresh.Distinct().Count() > 1990, $"2000 new codes: {fresh.Distinct().Count()} different");
        Check(fresh.SelectMany(c => c).Distinct().Count() == SimRoom.Alphabet.Length, "every alphabet character turns up");

        foreach (var (input, normalized, valid) in new[]
                 {
                     (" k7qf ", "K7QF", true), ("abcd1234", "ABCD1234", true), ("ABC", "ABC", false), ("ABCDEFGHI", "ABCDEFGHI", false),
                     ("AB-CD", "AB-CD", false), ("", "", false), (null, "", false), ("äBCD", "ÄBCD", false),
                 })
        {
            string n = SimRoom.Normalize(input);
            Check(n == normalized && SimRoom.IsValid(n) == valid, $"'{input}' -> '{n}' {(SimRoom.IsValid(n) ? "valid" : "invalid")}");
        }
        foreach (var (input, valid) in new[] { ("abc_DEF-123", true), ("", false), (new string('a', 64), true), (new string('a', 65), false), ("a b", false), ("a/b", false) })
            Check(SimRoom.IsValidId(input) == valid, $"id '{(input.Length > 20 ? input.Substring(0, 20) + "..." : input)}' {(valid ? "valid" : "invalid")}");
        return End();
    }

    public static string Url()
    {
        Begin();
        var config = ScriptableObject.CreateInstance<ServerConfig>();
        config.serverUrl = "wss://example.test/";
        config.deviceName = "Den TV & Co";
        string sim = config.SocketUrl("sim", SimRoom.Code, SimRoom.InstallId);
        Check(sim.EndsWith($"&role=sim&name=Den%20TV%20%26%20Co&room={SimRoom.Code}&id={SimRoom.InstallId}") && sim.StartsWith("wss://example.test/ws?token="),
            $"sim URL: {sim.Replace(config.Token, "<token>")}");
        string odd = config.SocketUrl("sim", "A B&C", "x/y");
        Check(odd.Contains("&room=A%20B%26C&id=x%2Fy"), $"escaped: {odd.Substring(odd.IndexOf("&room"))}");
        string legacy = config.SocketUrl("remote");
        Check(!legacy.Contains("room=") && !legacy.Contains("&id="), "no room / id: left out (the default room)");
        string source = System.IO.File.ReadAllText("Assets/GolfSim/Net/Runtime/SimConnection.cs");
        Check(source.Contains("Room = SimRoom.Code;") && source.Contains("config.SocketUrl(\"sim\", Room, SimRoom.InstallId)"),
            "SimConnection.Connect joins SimRoom.Code as SimRoom.InstallId");
        Object.DestroyImmediate(config);
        return End();
    }

    public static string Rejection()
    {
        Begin();
        foreach (var (json, expected) in new[]
                 {
                     ("{\"type\":\"error\",\"message\":\"Another sim is already connected to room K7QF2\"}", "Another sim is already connected to room K7QF2"),
                     ("{\"type\":\"error\",\"message\":\"Another sim is already connected to this server\"}", "Another sim is already connected to this server"),
                     ("{\"type\":\"error\",\"message\":\"Replaced by a new connection from the same sim\"}", "Replaced by a new connection from the same sim"),
                     ("{\"type\":\"error\",\"message\":\"Unknown message type\"}", null),
                     ("{\"type\":\"hello\",\"role\":\"sim\",\"room\":\"K7QF2\"}", null),
                     ("{\"type\":\"state\",\"message\":\"Another sim is already connected\"}", null),
                     ("not json error", null),
                 })
        {
            string got = SimConnection.RejectionIn(json);
            Check(got == expected, $"{json} -> {got ?? "not a rejection"}");
        }
        Check(Mathf.Approximately(SimConnection.RejectedRetryDelay, 30f), $"a turned-away sim retries every {SimConnection.RejectedRetryDelay:0} s");
        var hello = JsonUtility.FromJson<HelloMessage>("{\"type\":\"hello\",\"role\":\"sim\",\"room\":\"K7QF2\",\"simConnected\":true}");
        Check(hello.room == "K7QF2", $"hello.room parsed: {hello.room}");
        return End();
    }

    public static string Menu()
    {
        Begin();
        Check(RoomBadge.CodeText == $"Room {SimRoom.Code}", $"code line: {RoomBadge.CodeText}");
        Check(RoomBadge.StatusText(null) == "Offline", "status without a connection: Offline");
        foreach (var uxml in new[] { "MainMenu.uxml", "HomeMenu.uxml" })
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Ui + uxml).CloneTree();
            new RoomBadge(tree);
            var code = tree.Q<Label>("room-code");
            var status = tree.Q<Label>("room-status");
            Check(code?.text == RoomBadge.CodeText && status?.text == RoomBadge.StatusText(SimConnection.Instance),
                $"{uxml}: '{code?.text}' / '{status?.text}'");
        }
        return End();
    }

    public static string Live()
    {
        var c = SimConnection.Instance;
        if (!c) return "no SimConnection (enter Play mode)";
        string Badge(string name)
        {
            var doc = Object.FindObjectsByType<UIDocument>().FirstOrDefault(d => d.rootVisualElement?.Q<Label>("room-code") != null && d.name.Contains(name));
            return doc ? $"{doc.rootVisualElement.Q<Label>("room-code").text} / {doc.rootVisualElement.Q<Label>("room-status").text}" : "(not open)";
        }
        return $"url {c.config.ActiveUrl}, room {c.Room}, state {c.State}, rejection {c.Rejection ?? "none"}\n" +
               $"main menu: {Badge("Main")}\npause menu: {Badge("HOME")}";
    }
}
