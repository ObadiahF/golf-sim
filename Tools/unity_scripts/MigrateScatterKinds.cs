// Dev helper (one-off, edit mode), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/MigrateScatterKinds.cs --entry MigrateScatterKinds.Run
// Hole format v2 moved tree/rock placement into the hole package: ScatterSets and themes now only say which
// models represent each object kind. Sets kinds on the default ScatterSet's old rules (by their old names),
// adds the missing kinds, and resets every theme's scatter slots to DefaultThemes.KindQueries.
using GolfSim.Course;
using System.Linq;
using GolfSim.CourseEditor;
using UnityEditor;

public static class MigrateScatterKinds
{
    public static string Run()
    {
        var scatter = CourseDefaults.Scatter();
        var byOldName = new System.Collections.Generic.Dictionary<string, ObjectKind>
        {
            ["Woods"] = ObjectKind.Conifer, ["Native trees"] = ObjectKind.Deciduous, ["Shrubs"] = ObjectKind.Shrub,
            ["Slope boulders"] = ObjectKind.Boulder, ["Loose rocks"] = ObjectKind.Rock,
        };
        foreach (var rule in scatter.rules)
            if (byOldName.TryGetValue(rule.name, out var kind)) { rule.kind = kind; rule.name = kind.ToString(); }
        foreach (ObjectKind kind in System.Enum.GetValues(typeof(ObjectKind)))
            if (!scatter.rules.Any(r => r.kind == kind))
                scatter.rules.Add(new ScatterSet.Rule { name = kind.ToString(), kind = kind });
        EditorUtility.SetDirty(scatter);

        int themes = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:CourseTheme"))
        {
            var theme = AssetDatabase.LoadAssetAtPath<CourseTheme>(AssetDatabase.GUIDToAssetPath(guid));
            theme.scatter = DefaultThemes.KindQueries
                .Select(kv => new CourseTheme.ScatterSlot { kind = kv.Key, assets = kv.Value }).ToList();
            EditorUtility.SetDirty(theme);
            themes++;
        }
        AssetDatabase.SaveAssets();
        return $"default scatter: {string.Join(", ", scatter.rules.Select(r => $"{r.kind}={r.prototypes.Count(p => p.prefab)}"))}; {themes} themes reset";
    }
}
