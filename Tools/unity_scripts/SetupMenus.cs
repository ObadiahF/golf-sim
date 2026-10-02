// Dev helper (one-off, edit mode), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/SetupMenus.cs --entry SetupMenus.Run
// Turns the open hole scene into Assets/Scenes/HoleSimulator.unity (hole saved out of the generator's
// scratch folder, HOME menu added), renders its channel banner, and creates the MainMenu scene,
// the shared PanelSettings and the Hole Simulator GameMode. Build settings: MainMenu, HoleSimulator.
using System.IO;
using GolfSim.Course;
using GolfSim.CourseEditor;
using GolfSim.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

public static class SetupMenus
{
    const string Game = "Assets/GolfSim/Game";
    const string Scenes = "Assets/Scenes";

    public static string Run()
    {
        if (EditorApplication.isPlaying) return "exit Play mode first";
        var hole = Object.FindAnyObjectByType<HoleInfo>();
        if (!hole) return "no hole in the open scene";

        var panel = PanelSettingsAsset();
        var banner = RenderBanner(hole, $"{Game}/Art/HoleSimulator.png");
        var mode = ModeAsset(banner);

        // Hole Simulator scene = the current hole scene, saved under a new name.
        var scene = hole.gameObject.scene;
        EditorSceneManager.SaveScene(scene, $"{Scenes}/HoleSimulator.unity");
        AddHomeMenu(panel);
        AddEventSystem();
        string folder = HoleSaving.Save(hole);

        // Main menu scene.
        var menu = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.953f, 0.961f, 0.965f);
        camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
        var ui = Document("Main Menu", panel, $"{Game}/UI/MainMenu.uxml");
        ui.AddComponent<MainMenu>().modes = new[] { mode };
        AddEventSystem();
        EditorSceneManager.SaveScene(menu, $"{Scenes}/MainMenu.unity");

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene($"{Scenes}/MainMenu.unity", true),
            new EditorBuildSettingsScene($"{Scenes}/HoleSimulator.unity", true),
        };
        return $"hole saved to {folder}; scenes MainMenu + HoleSimulator created and in build settings";
    }

    static void AddHomeMenu(PanelSettings panel)
    {
        var existing = Object.FindAnyObjectByType<HomeMenu>();
        if (existing) Object.DestroyImmediate(existing.gameObject);
        var go = Document("HOME Menu", panel, $"{Game}/UI/HomeMenu.uxml");
        var home = go.AddComponent<HomeMenu>();
        var flyCam = Camera.main ? Camera.main.GetComponent<HoleFlyCamera>() : null;
        var shotPanel = Object.FindAnyObjectByType<GolfSim.Ball.ShotPanel>();
        home.pauseWhileOpen = new Behaviour[] { flyCam, shotPanel };
    }

    static void AddEventSystem()
    {
        if (!Object.FindAnyObjectByType<EventSystem>())
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    static GameObject Document(string name, PanelSettings panel, string uxml)
    {
        var go = new GameObject(name);
        var doc = go.AddComponent<UIDocument>();
        var so = new SerializedObject(doc); // the panelSettings setter doesn't always serialize in a fresh scene
        so.FindProperty("m_PanelSettings").objectReferenceValue = panel;
        so.ApplyModifiedPropertiesWithoutUndo();
        doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxml);
        return go;
    }

    static PanelSettings PanelSettingsAsset()
    {
        string path = $"{Game}/UI/PanelSettings.asset";
        var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
        if (!panel)
        {
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(panel, path);
        }
        panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>($"{Game}/UI/GolfTheme.tss");
        panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        panel.referenceResolution = new Vector2Int(1920, 1080);
        panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        panel.match = 0.5f;
        EditorUtility.SetDirty(panel);
        return panel;
    }

    static GameMode ModeAsset(Texture2D banner)
    {
        string path = $"{Game}/Modes/HoleSimulator.asset";
        var mode = AssetDatabase.LoadAssetAtPath<GameMode>(path);
        if (!mode)
        {
            mode = ScriptableObject.CreateInstance<GameMode>();
            AssetDatabase.CreateAsset(mode, path);
        }
        mode.title = "Hole Simulator";
        mode.description = "Play a single hole with real ball physics. Hit with the shot panel or your launch monitor.";
        mode.sceneName = "HoleSimulator";
        mode.banner = banner;
        EditorUtility.SetDirty(mode);
        AssetDatabase.SaveAssets();
        return mode;
    }

    static Texture2D RenderBanner(HoleInfo hole, string path)
    {
        const int w = 1600, h = 900;
        var go = new GameObject("BannerCamera") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.farClipPlane = 4000f;
        cam.fieldOfView = 50f;
        go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        // Behind and above the tee, looking down the hole toward the green.
        var fwd = Vector3.ProjectOnPlane(hole.PinWorld - hole.TeeWorld, Vector3.up).normalized;
        var pos = hole.TeeWorld - fwd * 25f + Vector3.up * 18f;
        var target = Vector3.Lerp(hole.TeeWorld, hole.PinWorld, 0.55f);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
        var rt = new RenderTexture(w, h, 24);
        cam.targetTexture = rt;
        try
        {
            for (int i = 0; i < 8; i++) cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        finally
        {
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
        }
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
