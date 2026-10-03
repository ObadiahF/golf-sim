using UnityEngine;

namespace GolfSim.Course
{
    /// <summary>
    /// Builds a hole package from disk while the game runs (e.g. holes downloaded from the trainer), replacing the
    /// hole currently in the scene. Everything it creates lives in memory and is freed with the hole.
    /// </summary>
    public static class RuntimeHoleBuilder
    {
        static readonly System.Collections.Generic.List<TerrainData> Built = new System.Collections.Generic.List<TerrainData>();

        public static HoleInfo Build(string packageFolder, RuntimeThemeLibrary themes) =>
            Build(HolePackage.Load(System.IO.Path.Combine(packageFolder, HolePackage.FileName)), themes);

        /// <summary>Builds a loaded package, or one made in code (heights and objects in memory), dressed by its theme.</summary>
        public static HoleInfo Build(HolePackage pkg, RuntimeThemeLibrary themes)
        {
            Clear();
            var root = HoleBuilder.Build(pkg, themes.OptionsFor(pkg), new HoleAssets());
            Built.Add(root.GetComponentInChildren<Terrain>().terrainData);
            return root.GetComponent<HoleInfo>();
        }

        /// <summary>Destroys the holes in the scene and the terrain data built for them at runtime.</summary>
        public static void Clear()
        {
            foreach (var hole in Object.FindObjectsByType<HoleInfo>(FindObjectsInactive.Include))
            {
                hole.gameObject.SetActive(false); // Destroy is deferred: hide it now so lookups find the new hole
                Destroy(hole.gameObject);
            }
            foreach (var data in Built) if (data) Destroy(data); // only what we built: scene holes use saved assets
            Built.Clear();
        }

        static void Destroy(Object obj)
        {
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
