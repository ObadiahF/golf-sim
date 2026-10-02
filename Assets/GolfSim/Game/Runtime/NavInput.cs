using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GolfSim.Game
{
    public enum NavKey { Up, Down, Left, Right, Select, Back }

    /// <summary>
    /// One input path for the menus and the game: the keyboard (arrows, Enter, Esc), a gamepad (d-pad, A, B)
    /// and the phone's remote ("nav" messages) all become NavKeys. Handlers are asked highest priority
    /// first, and the first one that returns true consumes the key (an open pause menu beats the game).
    /// </summary>
    public static class NavInput
    {
        public const int MenuPriority = 0, GamePriority = 0, OverlayPriority = 100;

        static readonly List<(int priority, Func<NavKey, bool> handler)> handlers = new List<(int, Func<NavKey, bool>)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => handlers.Clear();

        public static void Register(Func<NavKey, bool> handler, int priority)
        {
            Unregister(handler);
            int i = handlers.FindIndex(h => h.priority < priority);
            handlers.Insert(i < 0 ? handlers.Count : i, (priority, handler));
        }

        public static void Unregister(Func<NavKey, bool> handler) => handlers.RemoveAll(h => h.handler == handler);

        /// <summary>Delivers a key to the handlers; true if one used it.</summary>
        public static bool Push(NavKey key)
        {
            foreach (var (_, handler) in handlers.ToArray())
                if (handler(key)) return true;
            return false;
        }

        /// <summary>Parses a remote "nav" key (up, down, left, right, select, back).</summary>
        public static bool TryParse(string key, out NavKey nav) => Enum.TryParse(key, true, out nav) && Enum.IsDefined(typeof(NavKey), nav);

        /// <summary>Reads this frame's keyboard and gamepad presses (called once per frame by GameSession).</summary>
        public static void Poll()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.upArrowKey.wasPressedThisFrame) Push(NavKey.Up);
                if (kb.downArrowKey.wasPressedThisFrame) Push(NavKey.Down);
                if (kb.leftArrowKey.wasPressedThisFrame) Push(NavKey.Left);
                if (kb.rightArrowKey.wasPressedThisFrame) Push(NavKey.Right);
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Push(NavKey.Select);
                if (kb.escapeKey.wasPressedThisFrame) Push(NavKey.Back);
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.dpad.up.wasPressedThisFrame) Push(NavKey.Up);
                if (pad.dpad.down.wasPressedThisFrame) Push(NavKey.Down);
                if (pad.dpad.left.wasPressedThisFrame) Push(NavKey.Left);
                if (pad.dpad.right.wasPressedThisFrame) Push(NavKey.Right);
                if (pad.buttonSouth.wasPressedThisFrame) Push(NavKey.Select);
                if (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame) Push(NavKey.Back);
            }
        }
    }
}
