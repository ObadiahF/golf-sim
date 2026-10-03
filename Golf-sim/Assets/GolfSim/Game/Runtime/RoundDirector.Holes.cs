using System;
using System.Collections.Generic;
using GolfSim.Course;
using GolfSim.Net;
using UnityEngine;

namespace GolfSim.Game
{
    // Which holes a round plays: Course Trainer holes (a random pick by default, or the top-rated ones; see
    // ServerConfig.holeSelection and TrainerHoles), downloaded and built at runtime into the hole scene, when
    // available, else the hole scenes listed in CourseRound as built in the editor. While the holes download, an
    // overlay shows the progress over everything and takes every key; Back cancels.
    public partial class RoundDirector
    {
        /// <summary>Local package folders of this round's holes in order, or null to play the scenes as built.</summary>
        List<string> courseHoles;
        /// <summary>The round whose holes are being fetched (null when no fetch is running).</summary>
        Round fetching;

        /// <summary>True while a round's holes are downloading (Play is ignored, the menu is covered).</summary>
        public bool IsFetching => fetching != null;

        /// <summary>Gets the trainer's holes for `forRound` (cached after the first time), then continues.</summary>
        void FetchCourseHoles(Round forRound, Action then)
        {
            courseHoles = null;
            var config = ServerConfig.Load();
            if (!config.useTrainerHoles || !course.themes)
            {
                then();
                return;
            }
            fetching = forRound;
            phase = Phase.Loading;
            PublishState();
            ShowFetchProgress(0, forRound.holeCount, config.holeSelection);
            NavInput.Register(OnFetchNav, NavInput.ModalPriority);
            // A server game keeps its own list, so a resumed game replays the same holes even when the trainer is down.
            string roundKey = forRound.InServerGame ? $"{TrainerHoles.Safe(config.HttpUrl)}-game-{forRound.gameId}" : null;
            StartCoroutine(TrainerHoles.Fetch(config, forRound.holeCount, roundKey,
                (i, n) => { if (fetching == forRound) ShowFetchProgress(i, n, config.holeSelection); },
                () => fetching != forRound,
                result =>
                {
                    if (fetching != forRound) return; // cancelled, or replaced by a newer round
                    StopFetching();
                    if (result.holes != null)
                    {
                        courseHoles = result.holes.ConvertAll(h => h.folder);
                        string which = result.random ? $"{result.holes.Count} random holes" : $"the trainer's top {result.holes.Count} holes";
                        string from = result.offline ? " (offline, from the cache)" : result.fromCache ? " (cached list)" : "";
                        Debug.Log($"[RoundDirector] Playing {which}{from}: " +
                                  string.Join(", ", result.holes.ConvertAll(h => $"#{h.hole.rank} {h.hole.id} ({h.hole.ups}👍 {h.hole.downs}👎)")));
                        if (result.offline)
                            hud?.Toast(result.random ? "Couldn't reach the trainer: playing holes from the cache"
                                                     : "Couldn't reach the trainer: playing the last top holes");
                    }
                    else
                    {
                        Debug.LogWarning($"[RoundDirector] {result.error}; playing the built-in holes instead.");
                        hud?.Toast("Couldn't reach the trainer: playing the built-in holes");
                    }
                    then();
                }));
        }

        void ShowFetchProgress(int index, int count, ServerConfig.HoleSelection selection) =>
            hud?.ShowLoading("Getting the course",
                             index > 0 ? $"Downloading hole {index + 1} of {count}…"
                             : selection == ServerConfig.HoleSelection.Random ? "Picking the holes…" : "Getting the top-rated holes…",
                             count > 0 ? (float)index / count : 0f);

        /// <summary>The download is modal: Back cancels it, every other key is swallowed.</summary>
        bool OnFetchNav(NavKey key)
        {
            if (key == NavKey.Back) CancelFetch();
            return true;
        }

        /// <summary>Back during the download: drop the round (a server game can be resumed) and go back to where we were.</summary>
        public void CancelFetch()
        {
            if (fetching == null) return;
            Debug.Log("[RoundDirector] Course download cancelled.");
            StopFetching();
            EndRound();
            if (ball) StartPractice();
            else phase = Phase.Menu;
            PublishState();
        }

        void StopFetching()
        {
            fetching = null; // the coroutine sees it as cancelled and stops at its next step
            NavInput.Unregister(OnFetchNav);
            hud?.HideLoading();
        }

        /// <summary>Replaces the scene's hole with this round's downloaded hole for `index` (cycling if fewer).</summary>
        void BuildCourseHole(int index)
        {
            if (courseHoles == null || courseHoles.Count == 0 || !FindAnyObjectByType<HoleInfo>()) return;
            string folder = courseHoles[index % courseHoles.Count];
            try
            {
                var built = RuntimeHoleBuilder.Build(folder, course.themes);
                Debug.Log($"[RoundDirector] Hole {index + 1}: built {built.name} from {folder}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[RoundDirector] Couldn't build {folder}: {e.Message}. Playing the scene's own hole.");
            }
        }
    }
}
