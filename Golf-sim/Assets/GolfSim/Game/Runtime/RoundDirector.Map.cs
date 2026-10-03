using GolfSim.Ball;
using GolfSim.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    // The course map (CourseMap): M on the keyboard, Y on a gamepad or the phone's Map button ("map" {show}) opens it
    // whenever a hole is being played and its ball is at rest (a round, practice, any mode with a hole and a ball).
    // Aim and club changes redraw it live. It closes when the ball is hit, on Back, and whenever the game screen goes
    // (pause, replay, scorecard, loading), and "state" tells the phones whether it is up (mapOpen).
    public partial class RoundDirector
    {
        CourseMap map;
        bool mapWanted;

        /// <summary>The course map is up on the TV (state.mapOpen).</summary>
        public bool MapOpen => mapWanted;
        /// <summary>The map on the HUD (null without one); for tools.</summary>
        public CourseMap Map => map;

        void CreateMap()
        {
            if (hud == null) return;
            map = new CourseMap(GetComponentInChildren<UIDocument>().rootVisualElement);
            NavInput.Register(OnMapNav, NavInput.OverlayPriority + 5); // over the pause menu: Back closes the map first
        }

        /// <summary>Opens or closes the map (remote "map", M, a tool). It only opens while it may (MapAllowed).</summary>
        public void ShowMap(bool show)
        {
            mapWanted = show;
            PublishState(); // FillMap drops a map that can't show now; the phones hear either way
        }

        void PollMapKeys()
        {
            if (Keyboard.current?.mKey.wasPressedThisFrame == true || Gamepad.current?.buttonNorth.wasPressedThisFrame == true)
                ShowMap(!mapWanted);
        }

        bool OnMapNav(NavKey key)
        {
            if (key != NavKey.Back || !mapWanted) return false;
            ShowMap(false);
            return true;
        }

        /// <summary>A hole is being played on the game screen and its ball is at rest.</summary>
        bool MapAllowed(StateMessage s) => map != null && ball && hole && !ball.InMotion && s.screen == StateMessage.Game;

        /// <summary>state.mapOpen (called by BuildState): a map that can't show any more (the shot, a pause...) is closed.</summary>
        void FillMap(StateMessage s)
        {
            if (mapWanted && !MapAllowed(s)) mapWanted = false;
            s.mapOpen = mapWanted;
        }

        /// <summary>Shows the state on the map (called by PublishState); paints a new hole's picture as soon as it is bound.</summary>
        void RenderMap(StateMessage s)
        {
            if (map == null) return;
            if (hole) map.Prepare(hole);
            if (s.mapOpen) map.Show(MapSceneFor(s));
            else map.Hide();
        }

        MapScene MapSceneFor(StateMessage s)
        {
            var c = Clubs.Find(club);
            float toPin = YardsToPin / ShotData.YardsPerMeter;
            var scene = new MapScene
            {
                hole = hole, title = RoundHud.Eyebrow(s), club = club, aim = ball.aimOffset, aimDirection = ball.AimDirection,
                carry = c.IsPutter ? toPin : c.carryYards / ShotData.YardsPerMeter,
                ball = new MapScene.Marker { name = s.currentPlayer, position = ball.transform.position, color = TurnBanner.AccentFor(Mathf.Max(0, round?.Current ?? 0)) },
            };
            if (round == null) return scene;
            // The other players' balls in play (not still on the tee, not holed or picked up).
            for (int i = 0; i < round.Balls.Length; i++)
            {
                var other = round.Balls[i];
                if (i == round.Current || other.Done || other.OnTee) continue;
                scene.others.Add(new MapScene.Marker { name = other.player, position = other.position, color = TurnBanner.AccentFor(i) });
            }
            return scene;
        }
    }
}
