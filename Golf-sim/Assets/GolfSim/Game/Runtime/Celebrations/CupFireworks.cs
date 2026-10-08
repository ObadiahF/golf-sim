using System.Collections;
using UnityEngine;

namespace GolfSim.Game
{
    /// <summary>
    /// Fireworks over the green for an eagle, an albatross or a hole-in-one: rockets climb from around the cup with
    /// a sparkling trail, whistle, and burst into coloured, trailing sparks that fall and fade (with a boom and
    /// crackle). The sparks are HDR so bloom makes them glow; additive after dark, plain blended by day so they show
    /// against a bright sky. Everything removes itself afterwards. Materials: Resources/Celebration
    /// (Tools/unity_scripts/SetupCelebration.cs).
    /// </summary>
    public class CupFireworks : MonoBehaviour
    {
        const string NightMaterial = "Celebration/Spark", DayMaterial = "Celebration/SparkDay";
        const float Climb = 0.95f;   // s from launch to burst
        const float Stagger = 0.55f; // s between rockets
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        static readonly Color[] Colors =
        {
            new Color(1f, 0.18f, 0.15f), new Color(1f, 0.75f, 0.1f), new Color(0.2f, 0.6f, 1f),
            new Color(0.25f, 1f, 0.35f), new Color(0.8f, 0.3f, 1f), new Color(1f, 0.4f, 0.75f),
        };

        bool night;
        Material material;

        /// <summary>Sets off `count` rockets around the pin.</summary>
        public static void Launch(Vector3 pin, int count, bool night)
        {
            var go = new GameObject("Cup Fireworks");
            go.transform.position = pin;
            var fireworks = go.AddComponent<CupFireworks>();
            fireworks.night = night;
            fireworks.material = Resources.Load<Material>(night ? NightMaterial : DayMaterial);
            if (!fireworks.material)
            {
                Debug.LogWarning("[CupFireworks] No spark material in Resources/Celebration: no fireworks.");
                Destroy(go);
                return;
            }
            for (int i = 0; i < count; i++) fireworks.StartCoroutine(fireworks.Rocket(i * Stagger + Random.Range(0f, 0.15f)));
            Destroy(go, count * Stagger + Climb + 4f);
        }

        IEnumerator Rocket(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            var ground = transform.position;
            var side = Random.insideUnitCircle;
            var from = ground + new Vector3(side.x, 0f, side.y) * 6f;
            var to = ground + new Vector3(side.x, 0f, side.y) * 14f + Vector3.up * Random.Range(24f, 34f);
            var color = Colors[Random.Range(0, Colors.Length)];

            var trail = Trail(from, color);
            GameAudio.Play(SoundId.FireworkLaunch, from);
            for (float t = 0f; t < Climb; t += Time.unscaledDeltaTime)
            {
                float u = t / Climb;
                trail.transform.position = Vector3.Lerp(from, to, 1f - (1f - u) * (1f - u)); // slowing as it climbs
                yield return null;
            }
            trail.transform.position = to;
            trail.GetComponent<ParticleSystem>().Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(trail, 1.5f);

            Burst(to, color);
            GameAudio.Play(SoundId.FireworkBurst, to, Random.Range(0.8f, 1f));
        }

        /// <summary>The rocket: a bright head leaving short-lived sparks behind it.</summary>
        GameObject Trail(Vector3 at, Color color)
        {
            var (go, ps) = NewSystem("Rocket", at, Color.Lerp(color, Color.white, 0.6f), night ? 2.4f : 1.25f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.gravityModifier = 0.3f;
            var emission = ps.emission;
            emission.rateOverDistance = 14f;
            emission.rateOverTime = 20f;
            FadeOut(ps);
            ps.Play();
            return go;
        }

        /// <summary>The burst: a shell of sparks flying out, dragged to a stop, falling and fading with trails.</summary>
        void Burst(Vector3 at, Color color)
        {
            var (go, ps) = NewSystem("Burst", at, color, night ? 2.4f : 1.25f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(15f, 21f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.5f, 2.3f);
            main.gravityModifier = 0.25f;
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, 0.2f));
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Random.Range(170, 230)) });
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = 1000f; // no speed cap (its default is 1 m/s): only the drag slows them
            drag.dampen = 0f;
            drag.drag = 1.2f;
            drag.multiplyDragByParticleSize = false;
            drag.multiplyDragByParticleVelocity = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
            FadeOut(ps);
            var trails = ps.trails;
            trails.enabled = true;
            trails.lifetime = 0.3f;
            trails.minVertexDistance = 0.3f;
            trails.inheritParticleColor = true;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(0.5f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            go.GetComponent<ParticleSystemRenderer>().trailMaterial = material;
            ps.Play();
            Destroy(go, 3f);
        }

        (GameObject, ParticleSystem) NewSystem(string name, Vector3 at, Color color, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
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
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColor, new Color(intensity, intensity, intensity, 1f)); // HDR: the sparks bloom
            renderer.SetPropertyBlock(block);
            return (go, ps);
        }

        static void FadeOut(ParticleSystem ps)
        {
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
        }
    }
}
