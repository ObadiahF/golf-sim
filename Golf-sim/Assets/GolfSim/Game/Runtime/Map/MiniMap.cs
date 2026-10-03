using System.Collections.Generic;
using GolfSim.Ball;
using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// The mini map in the HUD's bottom right corner while a hole is played: the hole picture (HolePicture, tee at the
    /// bottom, pin at the top) with the pin, the aim line and every ball, the distance to the pin under it, and the
    /// ball followed live while it flies, leaving a trail. The trail stays until the next shot or the next player.
    /// When it shows is the game's call (it steps aside for the course map).
    /// </summary>
    public class MiniMap
    {
        const string Shown = "minimap--shown";
        const float TrailStep = 1.5f; // metres between trail points

        readonly VisualElement panel;
        readonly HolePicture picture;
        readonly Label caption;
        readonly List<Vector3> trail = new List<Vector3>();
        MapScene scene;
        bool flying;
        string trailPlayer;

        public MiniMap(VisualElement hudRoot)
        {
            CourseMap.AddStyles(hudRoot);
            panel = CourseMap.Element("minimap");
            picture = new HolePicture(CourseMap.Element("map__view", panel), new MapOverlay(compact: true), "MiniMap");
            caption = panel.AddLabel("minimap__caption");
            CourseMap.UnderOverlays(hudRoot, panel);
        }

        public bool IsShown => panel.ClassListContains(Shown);
        /// <summary>What it shows (or last showed); for tools.</summary>
        public MapScene Scene => scene;
        public IReadOnlyList<Vector3> Trail => trail;

        /// <summary>Shows this scene, or redraws it (aim, club, the next player).</summary>
        public void Show(MapScene shown)
        {
            scene = shown;
            if (scene.aiming) flying = false;
            if (!flying && scene.ball.name != trailPlayer) trail.Clear();
            scene.trail = trail;
            scene.aiming &= !flying;
            caption.text = $"{scene.ToPin * ShotData.YardsPerMeter:0} YD TO PIN";
            panel.AddToClassList(Shown);
            picture.Show(scene);
        }

        public void Hide() => panel.RemoveFromClassList(Shown);

        /// <summary>The ball in flight (every frame): moves it on the map and extends the trail.</summary>
        public void Track(Vector3 position)
        {
            if (scene == null || !IsShown) return;
            if (!flying)
            {
                flying = true;
                trail.Clear();
                trail.Add(scene.ball.position);
                trailPlayer = scene.ball.name;
                scene.aiming = false;
            }
            if (Round.FlatDistance(trail[trail.Count - 1], position) >= TrailStep) trail.Add(position);
            scene.ball.position = position;
            caption.text = $"{scene.ToPin * ShotData.YardsPerMeter:0} YD TO PIN";
            picture.Show(scene);
        }

        /// <summary>Paints the hole's picture ahead of time (while the hole fades in).</summary>
        public void Prepare(GolfSim.Course.HoleInfo hole) => picture.Prepare(hole);
    }
}
