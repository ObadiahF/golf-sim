using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// Shared setup for the effects made in code (CupFireworks, WaterSplash): their materials from Resources/Effects
    /// (Tools/unity_scripts/SetupEffects.cs) and a one-shot particle system with the common settings.
    /// </summary>
    public static class Particles
    {
        public const string Spark = "Effects/Spark", SparkDay = "Effects/SparkDay", Ripple = "Effects/Ripple";
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public static Material Load(string path)
        {
            var m = Resources.Load<Material>(path);
            if (!m) Debug.LogWarning($"[Particles] No material at Resources/{path}: run Tools/unity_scripts/SetupEffects.cs.");
            return m;
        }

        /// <summary>
        /// A stopped, non-looping particle system at `at` (world space, unscaled time), drawn with `material` at
        /// `intensity` (above 1: HDR, so bloom makes it glow). Configure it, then Play.
        /// </summary>
        public static ParticleSystem Make(string name, Transform parent, Vector3 at, Material material, Color color, float intensity = 1f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // configure before it plays
            var main = ps.main;
            main.loop = false;
            main.duration = 1f;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.maxParticles = 400;
            main.startColor = color;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColor, new Color(intensity, intensity, intensity, 1f));
            renderer.SetPropertyBlock(block);
            return ps;
        }

        /// <summary>All of its particles at once when it plays.</summary>
        public static void Burst(ParticleSystem ps, int count, float at = 0f)
        {
            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(at, (short)count) });
        }

        /// <summary>Full opacity until `hold` of its life, then fading out.</summary>
        public static void FadeOut(ParticleSystem ps, float hold = 0.55f, float startAlpha = 1f)
        {
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(startAlpha, hold), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
        }

        /// <summary>Grows (or shrinks) from `from` to `to` times its start size over its life.</summary>
        public static void Grow(ParticleSystem ps, float from, float to, float exponent = 1f)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            var curve = new AnimationCurve();
            for (int i = 0; i <= 8; i++)
            {
                float u = i / 8f;
                curve.AddKey(u, Mathf.Lerp(from, to, Mathf.Pow(u, exponent)));
            }
            size.size = new ParticleSystem.MinMaxCurve(Mathf.Max(from, to), Scale(curve, 1f / Mathf.Max(from, to)));
        }

        static AnimationCurve Scale(AnimationCurve c, float k)
        {
            var keys = c.keys;
            for (int i = 0; i < keys.Length; i++) keys[i].value *= k;
            return new AnimationCurve(keys);
        }
    }
}
