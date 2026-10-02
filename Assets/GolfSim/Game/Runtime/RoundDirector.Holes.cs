using System;
using System.Collections.Generic;
using GolfSim.Course;
using GolfSim.Net;
using UnityEngine;

namespace GolfSim.Game
{
    // Which holes a round plays: the Course Trainer's top-rated holes (downloaded and built at runtime into the
    // hole scene) when available, else the hole scenes listed in CourseRound as built in the editor.
    public partial class RoundDirector
    {
        /// <summary>Local package folders of this round's holes in order, or null to play the scenes as built.</summary>
        List<string> courseHoles;

        /// <summary>Gets the trainer's top holes for the round (cached after the first time), then continues.</summary>
        void FetchCourseHoles(Action then)
        {
            courseHoles = null;
            var config = ServerConfig.Load();
            if (!config.useTopHoles || !course.themes)
            {
                then();
                return;
            }
            phase = Phase.Loading;
            PublishState();
            hud?.Toast("Getting the top-rated holes…");
            StartCoroutine(TrainerHoles.FetchTop(config, round.holeCount, holes =>
            {
                courseHoles = holes.ConvertAll(h => h.folder);
                Debug.Log($"[RoundDirector] Playing the trainer's top {holes.Count} holes: " +
                          string.Join(", ", holes.ConvertAll(h => $"#{h.hole.rank} {h.hole.id} ({h.hole.ups}👍 {h.hole.downs}👎)")));
                then();
            }, error =>
            {
                Debug.LogWarning($"[RoundDirector] {error}; playing the built-in holes instead.");
                hud?.Toast("Couldn't reach the trainer: playing the built-in holes");
                then();
            }));
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
