// Dev check (Play mode, any scene: the round HUD lives on RoundDirector), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/HudLayoutCheck.cs --entry HudLayoutCheck.Show
//   unity command run_script --file Tools/unity_scripts/HudLayoutCheck.cs --entry HudLayoutCheck.Check   (a frame later)
// Q5-6: a toast never overlaps the stats card or the turn badge. Show builds copies of the round HUD off screen at
// 16:9 and 16:10 sizes, with a long lie, a long name on the badge and a long pick-up toast; Check (after the layout
// pass) measures them, saves each to Temp/HudLayoutCheck/ and removes them.
using System.IO;
using System.Linq;
using System.Text;
using GolfSim.Game;
using UnityEngine;
using UnityEngine.UIElements;

public static class HudLayoutCheck
{
    const string Name = "HudLayoutCheck", Out = "Temp/HudLayoutCheck/";
    static readonly Vector2Int[] Sizes = { new(1920, 1080), new(1280, 720), new(1920, 1200), new(1280, 800), new(2560, 1600) };
    static readonly string[] Toasts = { "Obadiah picks up: 10", "Wolfeschlegelsteinhausen picks up: 10. Out of bounds, stroke and distance" };

    public static string Show()
    {
        Remove();
        var hud = RoundDirector.Instance ? RoundDirector.Instance.GetComponentInChildren<UIDocument>() : null;
        if (!hud) return "no round HUD (enter Play mode)";
        foreach (var size in Sizes)
            for (int t = 0; t < Toasts.Length; t++)
            {
                var settings = Object.Instantiate(hud.panelSettings);
                settings.targetTexture = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32);
                var go = new GameObject($"{Name} {size.x}x{size.y} {t}") { hideFlags = HideFlags.DontSave };
                var doc = go.AddComponent<UIDocument>();
                doc.panelSettings = settings;
                doc.visualTreeAsset = hud.visualTreeAsset;
                Fill(doc.rootVisualElement, Toasts[t]);
            }
        return $"built {Sizes.Length * Toasts.Length} HUDs; run Check after a frame";
    }

    public static string Check()
    {
        var docs = Docs();
        if (docs.Length == 0) return "nothing built: run Show first";
        Directory.CreateDirectory(Out);
        var sb = new StringBuilder();
        bool all = true;
        foreach (var doc in docs)
        {
            var root = doc.rootVisualElement;
            Rect card = root.Q("hud-info").worldBound, toast = root.Q("hud-toast").worldBound, badge = root.Q("turn-badge").worldBound;
            float width = root.layout.width;
            bool clear = toast.width > 0f && toast.xMin >= card.xMax && toast.xMax <= badge.xMin;
            bool onScreen = card.xMin >= 0f && badge.xMax <= width + 0.5f;
            all &= clear && onScreen;
            string file = Out + doc.name.Replace(' ', '_') + ".png";
            Save(doc.panelSettings.targetTexture, file);
            sb.AppendLine($"{(clear && onScreen ? "PASS" : "FAIL")} Q5-6 {doc.name}: card ..{card.xMax:0}, toast {toast.xMin:0}..{toast.xMax:0} " +
                          $"({toast.height:0} high), badge {badge.xMin:0}..{badge.xMax:0} of {width:0} -> {file}");
        }
        Remove();
        return (all ? "ALL PASS\n" : "FAILED\n") + sb;
    }

    static void Fill(VisualElement root, string toast)
    {
        void Set(string name, string text) => root.Q<Label>(name).text = text;
        root.Q("hud-player").AddToClassList("hud--hidden"); // in a round the name is on the badge
        Set("hud-eyebrow", "HOLE 2  ·  PAR 5");
        Set("hud-strokes-caption", "SCORE");
        Set("hud-strokes", "10");
        Set("hud-club", "Driver");
        Set("hud-aim", "12.5° R");
        Set("hud-distance", "378 yd");
        Set("hud-lie", "Woods −15%");
        Set("turn-badge-name", "Wolfeschlegelsteinhausen");
        Set("turn-badge-detail", "Hole 2  ·  Par 5  ·  Picked up: 10");
        Set("hud-toast", toast);
        root.Q("turn-badge").AddToClassList("turn-badge--shown");
        root.Q("hud-toast").AddToClassList("toast--shown");
    }

    static UIDocument[] Docs() => Resources.FindObjectsOfTypeAll<UIDocument>().Where(d => d.name.StartsWith(Name)).OrderBy(d => d.name).ToArray();

    static void Save(RenderTexture rt, string path)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = previous;
        Object.Destroy(tex);
    }

    static void Remove()
    {
        foreach (var doc in Docs())
        {
            var settings = doc.panelSettings;
            Object.Destroy(doc.gameObject);
            if (settings)
            {
                settings.targetTexture.Release();
                Object.Destroy(settings.targetTexture);
                Object.Destroy(settings);
            }
        }
    }
}
