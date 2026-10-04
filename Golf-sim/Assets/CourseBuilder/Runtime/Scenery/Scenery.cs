using UnityEngine;

namespace GolfSim.Course
{
    /// <summary>
    /// Dresses the hole on screen for a time of day: the light and sky (SkyLighting) in its theme's air
    /// (ThemeScenery), the theme's water colours, the night kit after dark (NightKit) and whatever floats in the air
    /// (AirParticles). The ball's glow is the game's (GolfSim.Ball.BallGlow), driven by the returned rig's darkness.
    /// </summary>
    public static class Scenery
    {
        static readonly int ShallowColor = Shader.PropertyToID("_ShallowColor"), DeepColor = Shader.PropertyToID("_DeepColor");

        public static SceneryRig Apply(HoleInfo hole, TimeOfDay time)
        {
            var rig = SceneryRig.Fresh();
            rig.time = time;
            rig.theme = hole.theme;
            var preset = SkyPreset.For(time);
            var theme = ThemeScenery.For(hole.theme);
            SkyLighting.Apply(rig, preset, theme, hole.PinWorld - hole.TeeWorld);
            TintWater(hole, theme);
            if (preset.IsDark) NightKit.Build(rig, hole, preset.darkness);
            AirParticles.Create(rig, theme.air, preset);
            Debug.Log($"[Scenery] {theme.label} at {time.Label().ToLowerInvariant()} ({hole.name})");
            return rig;
        }

        /// <summary>A hole left as built (practice): forget the last round's glint, so its water follows the scene's sun.</summary>
        public static void Clear() => Shader.SetGlobalVector(SkyLighting.GlintDirection, Vector4.zero);

        /// <summary>The theme's pond colours, per renderer (the water material is shared by every hole).</summary>
        static void TintWater(HoleInfo hole, ThemeScenery theme)
        {
            if (theme.waterShallow == null && theme.waterDeep == null) return;
            var block = new MaterialPropertyBlock();
            if (theme.waterShallow is Color shallow) block.SetColor(ShallowColor, shallow);
            if (theme.waterDeep is Color deep) block.SetColor(DeepColor, deep);
            foreach (var renderer in hole.GetComponentsInChildren<MeshRenderer>())
                if (renderer.name.StartsWith(WaterBuilder.NamePrefix)) renderer.SetPropertyBlock(block);
        }
    }
}
