using System;
using UnityEngine;

namespace GolfSim.Course
{
    /// <summary>
    /// Templates for every material the game makes in code (markers, cup, water, tracer and lines, runtime terrain).
    /// A built game only has the shaders, and keyword variants, that some included asset uses, so Shader.Find comes
    /// back null there for a shader only code asks for. The asset at Resources/RuntimeMaterials references these
    /// templates, which keeps them in every build; code clones them. Tools/unity_scripts/SetupRuntimeMaterials.cs makes them.
    /// </summary>
    [CreateAssetMenu(menuName = "Golf/Runtime Materials", fileName = "RuntimeMaterials")]
    public class RuntimeMaterials : ScriptableObject
    {
        public const string ResourceName = "RuntimeMaterials";

        [Tooltip("Opaque URP Lit: tee markers, flagstick, flag.")]
        public Material lit;
        [Tooltip("Transparent glossy URP Lit: pond water when a theme has none.")]
        public Material litTransparent;
        [Tooltip("Alpha-blended URP Particles/Unlit: tracer, aim line, putt preview, replay ripples.")]
        public Material line;
        public Material cupMask;
        public Material cupInterior;
        [Tooltip("URP Terrain Lit for holes built while the game runs (the pipeline's default terrain material is editor-only).")]
        public Material terrain;

        static RuntimeMaterials loaded;

        public static RuntimeMaterials Load()
        {
            if (!loaded) loaded = Resources.Load<RuntimeMaterials>(ResourceName);
            return loaded;
        }

        /// <summary>The template `pick` chooses (shared, don't modify it), or null after logging an error.</summary>
        public static Material Template(Func<RuntimeMaterials, Material> pick)
        {
            var library = Load();
            var template = library ? pick(library) : null;
            if (!template) Debug.LogError($"[RuntimeMaterials] A template is missing from Resources/{ResourceName}; run SetupRuntimeMaterials.");
            return template;
        }

        /// <summary>
        /// A new material copied from the template `pick` chooses. Never throws: without the template it is the error
        /// shader (magenta), so whatever draws it carries on.
        /// </summary>
        public static Material Create(Func<RuntimeMaterials, Material> pick, string name)
        {
            var template = Template(pick);
            var mat = template ? new Material(template)
                               : new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Hidden/Universal Render Pipeline/FallbackError"));
            mat.name = name;
            return mat;
        }
    }
}
