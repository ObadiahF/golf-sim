// Dev helper (one-off, edit mode), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/SetupRound.cs --entry SetupRound.Run
// Creates the multiplayer assets: Resources/CourseRound (hole scenes, turn rules, HUD), Resources/GolfServer
// (server URL + token) and the "Play a Round" and "Scores" GameModes, and puts them on the MainMenu scene's
// cards (round first, scores last). Idempotent: existing assets are kept. The MainMenu scene is opened
// additively, so the open scene stays.
using System.Linq;
using GolfSim.Game;
using GolfSim.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public static class SetupRound
{
    const string Game = "Assets/GolfSim/Game";

    public static string Run()
    {
        if (EditorApplication.isPlaying) return "exit Play mode first";
        var round = Asset<CourseRound>($"{Game}/Resources/CourseRound.asset", r =>
        {
            r.holeScenes = new[] { "HoleSimulator" };
            r.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>($"{Game}/UI/PanelSettings.asset");
            r.hudLayout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"{Game}/UI/RoundHud.uxml");
        });
        Asset<ServerConfig>("Assets/GolfSim/Net/Resources/GolfServer.asset", _ => { });
        var mode = Asset<GameMode>($"{Game}/Modes/PlayRound.asset", m =>
        {
            m.title = "Play a Round";
            m.description = "9 or 18 holes for up to 8 players, taking turns with one phone. Add players in the SwingRemote app " +
                            "and press Start Game, or press Play here for a solo round.";
            m.kind = GameMode.ModeKind.Round;
            m.banner = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Game}/Art/HoleSimulator.png");
        });
        var scores = Asset<GameMode>($"{Game}/Modes/Scores.asset", m =>
        {
            m.title = "Scores";
            m.description = "Handicaps, averages, best rounds, wins, birdies and aces for everyone who has played, from the game server.";
            m.kind = GameMode.ModeKind.Scores;
            m.banner = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Game}/Art/HoleSimulator.png");
        });
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Additive);
        var menu = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<MainMenu>(true)).First(m => m);
        if (!menu.modes.Contains(mode) || !menu.modes.Contains(scores))
        {
            menu.modes = new[] { mode }.Concat(menu.modes.Where(m => m != mode && m != scores)).Append(scores).ToArray();
            EditorUtility.SetDirty(menu);
            EditorSceneManager.SaveScene(scene);
        }
        if (SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
        return $"CourseRound ({string.Join(", ", round.holeScenes)}), GolfServer, PlayRound mode; menu cards: " +
               string.Join(", ", menu.modes.Select(m => m.title));
    }

    static T Asset<T>(string path, System.Action<T> init) where T : ScriptableObject
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing) return existing;
        var folder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(folder).Replace('\\', '/'), System.IO.Path.GetFileName(folder));
        var asset = ScriptableObject.CreateInstance<T>();
        init(asset);
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }
}
