using UnityEngine;
using UnityEngine.Rendering;

namespace GolfSim.Course
{
    /// <summary>
    /// Fireflies after sunset, or falling snow, in a box that rides along with the camera (the particles stay where
    /// they were born, so flying over the hole passes through them). Only near the camera, where they show:
    /// a hole-wide cloud would cost thousands of particles to look the same.
    /// </summary>
    public class AirParticles : MonoBehaviour
    {
        const float BoxSize = 90f;

        /// <summary>Adds the theme's air life to the rig: snow any time, fireflies only when dark. Null when there is none.</summary>
        public static AirParticles Create(SceneryRig rig, AirLife air, SkyPreset preset)
        {
            if (air == AirLife.None || air == AirLife.Fireflies && !preset.IsDark) return null;
            var go = rig.Child(air.ToString()).gameObject;
            var particles = go.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // configure first, then play
            if (air == AirLife.Snow) Snow(particles);
            else Fireflies(particles, preset.darkness);

            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            var main = particles.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.prewarm = true;
            main.playOnAwake = true;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var material = rig.Own(RuntimeMaterials.Create(m => m.line, $"{air} (Scenery)"));
            material.SetTexture("_BaseMap", rig.Own(SoftDot()));
            material.SetColor("_BaseColor", air == AirLife.Snow ? new Color(1f, 1f, 1f, 0.9f) : new Color(2.6f, 3f, 1.1f, 1f)); // fireflies: HDR for the bloom
            renderer.sharedMaterial = material;

            var follow = go.AddComponent<AirParticles>();
            particles.Play();
            return follow;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam) transform.position = cam.transform.position;
        }

        static void Fireflies(ParticleSystem particles, float darkness)
        {
            var main = particles.main;
            main.maxParticles = 600;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 8f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.13f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.9f, 1f, 0.5f), new Color(1f, 0.85f, 0.35f));
            var emission = particles.emission;
            emission.rateOverTime = 90f * darkness;
            var shape = particles.shape;
            shape.scale = new Vector3(BoxSize, 3f, BoxSize);
            shape.position = new Vector3(0f, -1.5f, 0f); // knee to head height around a camera standing on the ground
            var noise = particles.noise;
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.2f;
            // Each one blinks: dark, a slow glow up, a pulse, dark again.
            var color = particles.colorOverLifetime;
            color.enabled = true;
            var blink = new Gradient();
            blink.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0f, 0.25f), new GradientAlphaKey(1f, 0.4f),
                                  new GradientAlphaKey(0.2f, 0.55f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 0.9f) });
            color.color = blink;
        }

        static void Snow(ParticleSystem particles)
        {
            var main = particles.main;
            main.maxParticles = 4000;
            main.startLifetime = 14f;
            main.startSpeed = 0f;
            main.gravityModifier = 0.012f; // settles at ~1.2 m/s with the drag below: big lazy flakes
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            var emission = particles.emission;
            emission.rateOverTime = 260f;
            var shape = particles.shape;
            shape.scale = new Vector3(BoxSize, 1f, BoxSize);
            shape.position = new Vector3(0f, 14f, 0f);
            var drag = particles.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = 1.2f;
            drag.dampen = 0.2f;
            var noise = particles.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 0.15f;
            var fade = particles.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
        }

        /// <summary>A small round falloff: a glowing point for a firefly, a soft flake for snow.</summary>
        static Texture2D SoftDot()
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Soft Dot" };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f).magnitude / (size / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a * a);
                }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
