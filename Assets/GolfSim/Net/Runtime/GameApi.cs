using System;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace GolfSim.Net
{
    /// <summary>
    /// The game server's REST API (read-only use from the sim): GET with the bearer token from ServerConfig.
    /// Run the coroutines from any MonoBehaviour. See Game-server/docs/PROTOCOL.md for the shapes.
    /// </summary>
    public static class GameApi
    {
        /// <summary>JSON null numbers are read as this (JsonUtility would read 0, which is a valid handicap).</summary>
        public const float Missing = -99999f;

        static readonly Regex NullNumber = new Regex(@"(?<!\\)""([A-Za-z]+)""\s*:\s*null", RegexOptions.Compiled);

        public static bool Has(float value) => value > Missing + 1f;

        /// <summary>GET /api/players: everyone's stats, sorted by name.</summary>
        public static IEnumerator Players(Action<PlayerStats[]> done, Action<string> failed) =>
            Get("/api/players", json => done(JsonUtility.FromJson<PlayerList>("{\"items\":" + WithMissing(json) + "}").items), failed);

        public static IEnumerator Get(string path, Action<string> done, Action<string> failed)
        {
            var config = ServerConfig.Load();
            using var request = UnityWebRequest.Get(config.HttpUrl + path);
            request.SetRequestHeader("Authorization", "Bearer " + config.token);
            request.timeout = 8;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success) done(request.downloadHandler.text);
            else failed($"{config.HttpUrl}: {(request.responseCode > 0 ? $"HTTP {request.responseCode}" : request.error)}");
        }

        /// <summary>Replaces "field": null with the Missing sentinel so nullable numbers survive JsonUtility.</summary>
        static string WithMissing(string json) => NullNumber.Replace(json, m => $"\"{m.Groups[1].Value}\":{Missing}");

        [Serializable] class PlayerList { public PlayerStats[] items = new PlayerStats[0]; }
    }

    /// <summary>One player's history (GET /api/players). Totals, averages, bests and handicap count finished rounds.</summary>
    [Serializable]
    public class PlayerStats
    {
        public string name;
        public int gamesPlayed, finishedRounds, wins;
        /// <summary>GameApi.Missing when there's no finished round (handicap: fewer than 3).</summary>
        public float bestTotal, averageTotal, averageToPar, recentAverageToPar, handicap;
        public HoleTallies holes = new HoleTallies();
        public string lastPlayedAt;
    }

    /// <summary>How a player's holes went, across every game.</summary>
    [Serializable]
    public class HoleTallies
    {
        public int played, holesInOne, eagles, birdies, pars, bogeys, doubleBogeysOrWorse;
    }
}
