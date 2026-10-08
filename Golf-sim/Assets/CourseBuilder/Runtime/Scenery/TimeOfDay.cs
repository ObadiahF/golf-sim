using System;

namespace GolfSim.Course
{
    /// <summary>
    /// When a hole is played. Day is the scene as built; the others relight it (SkyPreset). The two dark ones
    /// switch on the night kit (NightKit: lit green, glowing flag and tee markers, lanterns) and the glowing ball.
    /// </summary>
    public enum TimeOfDay { Day, GoldenHour, Dusk, Night }

    /// <summary>
    /// A menu's pick: a fixed time of day, Auto (the round's own afternoon) or Sunset (golden hour running into dusk);
    /// see SkySchedule.
    /// </summary>
    public enum SkyChoice { Auto, Day, GoldenHour, Dusk, Night, Sunset }

    public static class TimeOfDayNames
    {
        public static readonly TimeOfDay[] All = (TimeOfDay[])Enum.GetValues(typeof(TimeOfDay));

        /// <summary>For the HUD and logs: "Golden hour".</summary>
        public static string Label(this TimeOfDay time) => time switch
        {
            TimeOfDay.GoldenHour => "Golden hour",
            _ => time.ToString(),
        };

        /// <summary>The fixed time a choice stands for, or null for the ones that change during a round (Auto, Sunset).</summary>
        public static TimeOfDay? Fixed(this SkyChoice choice) => choice is SkyChoice.Auto or SkyChoice.Sunset ? null : (TimeOfDay)(choice - 1);
    }
}
