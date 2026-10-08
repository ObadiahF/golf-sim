// Dev helper (one-off, edit mode), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/SetupMenuCards.cs --entry SetupMenuCards.Run
// The main menu's cards after the menu rework: Play a Round (opens "Choose a course"), Practice (the driving range and
// putting green; Hole Simulator only with Settings > Developer modes), Scores and Settings (the old Sound card, renamed,
// same asset). Night Golf is a course now (CourseCatalog), so its card asset goes. Opens the menu scene additively,
// sets MainMenu.modes / practice / developerModes and saves only that scene. Safe to re-run.
using System;
using System.Linq;
using GolfSim.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SetupMenuCards
{
    const string Modes = "Assets/GolfSim/Game/Modes/";
    const string Art = "Assets/GolfSim/Game/Art/";
    const string CourseArt = "Assets/GolfSim/Game/Resources/CourseArt/";
    const string MenuScene = "Assets/Scenes/MainMenu.unity";

    public static string Run()
    {
        AssetDatabase.Refresh(); // the course art
        var play = Mode("PlayRound", GameMode.ModeKind.Round, "Play a Round",
                        "Up to 8 players taking turns with one phone, on any of 15 courses. Add players in the SwingRemote app and press " +
                        "Start Game, or choose a course here for a solo round.", Art + "HoleSimulator.png");
        var practice = Mode("Practice", GameMode.ModeKind.Practice, "Practice",
                            "Work on any club at the driving range, or your putting on a big sloping green.", Art + "DrivingRange.png");
        var scores = Mode("Scores", GameMode.ModeKind.Scores, "Scores", null, CourseArt + "Heathland.png");
        if (AssetDatabase.LoadAssetAtPath<GameMode>(Modes + "Sound.asset"))
        {
            string error = AssetDatabase.RenameAsset(Modes + "Sound.asset", "Settings"); // same guid: the scene keeps it
            if (!string.IsNullOrEmpty(error)) throw new Exception(error);
        }
        var settings = Mode("Settings", GameMode.ModeKind.Settings, "Settings",
                            "Sound, wind, putting assist, graphics, the room code and updates.", CourseArt + "Lakeside.png");
        var range = AssetDatabase.LoadAssetAtPath<GameMode>(Modes + "DrivingRange.asset");
        var green = AssetDatabase.LoadAssetAtPath<GameMode>(Modes + "PuttingGreen.asset");
        var holeSimulator = AssetDatabase.LoadAssetAtPath<GameMode>(Modes + "HoleSimulator.asset");
        if (AssetDatabase.LoadAssetAtPath<GameMode>(Modes + "NightGolf.asset")) AssetDatabase.DeleteAsset(Modes + "NightGolf.asset");
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Additive);
        try
        {
            var menu = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<MainMenu>(true)).First(m => m);
            menu.modes = new[] { play, practice, scores, settings };
            menu.practice = new[] { range, green };
            menu.developerModes = new[] { holeSimulator };
            EditorUtility.SetDirty(menu);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"modes: {Titles(menu.modes)}; practice: {Titles(menu.practice)}; developer: {Titles(menu.developerModes)}";
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    static GameMode Mode(string asset, GameMode.ModeKind kind, string title, string description, string bannerPath)
    {
        string path = Modes + asset + ".asset";
        var mode = AssetDatabase.LoadAssetAtPath<GameMode>(path);
        if (!mode)
        {
            mode = ScriptableObject.CreateInstance<GameMode>();
            AssetDatabase.CreateAsset(mode, path);
        }
        mode.kind = kind;
        mode.title = title;
        if (description != null) mode.description = description;
        mode.banner = AssetDatabase.LoadAssetAtPath<Texture2D>(bannerPath) ?? throw new Exception($"No art at {bannerPath}");
        EditorUtility.SetDirty(mode);
        return mode;
    }

    static string Titles(GameMode[] modes) => string.Join(", ", modes.Select(m => m ? m.title : "-"));
}
