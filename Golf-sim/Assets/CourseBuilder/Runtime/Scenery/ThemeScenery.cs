using System;
using UnityEngine;

namespace GolfSim.Course
{
    /// <summary>What drifts through the air of a theme: nothing, fireflies after sunset, or snow.</summary>
    public enum AirLife { None, Fireflies, Snow }

    /// <summary>
    /// A theme's atmosphere on top of its ground and models (CourseTheme): how hazy it is and in what colour,
    /// which times of day it likes, and what floats in the air. One row per theme name (hole.json "theme"),
    /// matching the generator presets in Tools/course_gen/style.py; an unknown theme gets the plain row.
    /// </summary>
    public class ThemeScenery
    {
        public string theme;
        public string label;                    // course type for the HUD: "Red Rock Canyon"
        public Color fogTint = Color.white;     // multiplies the time of day's fog colour
        public float haze = 1f;                 // multiplies its fog density (sea mist > dry desert air)
        /// <summary>Chance of a round starting at each TimeOfDay (Day, GoldenHour, Dusk, Night); relative weights.</summary>
        public float[] startWeights = { 0.55f, 0.2f, 0.12f, 0.13f };
        public AirLife air;
        /// <summary>The ponds' shallow tint and deep colour (GolfSim/Water), or null for the material's own.</summary>
        public Color? waterShallow, waterDeep;

        static readonly ThemeScenery Plain = new ThemeScenery { theme = "", label = "Golf course" };

        static readonly ThemeScenery[] Rows =
        {
            new ThemeScenery { theme = "coastal", label = "Coastal", fogTint = new Color(0.95f, 0.98f, 1.05f), haze = 1.25f },
            new ThemeScenery { theme = "parkland", label = "Parkland", air = AirLife.Fireflies },
            new ThemeScenery { theme = "forest", label = "Forest", fogTint = new Color(0.92f, 1f, 0.95f), haze = 1.3f, air = AirLife.Fireflies },
            new ThemeScenery { theme = "lakes", label = "Lakes", haze = 1.45f, air = AirLife.Fireflies },
            new ThemeScenery { theme = "links", label = "Links", fogTint = new Color(0.97f, 0.98f, 1.02f), haze = 1.3f,
                               startWeights = new[] { 0.45f, 0.3f, 0.13f, 0.12f } },
            new ThemeScenery { theme = "desert", label = "Desert", fogTint = new Color(1.06f, 0.96f, 0.86f), haze = 0.6f,
                               startWeights = new[] { 0.45f, 0.25f, 0.12f, 0.18f } },
            new ThemeScenery { theme = "mountain", label = "Mountain", fogTint = new Color(0.94f, 0.98f, 1.06f), haze = 0.7f },
            new ThemeScenery { theme = "autumn", label = "Autumn Parkland", fogTint = new Color(1.06f, 0.97f, 0.88f), haze = 1.15f,
                               startWeights = new[] { 0.4f, 0.35f, 0.12f, 0.13f } },
            new ThemeScenery { theme = "tropical", label = "Tropical Island", fogTint = new Color(0.92f, 1.02f, 1.04f), haze = 1.3f,
                               waterShallow = new Color(0.5f, 1f, 0.92f), waterDeep = new Color(0f, 0.2f, 0.24f),
                               air = AirLife.Fireflies, startWeights = new[] { 0.5f, 0.2f, 0.15f, 0.15f } },
            new ThemeScenery { theme = "canyon", label = "Red Rock Canyon", fogTint = new Color(1.1f, 0.9f, 0.8f), haze = 0.55f,
                               waterShallow = new Color(0.72f, 0.8f, 0.62f), waterDeep = new Color(0.04f, 0.1f, 0.09f),
                               startWeights = new[] { 0.4f, 0.3f, 0.15f, 0.15f } },
            new ThemeScenery { theme = "winter", label = "Winter", fogTint = new Color(0.92f, 0.96f, 1.08f), haze = 1.15f,
                               waterShallow = new Color(0.6f, 0.72f, 0.78f), waterDeep = new Color(0.02f, 0.06f, 0.1f),
                               air = AirLife.Snow, startWeights = new[] { 0.5f, 0.2f, 0.15f, 0.15f } },
            new ThemeScenery { theme = "heathland", label = "Heathland", fogTint = new Color(0.98f, 0.96f, 1.02f), haze = 1.2f },
        };

        public static ThemeScenery For(string theme) =>
            Array.Find(Rows, r => string.Equals(r.theme, theme, StringComparison.OrdinalIgnoreCase)) ?? Plain;

        /// <summary>A weighted pick of the starting time of day from a roll in 0..1.</summary>
        public TimeOfDay PickStart(float roll01)
        {
            float total = 0f;
            foreach (float w in startWeights) total += w;
            float roll = roll01 * total;
            for (int i = 0; i < startWeights.Length; i++)
            {
                roll -= startWeights[i];
                if (roll <= 0f) return (TimeOfDay)i;
            }
            return TimeOfDay.Day;
        }
    }
}
