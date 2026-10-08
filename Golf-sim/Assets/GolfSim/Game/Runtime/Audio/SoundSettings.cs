using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// The Sound section of the Settings screen: one bar per volume (master, effects, crowd, ambience, interface), saved
    /// through GameAudio. Left/Right changes the highlighted one by 10 % (to the next 10 % mark from a default off the
    /// grid; keyboard, gamepad or the phone's D-pad); a click on a bar sets it.
    /// </summary>
    public static class SoundSettings
    {
        const float Increment = 0.1f;

        readonly struct Channel
        {
            public readonly string name;
            public readonly Func<float> get;
            public readonly Action<float> set;

            public Channel(string name, Func<float> get, Action<float> set)
            {
                this.name = name;
                this.get = get;
                this.set = set;
            }
        }

        static readonly Channel[] Channels =
        {
            new Channel("Master", () => GameAudio.MasterVolume, v => GameAudio.MasterVolume = v),
            Bus("Effects", SoundBus.Sfx),
            Bus("Crowd", SoundBus.Crowd),
            Bus("Ambience", SoundBus.Ambience),
            Bus("Interface", SoundBus.Ui),
        };

        /// <summary>A row per volume, for the Settings screen.</summary>
        public static SettingRow[] Rows() => Array.ConvertAll(Channels, c => (SettingRow)new VolumeRow(c));

        /// <summary>The next 10 % mark in this direction: 70 → 60 or 80, and 75 (a default) → 70 or 80.</summary>
        public static float Stepped(float value, int direction)
        {
            const float Slack = 1e-3f; // float noise: 0.7 is 6.9999 steps
            float steps = value / Increment;
            float next = direction > 0 ? Mathf.Floor(steps + Slack) + 1f : Mathf.Ceil(steps - Slack) - 1f;
            return Mathf.Clamp01(next * Increment);
        }

        static Channel Bus(string name, SoundBus bus) => new Channel(name, () => GameAudio.GetVolume(bus), v => GameAudio.SetVolume(bus, v));

        /// <summary>A volume: its bar and percentage.</summary>
        class VolumeRow : SettingRow
        {
            readonly Channel channel;
            readonly VisualElement fill;
            readonly Label value;

            public VolumeRow(Channel channel) : base(channel.name)
            {
                this.channel = channel;
                var bar = MenuUi.Box("volume__bar");
                fill = MenuUi.Box("volume__fill");
                fill.pickingMode = PickingMode.Ignore;
                bar.Add(fill);
                bar.RegisterCallback<PointerDownEvent>(e => Set(bar.layout.width > 0f ? e.localPosition.x / bar.layout.width : channel.get()));
                Control.Add(bar);
                value = MenuUi.Text("", "setting__value");
                value.AddToClassList("volume__value");
                Control.Add(value);
            }

            public override void Render()
            {
                float v = channel.get();
                fill.style.width = Length.Percent(v * 100f);
                value.text = $"{Mathf.RoundToInt(v * 100f)}%";
            }

            public override void Step(int direction) => Set(Stepped(channel.get(), direction));

            public override void Activate() { } // Select on a volume does nothing (Back closes the screen)

            void Set(float v)
            {
                channel.set(Mathf.Floor(Mathf.Clamp01(v) / Increment + 0.5f) * Increment); // whole steps (halves up), as the D-pad gives
                Render();
            }
        }
    }
}
