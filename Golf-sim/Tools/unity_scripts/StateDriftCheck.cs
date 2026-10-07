// Dev check (Play mode, against a LOCAL game server), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/StateDriftCheck.cs --entry StateDriftCheck.Start
//   ... play (rounds, putts, replays, the pause menu) with the Editor in front so it ticks ...
//   unity command run_script --file Tools/unity_scripts/StateDriftCheck.cs --entry StateDriftCheck.Report
// The phones only learn what the TV shows from "state", sent when RoundDirector.PublishState sees the JSON change. If
// something changes the TV without a PublishState, the phones keep the old state (e.g. the replay on the TV while the
// phone still shows the club wheel). Start watches every Editor tick for the state the director would build now
// differing from the last one it sent, and Report lists each drift that lasted over MinSeconds: which fields, how long.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using GolfSim.Game;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class StateDriftCheck
{
    const float MinSeconds = 0.3f;
    static readonly FieldInfo LastState = typeof(RoundDirector).GetField("lastState", BindingFlags.NonPublic | BindingFlags.Instance);
    // Each run_script call compiles its own copy of this class, so what Tick (registered by Start's copy) finds is kept
    // in SessionState for Report's copy to read.
    const string DriftsKey = "StateDriftCheck.drifts", TicksKey = "StateDriftCheck.ticks", OpenKey = "StateDriftCheck.open";
    static string driftFields;
    static float driftSince = -1f;
    static int ticks;

    public static string Start()
    {
#if UNITY_EDITOR
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
#endif
        SessionState.EraseString(DriftsKey);
        SessionState.EraseString(OpenKey);
        SessionState.SetInt(TicksKey, 0);
        driftSince = -1f;
        ticks = 0;
        return "watching";
    }

    public static string Stop()
    {
#if UNITY_EDITOR
        EditorApplication.update -= Tick;
#endif
        return Report();
    }

    public static string Report()
    {
        var drifts = SessionState.GetString(DriftsKey, "").Split('\n').Where(l => l != "").ToList();
        var sb = new StringBuilder($"{SessionState.GetInt(TicksKey, 0)} ticks watched; {drifts.Count} drifts over {MinSeconds} s\n");
        foreach (var d in drifts.Skip(Mathf.Max(0, drifts.Count - 40))) sb.AppendLine(d);
        string open = SessionState.GetString(OpenKey, "");
        if (open != "") sb.AppendLine("ONGOING " + open);
        return sb.ToString();
    }

    static void Tick()
    {
        var d = RoundDirector.Instance;
        if (!Application.isPlaying || !d) return;
        if (++ticks % 30 == 0) SessionState.SetInt(TicksKey, ticks);
        string sent = LastState.GetValue(d) as string;
        if (sent == null) return;
        string now = d.BuildState().ToJson();
        if (now == sent)
        {
            Close();
            return;
        }
        string fields = Diff(JsonUtility.FromJson<Fields>(sent), JsonUtility.FromJson<Fields>(now), sent, now);
        if (driftSince < 0f) { driftSince = Time.realtimeSinceStartup; driftFields = fields; }
        else if (!driftFields.Contains(fields)) driftFields += " | " + fields;
        if (Time.realtimeSinceStartup - driftSince >= MinSeconds)
            SessionState.SetString(OpenKey, $"since t={driftSince:0.0} ({Time.realtimeSinceStartup - driftSince:0.0} s): {driftFields}");
    }

    static void Close()
    {
        if (driftSince < 0f) return;
        float lasted = Time.realtimeSinceStartup - driftSince;
        if (lasted >= MinSeconds)
            SessionState.SetString(DriftsKey, SessionState.GetString(DriftsKey, "") + $"{lasted,5:0.0} s at t={driftSince:0.0}: {driftFields}\n");
        SessionState.EraseString(OpenKey);
        driftSince = -1f;
    }

    // Just enough of StateMessage to name the fields that differ.
    [System.Serializable]
    class Fields { public string screen, waitReason, club, lie, currentPlayer; public bool canShoot, canReplay, putting, mapOpen; public float aim, distanceToPin; public int strokes, hole, wind; }

    static string Diff(Fields a, Fields b, string rawA, string rawB)
    {
        var parts = new List<string>();
        foreach (var f in typeof(Fields).GetFields())
        {
            var va = f.GetValue(a);
            var vb = f.GetValue(b);
            if (!Equals(va, vb)) parts.Add($"{f.Name} {va} -> {vb}");
        }
        return parts.Count > 0 ? string.Join(", ", parts) : "other fields (putting numbers, practice)";
    }
}
