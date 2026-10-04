// Dev helper (one-off, edit mode), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/SetupNightGolf.cs --entry SetupNightGolf.Run
// The Night Golf card on the main menu: a Round GameMode whose holes are all played at night (GameMode.sky), with
// Art/NightGolf.png as its channel art (a night tee shot from SceneryCheck.Shots), placed right after Play a Round.
// Opens the menu scene additively to add the card and saves only that scene. Safe to re-run.
using System;
using System.Linq;
using GolfSim.Course;
using GolfSim.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SetupNightGolf
{
    const string ModePath = "Assets/GolfSim/Game/Modes/NightGolf.asset";
    const string RoundModePath = "Assets/GolfSim/Game/Modes/PlayRound.asset";
    const string BannerPath = "Assets/GolfSim/Game/Art/NightGolf.png";
    const string MenuScene = "Assets/Scenes/MainMenu.unity";

    public static string Run()
    {
        var banner = AssetDatabase.LoadAssetAtPath<Texture2D>(BannerPath) ?? throw new Exception($"Put the card art at {BannerPath} first");
        var mode = AssetDatabase.LoadAssetAtPath<GameMode>(ModePath);
        if (!mode)
        {
            mode = ScriptableObject.CreateInstance<GameMode>();
            AssetDatabase.CreateAsset(mode, ModePath);
        }
        mode.kind = GameMode.ModeKind.Round;
        mode.title = "Night Golf";
        mode.description = "{holes} holes under the stars: a glow-in-the-dark ball, floodlit greens and lanterns along the fairways. " +
                           "Same players and phones as Play a Round.";
        mode.sky = SkyChoice.Night;
        mode.banner = banner;
        EditorUtility.SetDirty(mode);
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Additive);
        try
        {
            var menu = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<MainMenu>(true)).First(m => m);
            if (!menu.modes.Contains(mode))
            {
                var list = menu.modes.ToList();
                int after = list.IndexOf(AssetDatabase.LoadAssetAtPath<GameMode>(RoundModePath));
                list.Insert(after + 1, mode);
                menu.modes = list.ToArray();
                EditorUtility.SetDirty(menu);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            return $"menu modes: {string.Join(", ", menu.modes.Select(m => m ? m.title : "-"))}";
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
