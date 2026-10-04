using GolfSim.Course;
using UnityEngine;

namespace GolfSim.Ball
{
    /// <summary>
    /// The glow-in-the-dark ball for dusk and night holes: its surface glows (bloom turns it into a bright dot you
    /// can follow down the fairway), a small light shows the grass around its lie, and the shot tracer takes the
    /// same colour. Set(0) puts the day ball back.
    /// </summary>
    [RequireComponent(typeof(GolfBall))]
    public class BallGlow : MonoBehaviour
    {
        [ColorUsage(false, true)] public Color glowColor = new Color(0.55f, 1f, 0.35f);
        [Tooltip("HDR emission at full darkness (times glowColor).")] public float strength = 4f;
        [Tooltip("The tracer after dark (HDR): the glow's green, bright enough for the bloom.")]
        [ColorUsage(false, true)] public Color tracerColor = new Color(0.45f, 1.6f, 0.35f);
        public float lightRange = 3.5f, lightIntensity = 1.6f;

        // Not kept over a script reload (the day materials can't be): remembered again on the next Set.
        [System.NonSerialized] Renderer[] renderers;
        [System.NonSerialized] Material[][] dayMaterials;
        Material glowMaterial;
        Light glowLight;
        Color dayTracer;

        /// <summary>0 = the day ball, 1 = full glow at night (SkyPreset.darkness).</summary>
        public float Amount { get; private set; }

        public void Set(float darkness)
        {
            Amount = Mathf.Clamp01(darkness);
            Remember();
            var tracer = GetComponent<BallTracer>();
            if (Amount <= 0f)
            {
                for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterials = dayMaterials[i];
                if (glowLight) glowLight.enabled = false;
                if (tracer) tracer.SetColor(dayTracer);
                return;
            }

            if (!glowMaterial)
            {
                var day = dayMaterials.Length > 0 && dayMaterials[0].Length > 0 ? dayMaterials[0][0] : null;
                glowMaterial = HoleAssets.Glow("Glowing Ball", day ? day.color : Color.white, Color.black);
                glowMaterial.renderQueue = GolfBall.RenderQueue; // seen in the cup, like the day ball (GolfBall.Start)
            }
            glowMaterial.SetColor("_EmissionColor", glowColor * (strength * Amount));
            foreach (var r in renderers)
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int m = 0; m < mats.Length; m++) mats[m] = glowMaterial;
                r.sharedMaterials = mats;
            }

            if (!glowLight)
            {
                glowLight = new GameObject("Ball Glow").AddComponent<Light>();
                glowLight.transform.SetParent(transform, false);
                glowLight.transform.localPosition = Vector3.up * 0.15f; // a little above, so it lights the lie, not the ball's inside
                glowLight.type = LightType.Point;
                glowLight.shadows = LightShadows.None;
                glowLight.renderMode = LightRenderMode.ForcePixel;
            }
            glowLight.enabled = true;
            glowLight.color = glowColor;
            glowLight.range = lightRange;
            glowLight.intensity = lightIntensity * Amount;
            if (tracer) tracer.SetColor(Color.Lerp(dayTracer, tracerColor, Amount));
        }

        void Remember()
        {
            if (renderers != null) return;
            renderers = GetComponentsInChildren<MeshRenderer>();
            dayMaterials = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++) dayMaterials[i] = renderers[i].sharedMaterials;
            var tracer = GetComponent<BallTracer>();
            dayTracer = tracer ? tracer.color : Color.white;
        }

        void OnDestroy()
        {
            if (glowMaterial) Destroy(glowMaterial);
        }
    }
}
