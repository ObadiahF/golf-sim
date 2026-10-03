// Dev helper, run from the shell with the Unity CLI (not compiled into the project), edit mode, against a LOCAL trainer
// (docker compose with TRAINER_GAME_KEY set) that has a pool and a liked hole:
//   echo "http://127.0.0.1:8865,<game key>" > Temp/randomholes_args.txt
//   unity command run_script --file Tools/unity_scripts/RandomHolesCheck.cs --entry RandomHolesCheck.Start
//   unity command run_script --file Tools/unity_scripts/RandomHolesCheck.cs --entry RandomHolesCheck.Result   (until not WAIT)
// TrainerHoles.Fetch in five steps: a random round for a server game, a second random round (the first one's holes
// sent as `exclude`, so they come last), the server game again (its saved list, no network), the trainer down (a
// random pick from the cached packages) and a top-rated round. The Editor only ticks while frontmost. Result puts
// back what the run changed: the cache folders it added, holes/recent.json, holes/rounds/last.json, the config.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GolfSim.Net;
using UnityEditor;
using UnityEngine;

public static class RandomHolesCheck
{
    const string Out = "Temp/randomholes_result.txt", Key = "rndcheck-game";
    static string RecentPath => Path.Combine(TrainerHoles.CacheFolder, "recent.json");
    static string LastPath => Path.Combine(TrainerHoles.CacheFolder, "rounds", "last.json");

    public static string Start()
    {
        var a = File.ReadAllText("Temp/randomholes_args.txt").Trim().Split(',');
        var config = ServerConfig.Load();
        var before = new HashSet<string>(Directory.Exists(TrainerHoles.CacheFolder) ? Directory.GetDirectories(TrainerHoles.CacheFolder) : new string[0]);
        string recent = File.Exists(RecentPath) ? File.ReadAllText(RecentPath) : null;
        string last = File.Exists(LastPath) ? File.ReadAllText(LastPath) : null;
        string url = config.trainerUrl, env = Environment.GetEnvironmentVariable("GOLF_TRAINER_KEY");
        var selection = config.holeSelection;
        if (File.Exists(Out)) File.Delete(Out);
        Environment.SetEnvironmentVariable("GOLF_TRAINER_KEY", a[1]);
        config.trainerUrl = a[0];

        var log = new StringBuilder();
        var runs = new List<TrainerHoles.Result>();
        IEnumerator Steps()
        {
            config.holeSelection = ServerConfig.HoleSelection.Random;
            foreach (var key in new[] { Key, null, Key })
                yield return TrainerHoles.Fetch(config, 9, key, null, () => false, runs.Add);
            config.trainerUrl = "http://127.0.0.1:1"; // nobody there
            yield return TrainerHoles.Fetch(config, 9, null, null, () => false, runs.Add);
            config.trainerUrl = a[0];
            config.holeSelection = ServerConfig.HoleSelection.TopRated;
            yield return TrainerHoles.Fetch(config, 9, null, null, () => false, runs.Add);
        }

        void Restore()
        {
            foreach (var dir in Directory.GetDirectories(TrainerHoles.CacheFolder).Where(d => !before.Contains(d)))
                Directory.Delete(dir, true);
            Put(RecentPath, recent);
            Put(LastPath, last);
            File.Delete(Path.Combine(TrainerHoles.CacheFolder, "rounds", Key + ".json"));
            config.trainerUrl = url;
            config.holeSelection = selection;
            Environment.SetEnvironmentVariable("GOLF_TRAINER_KEY", env);
        }

        var routine = SafeCoroutine.Run(Steps(), e => log.AppendLine($"FAIL exception {e}"));
        EditorApplication.CallbackFunction tick = null;
        tick = () =>
        {
            bool more;
            try { more = routine.MoveNext(); }
            catch (Exception e) { log.AppendLine($"FAIL {e}"); more = false; }
            if (more) return;
            EditorApplication.update -= tick;
            try { log.Append(Judge(runs)); }
            finally { Restore(); }
            File.WriteAllText(Out, log.ToString());
        };
        EditorApplication.update += tick;
        return "started; run RandomHolesCheck.Result until it is not WAIT";
    }

    public static string Result() => File.Exists(Out) ? File.ReadAllText(Out) : "WAIT";

    static string Judge(List<TrainerHoles.Result> r)
    {
        if (r.Count != 5) return $"FAIL only {r.Count} of 5 fetches finished: {string.Join("; ", r.Select(x => x.error))}";
        List<string> Ids(TrainerHoles.Result x) => x.holes?.ConvertAll(h => h.hole.id) ?? new List<string>();
        var sb = new StringBuilder();
        void Check(bool ok, string what, TrainerHoles.Result x) =>
            sb.AppendLine($"{(ok ? "PASS" : "FAIL")} {what}: {string.Join(" ", Ids(x))}{(x.error != null ? $" [{x.error}]" : "")}" +
                          $" (random {x.random}, fromCache {x.fromCache}, offline {x.offline})");
        var first = Ids(r[0]);
        Check(first.Count == 9 && first.Distinct().Count() == 9 && r[0].random && !r[0].fromCache, "random round, 9 different holes", r[0]);
        var second = Ids(r[1]);
        int freshFirst = second.TakeWhile(id => !first.Contains(id)).Count();
        Check(second.Count == 9 && second.Distinct().Count() == 9 && freshFirst == second.Count(id => !first.Contains(id)),
              $"second round: the {freshFirst} holes not just played come first", r[1]);
        Check(Ids(r[2]).SequenceEqual(first) && r[2].fromCache && !r[2].offline, "server game again: same holes, saved list", r[2]);
        var offline = Ids(r[3]);
        Check(offline.Count > 0 && offline.Distinct().Count() == offline.Count && r[3].offline && r[3].random,
              "trainer down: random pick from the cache", r[3]);
        Check(r[4].holes != null && !r[4].random && !r[4].fromCache, "top-rated round", r[4]);
        sb.AppendLine($"recent.json kept {TrainerHoles.RecentIds().Count} ids (cap 30)");
        return sb.ToString();
    }

    static void Put(string path, string text)
    {
        if (text != null) File.WriteAllText(path, text);
        else if (File.Exists(path)) File.Delete(path);
    }
}
