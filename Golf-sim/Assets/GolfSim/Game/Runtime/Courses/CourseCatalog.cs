using System;
using GolfSim.Course;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// One card on the "Choose a course" screen: where its holes come from (course_gen presets, Tools/course_gen/style.py,
    /// asked of the trainer as random-holes?preset=), how they are dressed and when in the day they are played.
    /// </summary>
    public class CourseType
    {
        public readonly string id, title, description;
        /// <summary>The trainer's preset ids the holes are drawn from; empty: every course type.</summary>
        public readonly string[] presets;
        /// <summary>Dress every hole as this theme whatever its package says (Coastal: links holes by the sea), or null.</summary>
        public readonly string theme;
        /// <summary>Its time of day when the screen's Time of day is "Course default".</summary>
        public readonly SkyChoice sky;
        readonly string art;
        Texture2D banner;

        public CourseType(string id, string title, string description, string art, string[] presets,
                          SkyChoice sky = SkyChoice.Auto, string theme = null)
        {
            this.id = id;
            this.title = title;
            this.description = description;
            this.art = art;
            this.presets = presets;
            this.sky = sky;
            this.theme = theme;
        }

        /// <summary>The card and backdrop art (Resources/CourseArt).</summary>
        public Texture2D Banner => banner ? banner : banner = Resources.Load<Texture2D>(CourseCatalog.ArtFolder + art);

        /// <summary>A single course type (not Night Golf, Sunset or Surprise Me, which mix them).</summary>
        public bool IsType => presets.Length > 0;
    }

    /// <summary>The Time of day choice on the course screen: the course's own (Course default), or a fixed one.</summary>
    public enum SkyOption { CourseDefault, Day, GoldenHour, Dusk, Night }

    /// <summary>
    /// The courses a round can be played on, and the last one chosen on the TV (PlayerPrefs). A round started from the
    /// phone (gameStarted) plays the last course chosen here too, at its last time of day. The round's length is the
    /// Gameplay setting (GameSettings.RoundLength), which the course screen's Holes choice changes.
    /// </summary>
    public static class CourseCatalog
    {
        public const string ArtFolder = "CourseArt/";
        const string CourseKey = "GolfSim.Course", SkyKey = "GolfSim.CourseSky";

        static string[] One(string preset) => new[] { preset };

        public static readonly CourseType[] All =
        {
            new CourseType("parkland", "Classic Parkland", "Tree-lined fairways, ponds and bunkers: the classic course.", "Parkland", One("parkland")),
            new CourseType("autumn", "Autumn", "Parkland in red and gold, with warm low light through the leaves.", "Autumn", One("autumn")),
            new CourseType("tropical", "Tropical Island", "Palms, white sand and turquoise lagoons.", "Tropical", One("tropical")),
            new CourseType("canyon", "Red Rock Canyon", "Big drops between red rock and desert scrub.", "Canyon", One("canyon")),
            new CourseType("winter", "Winter", "Snow beside the fairways and flakes in the air.", "Winter", One("winter")),
            new CourseType("heathland", "Heathland", "Heather, gorse and firm, rolling fairways.", "Heathland", One("heathland")),
            new CourseType("links", "Links", "Wide, windswept fairways, deep bunkers and hardly a tree.", "Links", One("links")),
            new CourseType("desert", "Desert", "Ribbons of green through sand and cactus scrub.", "Desert", One("desert")),
            new CourseType("mountain", "Mountain", "Steep climbs and drops with pines all around.", "Mountain", One("mountain")),
            new CourseType("lakes", "Lakeside", "Water on almost every hole: carry it or lay up.", "Lakeside", One("lakes")),
            new CourseType("forest", "Forest", "Narrow fairways cut through dense woods.", "Forest", One("forest")),
            // No generator preset of its own: links holes, dressed for the coast (sea mist, seaside planting).
            new CourseType("coastal", "Coastal", "Links holes by the sea, in the coastal mist.", "Coastal", One("links"), theme: "coastal"),
            new CourseType("night", "Night Golf", "Any course after dark: a glowing ball, floodlit greens and lanterns along the fairways.",
                           "Night", new string[0], SkyChoice.Night),
            new CourseType("sunset", "Sunset Round", "Tee off in golden hour and finish at dusk as the lights come on.",
                           "Sunset", new string[0], SkyChoice.Sunset),
            new CourseType("surprise", "Surprise Me", "Every course type mixed, on an afternoon that may run into night.",
                           "Surprise", new string[0]),
        };

        /// <summary>Surprise Me: what rounds played before there was a choice (every type, Auto sky).</summary>
        public static CourseType Default => Find("surprise");

        public static CourseType Find(string id) => Array.Find(All, c => c.id == id);

        /// <summary>The course last chosen on the TV (Surprise Me before the first choice).</summary>
        public static CourseType Last
        {
            get => Find(PlayerPrefs.GetString(CourseKey, "")) ?? Default;
            set => PlayerPrefs.SetString(CourseKey, (value ?? Default).id);
        }

        /// <summary>The Time of day last chosen on the course screen.</summary>
        public static SkyOption LastSky
        {
            get => (SkyOption)Mathf.Clamp(PlayerPrefs.GetInt(SkyKey, 0), 0, (int)SkyOption.Night);
            set => PlayerPrefs.SetInt(SkyKey, (int)value);
        }

        /// <summary>The round's time of day: the course's own, or the fixed one picked (SkyOption.Day.. match SkyChoice.Day..).</summary>
        public static SkyChoice SkyFor(CourseType course, SkyOption option) =>
            option == SkyOption.CourseDefault ? course.sky : (SkyChoice)(int)option;

        /// <summary>For the screen and the hero text: "Golden hour", "Course default".</summary>
        public static string Label(this SkyOption option) => option switch
        {
            SkyOption.CourseDefault => "Course default",
            SkyOption.GoldenHour => "Golden hour",
            _ => option.ToString(),
        };
    }
}
