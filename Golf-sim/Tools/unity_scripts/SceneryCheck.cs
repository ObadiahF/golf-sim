// Dev check (edit mode, the HoleSimulator scene open), run with the Unity CLI (not compiled into the project):
//   unity command run_script --file Tools/unity_scripts/SceneryCheck.cs --entry SceneryCheck.Check
//   echo "<hole package folder>,<theme>,<time>,<out folder>" > Temp/scenery_args.txt
//   unity command run_script --file Tools/unity_scripts/SceneryCheck.cs --entry SceneryCheck.Shots
//   unity command run_script --file Tools/unity_scripts/SceneryCheck.cs --entry SceneryCheck.Revert
// Check: the scenery pieces without Play mode. SkySchedule (deterministic, never goes back toward day, fixed choices),
// every theme has its scenery row and a baked runtime theme, and per time of day on a hole built from a package:
// the sun, sky, ambient and fog match the preset (a night sky's horizon is the fog colour), the sky reflection probe is
// rendered after the day, the ponds are GolfSim/Water, the night kit (floodlights, cup ring, glowing flag) is there only
// after dark, and the glowing ball glows and goes back to the day ball.
// Shots: builds the package's hole dressed as <theme> (any theme, whatever the package asks for) at <time> (Day,
// GoldenHour, Dusk, Night) and saves up to four views as PNGs: from behind the tee, the ball on the tee up close, the
// green from the fairway, and across the biggest pond toward the moon or sun (its reflection). Particles (fireflies,
// snow) don't run in edit mode. Revert reopens the scene unsaved.
// Play mode (GolfServer autoConnect off first): PlayNight starts a one-hole night round, PlayState reports it,
// PlayShot hits a shot and flies it 1.5 s (capture the game view to see the glowing ball and tracer).
using System;
using System.IO;
using System.Linq;
using System.Text;
using GolfSim.Ball;
using GolfSim.Course;
using GolfSim.CourseEditor;
using GolfSim.Game;
using GolfSim.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class SceneryCheck
{
    const string ArgsPath = "Temp/scenery_args.txt";
    const int Width = 1600, Height = 900;

    public static string Check()
    {
        var log = new StringBuilder();
        int fails = 0;
        void Expect(bool ok, string what)
        {
            log.AppendLine($"{(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) fails++;
        }

        // The schedule: deterministic, monotonic toward night, fixed choices fixed.
        var plain = ThemeScenery.For("parkland");
        bool deterministic = true, monotonic = true, someDark = false, someDay = false;
        for (uint seed = 1; seed < 400; seed++)
        {
            var prev = TimeOfDay.Day;
            for (int h = 0; h < 18; h++)
            {
                var t = SkySchedule.For(seed, h, plain);
                deterministic &= t == SkySchedule.For(seed, h, plain);
                if (h > 0) monotonic &= t >= prev;
                prev = t;
                someDark |= t >= TimeOfDay.Dusk;
                someDay |= h == 0 && t == TimeOfDay.Day;
            }
        }
        Expect(deterministic, "SkySchedule gives a hole the same time every time");
        Expect(monotonic, "SkySchedule never goes back toward day within a round");
        Expect(someDark && someDay, "Auto rounds start in daylight sometimes and reach dusk or night sometimes");
        Expect(TimeOfDayNames.All.All(t => SkySchedule.For((SkyChoice)(t + 1), 7, 5, plain) == t), "a fixed choice is that time on every hole");

        // Every catalog theme has a scenery row and a baked runtime theme.
        var themes = CourseRound.Load().themes;
        foreach (var name in DefaultThemes.Names)
        {
            Expect(ThemeScenery.For(name).theme == name, $"theme '{name}' has a ThemeScenery row ({ThemeScenery.For(name).label})");
            Expect(themes && themes.themes.Any(t => t.name == name), $"theme '{name}' is baked into {(themes ? themes.name : "RuntimeThemes")}");
        }

        // Each time of day on a built hole.
        var hole = Build(FirstPackage(), null);
        var ball = UnityEngine.Object.FindAnyObjectByType<GolfBall>();
        foreach (var time in TimeOfDayNames.All)
        {
            var preset = SkyPreset.For(time);
            Scenery.Apply(hole, time);
            var sun = SkyLighting.FindSun();
            Expect(sun && Mathf.Abs(Vector3.Angle(-sun.transform.forward, Vector3.up) - (90f - preset.elevation)) < 0.5f,
                   $"{time}: the sun stands {preset.elevation}° up");
            Expect(Mathf.Approximately(RenderSettings.fogDensity, preset.fogDensity * ThemeScenery.For(hole.theme).haze), $"{time}: fog density");
            Expect(RenderSettings.ambientMode == (preset.skyAmbient ? AmbientMode.Skybox : AmbientMode.Trilight), $"{time}: ambient mode");
            Expect(RenderSettings.skybox && (RenderSettings.skybox.shader.name == "GolfSim/NightSky") == !preset.proceduralSky,
                   $"{time}: sky is {(RenderSettings.skybox ? RenderSettings.skybox.shader.name : "none")}");
            Expect(RenderSettings.fogColor == (preset.proceduralSky ? RenderSettings.fogColor : RenderSettings.skybox.GetColor("_HorizonColor")),
                   $"{time}: the sky's horizon is the fog colour");
            var probe = GameObject.Find("Scenery/Sky Reflection");
            Expect((probe != null) == (time != TimeOfDay.Day), $"{time}: sky reflection probe {(probe ? "rendered" : "absent (the scene's own)")}");
            var water = hole.GetComponentsInChildren<MeshRenderer>().Where(r => r.name.StartsWith(WaterBuilder.NamePrefix)).ToArray();
            Expect(water.All(r => r.sharedMaterial.shader.name == "GolfSim/Water"), $"{time}: {water.Length} ponds drawn with GolfSim/Water");
            var kit = GameObject.Find("Scenery/Night Kit");
            Expect((kit != null) == preset.IsDark, $"{time}: night kit {(kit ? "built" : "absent")}");
            if (kit)
            {
                Expect(kit.GetComponentsInChildren<Light>().Count(l => l.type == LightType.Spot) >= 2, $"{time}: floodlights on the green and the tee");
                Expect(kit.transform.Find("Cup Ring"), $"{time}: glowing cup ring");
                var flag = hole.GetComponentInChildren<FlagWave>().GetComponent<Renderer>().sharedMaterial;
                Expect(flag.IsKeywordEnabled("_EMISSION") && flag.GetColor("_EmissionColor").maxColorComponent > 1f, $"{time}: the flag glows");
                log.AppendLine($"     {kit.transform.Find("Lanterns")?.childCount ?? 0} lanterns");
            }
            if (ball)
            {
                var glow = ball.GetComponent<BallGlow>() ?? ball.gameObject.AddComponent<BallGlow>();
                glow.Set(preset.darkness);
                var mat = ball.GetComponentInChildren<MeshRenderer>().sharedMaterial;
                bool glowing = mat.IsKeywordEnabled("_EMISSION") && mat.GetColor("_EmissionColor").maxColorComponent > 0.5f;
                Expect(glowing == preset.IsDark, $"{time}: ball {(glowing ? "glows" : "is the day ball")}");
            }
        }
        return $"{(fails == 0 ? "PASS" : $"{fails} FAILED")}\n{log}";
    }

    public static string Shots()
    {
        var a = File.ReadAllText(ArgsPath).Trim().Split(',');
        string folder = a[0], theme = a[1], output = a[3];
        var time = (TimeOfDay)Enum.Parse(typeof(TimeOfDay), a[2]);
        Revert(); // the scene as saved: its own sky and light, no hole from an earlier shot
        var hole = Build(folder, theme);
        Scenery.Apply(hole, time);
        var preset = SkyPreset.For(time);

        var ball = UnityEngine.Object.FindAnyObjectByType<GolfBall>();
        var forward = Vector3.ProjectOnPlane(hole.PinWorld - hole.TeeWorld, Vector3.up).normalized;
        if (ball)
        {
            ball.transform.position = hole.TeeWorld + forward * 0.5f + Vector3.up * 0.03f;
            (ball.GetComponent<BallGlow>() ?? ball.gameObject.AddComponent<BallGlow>()).Set(preset.darkness);
        }

        Directory.CreateDirectory(output);
        string tag = $"{theme}_{time}".ToLowerInvariant();
        var tee = hole.TeeWorld;
        Render(tee - forward * 9f + Vector3.up * 3.2f, tee + forward * 120f + Vector3.up * 8f, Path.Combine(output, $"{tag}_tee.png"));
        if (ball) Render(ball.transform.position - forward * 1.2f + Vector3.up * 0.45f, ball.transform.position + forward * 3f, Path.Combine(output, $"{tag}_ball.png"));
        var pin = hole.PinWorld;
        var approach = pin - forward * 20f + Vector3.up * 3.5f;
        approach.y = Mathf.Max(approach.y, Ground(approach) + 2.5f);
        Render(approach, pin + Vector3.up * 0.8f, Path.Combine(output, $"{tag}_green.png"));
        if (WaterView(hole, out var from, out var to)) Render(from, to, Path.Combine(output, $"{tag}_water.png"));
        return $"saved {tag}_*.png to {output} ({hole.name})";
    }

    /// <summary>Play mode (server off: GolfServer autoConnect off): a one-hole round from the Night Golf card.</summary>
    public static string PlayNight()
    {
        if (!Application.isPlaying) return "enter Play mode first";
        RoundDirector.PlayFromMenu(CourseCatalog.Find("night"), 1, SkyChoice.Night);
        return "night round starting: run PlayState once the hole is up";
    }

    /// <summary>Play mode: what the round's hole looks like now (time of day, rig, the ball's glow).</summary>
    public static string PlayState()
    {
        var d = RoundDirector.Instance;
        var rig = SceneryRig.Current;
        var glow = d && d.Ball ? d.Ball.GetComponent<BallGlow>() : null;
        var tracer = d && d.Ball ? d.Ball.GetComponent<BallTracer>() : null;
        return $"round={(d?.Round != null)} sky={d?.Sky} rig={(rig ? $"{rig.time} ({rig.theme})" : "none")} " +
               $"glow={(glow ? glow.Amount : 0f):0.00} light={(glow ? glow.GetComponentInChildren<Light>()?.enabled : null)} " +
               $"skybox={RenderSettings.skybox?.shader.name} fog={RenderSettings.fogDensity:0.0000} " +
               $"lanterns={GameObject.Find("Scenery/Night Kit/Lanterns")?.transform.childCount ?? 0} " +
               $"air={(rig ? rig.GetComponentInChildren<ParticleSystem>()?.particleCount : null)}";
    }

    /// <summary>Play mode: hits the suggested club and flies it 1.5 s (capture the game view next: the glowing ball and its tracer).</summary>
    public static string PlayShot()
    {
        var ball = RoundDirector.Instance.Ball;
        var c = Clubs.Find(RoundDirector.Instance.Club);
        var ack = GolfSim.Ball.Shots.Submit(new RemoteShotMessage
        {
            type = "shot", id = UnityEngine.Random.Range(1, int.MaxValue), club = c.name, speed = c.shot.ballSpeed, launch = c.shot.launchAngle,
            azimuth = 0f, back = c.shot.backspin, side = c.shot.sidespin,
        }, "SceneryCheck", out _);
        if (ack.status != "ok") return $"FAIL not hit: {ack.status} {ack.message}";
        for (int i = 0; i < 75 && ball.InMotion; i++) ball.Advance(0.02f);
        var line = ball.GetComponent<BallTracer>();
        var glow = ball.GetComponent<BallGlow>();
        return $"in flight at {ball.transform.position}; tracer {(line ? line.Hidden ? "hidden" : "drawn" : "none")}, glow {(glow ? glow.Amount : 0f):0.00}";
    }

    /// <summary>Reopens the hole scene, dropping everything the shots changed.</summary>
    public static string Revert()
    {
        var path = EditorSceneManager.GetActiveScene().path;
        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        return $"reopened {path}";
    }

    static HoleInfo Build(string folder, string theme)
    {
        var pkg = HolePackage.Load(Path.Combine(folder, HolePackage.FileName));
        if (!string.IsNullOrEmpty(theme)) pkg.theme = theme;
        return RuntimeHoleBuilder.Build(pkg, CourseRound.Load().themes);
    }

    /// <summary>The first cached hole with a pond (so the water is checked too), else the first one.</summary>
    static string FirstPackage()
    {
        var all = Directory.GetDirectories(TrainerHoles.CacheFolder).Where(d => File.Exists(Path.Combine(d, HolePackage.FileName))).ToArray();
        return all.FirstOrDefault(d => HolePackage.Load(Path.Combine(d, HolePackage.FileName)).water.Length > 0) ?? all.First();
    }

    /// <summary>
    /// Across the biggest pond toward the moon (night sky) or the sun: where its reflection and glint path are. Uses only
    /// what any version of the scene has (pond objects by name, the sky's _MoonDirection, the sun), so before/after
    /// shots stand at the same spot.
    /// </summary>
    static bool WaterView(HoleInfo hole, out Vector3 from, out Vector3 to)
    {
        from = to = default;
        var pond = hole.GetComponentsInChildren<MeshRenderer>().Where(r => r.name.StartsWith("Water "))
                       .OrderByDescending(r => r.bounds.size.x * r.bounds.size.z).FirstOrDefault();
        if (!pond) return false;
        var sky = RenderSettings.skybox;
        var sun = SkyLighting.FindSun();
        var light = sky && sky.HasProperty("_MoonDirection") ? (Vector3)sky.GetVector("_MoonDirection") : sun ? -sun.transform.forward : Vector3.forward;
        var flat = Vector3.ProjectOnPlane(light, Vector3.up).normalized;
        var b = pond.bounds;
        float reach = Mathf.Max(b.extents.x, b.extents.z);
        from = b.center - flat * (reach * 0.8f + 4f);
        from.y = Mathf.Max(b.max.y, Ground(from)) + 3.5f;
        to = from + Quaternion.AngleAxis(16f, Vector3.Cross(Vector3.up, flat)) * flat * 20f; // 16° down: where the light's reflection lies
        return true;
    }

    static float Ground(Vector3 p)
    {
        var terrain = Terrain.activeTerrain;
        return terrain ? terrain.SampleHeight(p) + terrain.transform.position.y : 0f;
    }

    static void Render(Vector3 from, Vector3 lookAt, string path)
    {
        var cam = Camera.main;
        cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(lookAt - from));
        cam.aspect = (float)Width / Height; // the Game view's aspect would stretch the shot
        var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
        var previous = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = previous;
        cam.ResetAspect();
        var active = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        tex.Apply();
        RenderTexture.active = active;
        RenderTexture.ReleaseTemporary(rt);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
    }
}
