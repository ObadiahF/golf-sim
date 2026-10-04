using System.Collections.Generic;
using UnityEngine;

namespace GolfSim.Course
{
    /// <summary>
    /// Everything Scenery.Apply made for the hole on screen (post volume, night kit, air particles, the sky
    /// material), under one scene object. It goes with the scene; applying again (a practice hole rebuilt in
    /// place) replaces it. Materials and profiles made in code are freed with it.
    /// </summary>
    public class SceneryRig : MonoBehaviour
    {
        public TimeOfDay time;
        public string theme;

        readonly List<Object> owned = new List<Object>();

        /// <summary>The rig in the active scene, after destroying any earlier one.</summary>
        public static SceneryRig Fresh()
        {
            foreach (var old in FindObjectsByType<SceneryRig>(FindObjectsInactive.Include))
            {
                old.gameObject.SetActive(false); // Destroy is deferred: hide it so its lights and particles go now
                Discard(old.gameObject);
            }
            return new GameObject("Scenery").AddComponent<SceneryRig>();
        }

        /// <summary>The rig of the hole on screen, or null (no scenery applied: a day scene as built).</summary>
        public static SceneryRig Current => FindAnyObjectByType<SceneryRig>();

        /// <summary>Frees `obj` (a material, profile, texture or mesh made in code) when the rig goes.</summary>
        public T Own<T>(T obj) where T : Object
        {
            owned.Add(obj);
            return obj;
        }

        /// <summary>A child object of the rig.</summary>
        public Transform Child(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            return t;
        }

        void OnDestroy()
        {
            foreach (var obj in owned) if (obj) Discard(obj);
            owned.Clear();
        }

        /// <summary>Destroy in Play mode, DestroyImmediate in edit mode (dev tools dress a hole there for screenshots).</summary>
        public static void Discard(Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
