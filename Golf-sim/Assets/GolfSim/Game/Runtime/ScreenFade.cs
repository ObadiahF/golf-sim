using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace GolfSim.Game
{
    /// <summary>
    /// Full-screen fade curtain for a UI Toolkit root: fades in on creation, fades out to load a scene (and back in if
    /// the root persists). Only one scene load runs at a time: a second request during the fade (a double Select or
    /// click, from any menu) is ignored until the scene has loaded.
    /// </summary>
    public class ScreenFade
    {
        const string Opaque = "fade--opaque";
        const long FadeMs = 450; // matches the .fade transition in Menu.uss

        /// <summary>True from a LoadScene request until that scene has loaded.</summary>
        public static bool Loading { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Loading = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Loading = false;

        readonly VisualElement curtain = new VisualElement { pickingMode = PickingMode.Ignore };

        public ScreenFade(VisualElement root)
        {
            curtain.AddToClassList("fade");
            curtain.AddToClassList(Opaque);
            root.Add(curtain);
            curtain.schedule.Execute(() => curtain.RemoveFromClassList(Opaque)).StartingIn(30);
        }

        public void LoadScene(string sceneName)
        {
            if (Loading) return;
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"[ScreenFade] Scene '{sceneName}' is not in the build settings.");
                return;
            }
            Loading = true;
            curtain.pickingMode = PickingMode.Position; // swallow clicks while leaving
            curtain.AddToClassList(Opaque);
            curtain.schedule.Execute(() =>
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(sceneName);
                // Only runs when this root outlives the scene (a DontDestroyOnLoad HUD): fade back in.
                curtain.schedule.Execute(() =>
                {
                    curtain.pickingMode = PickingMode.Ignore;
                    curtain.RemoveFromClassList(Opaque);
                }).StartingIn(100);
            }).StartingIn(FadeMs);
        }
    }
}
