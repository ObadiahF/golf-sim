using UnityEngine;

namespace GolfSim.Course
{
    /// <summary>
    /// Dresses the hole on screen for a time of day: the light and sky (SkyLighting) in its theme's air
    /// (ThemeScenery), the night kit after dark (NightKit) and whatever floats in the air (AirParticles).
    /// The ball's glow is the game's (GolfSim.Ball.BallGlow), driven by the returned rig's darkness.
    /// </summary>
    public static class Scenery
    {
        public static SceneryRig Apply(HoleInfo hole, TimeOfDay time)
        {
            var rig = SceneryRig.Fresh();
            rig.time = time;
            rig.theme = hole.theme;
            var preset = SkyPreset.For(time);
            var theme = ThemeScenery.For(hole.theme);
            SkyLighting.Apply(rig, preset, theme, hole.PinWorld - hole.TeeWorld);
            if (preset.IsDark) NightKit.Build(rig, hole, preset.darkness);
            AirParticles.Create(rig, theme.air, preset);
            Debug.Log($"[Scenery] {theme.label} at {time.Label().ToLowerInvariant()} ({hole.name})");
            return rig;
        }
    }
}
