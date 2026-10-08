using System.Collections.Generic;
using GolfSim.Ball;
using GolfSim.Net;
using UnityEngine;

namespace GolfSim.Game
{
    // What the Settings screen lists, section by section. Each row reads and writes its setting directly (GameAudio,
    // GameSettings, PuttPreview), so the screen in the pause menu and the one on the main menu always agree.
    public partial class SettingsScreen
    {
        static IEnumerable<(string title, SettingRow[] rows)> Sections()
        {
            yield return ("Sound", SoundSettings.Rows());
            yield return ("Gameplay", new SettingRow[]
            {
                ChoiceSetting.For("Wind", new[] { "Off", "Light", "Normal", "Strong" }, () => GameSettings.Wind, v => GameSettings.Wind = v,
                                  v => v == WindStrength.Off ? "Every hole is calm." : $"{v} wind on every hole; the HUD and the phones show it."),
                ChoiceSetting.For("Putting assist", new[] { "Full", "Partial", "Off" }, () => GameSettings.PuttingAssist, v => GameSettings.PuttingAssist = v,
                                  v => v switch
                                  {
                                      PuttingAssist.Full => "The whole break line, to the cup.",
                                      PuttingAssist.Partial => "The first part of the break line.",
                                      _ => "No line: read the green yourself.",
                                  }),
                new ChoiceSetting("Round length", new[] { "9 holes", "18 holes" },
                                  () => GameSettings.RoundLength == GameSettings.RoundLengths[1] ? 1 : 0,
                                  i => GameSettings.RoundLength = GameSettings.RoundLengths[i],
                                  _ => "Rounds started on the TV. A game started from the phone keeps its own length."),
            });
            yield return ("Display", new SettingRow[]
            {
                ChoiceSetting.For("Quality", new[] { "High", "Performance" }, () => GameSettings.Quality, v => GameSettings.Quality = v,
                                  v => v == GraphicsQuality.High ? "Sharpest shadows and smooth edges."
                                                                 : "Lighter shadows and no anti-aliasing, for slower PCs."),
                ChoiceSetting.Toggle("Fullscreen", () => GameSettings.Fullscreen, v => GameSettings.Fullscreen = v,
                                     _ => Application.isEditor ? "Applies in the built game (the editor's Game view stays as it is)." : null),
            });
            yield return ("Connection", new SettingRow[]
            {
                new InfoRow("Room", () => RoomBadge.CodeText, () => "Phones type this code once, in the app's Settings."),
                new InfoRow("Server", () => ServerHost, () => RoomBadge.StatusText(SimConnection.Instance)),
            });
            yield return ("About", new SettingRow[]
            {
                new InfoRow("Version", () => BuildInfo.Label),
                new ActionRow("Updates", "Check for updates", CheckForUpdates, UpdateStatus),
            });
            yield return ("Developer", new SettingRow[]
            {
                ChoiceSetting.Toggle("Developer modes", () => GameSettings.DeveloperModes, v => GameSettings.DeveloperModes = v,
                                     on => on ? "Hole Simulator is under Practice." : "Shows Hole Simulator (for the AI course work) under Practice."),
            });
        }

        /// <summary>The game server's host, e.g. golf-server.obadiahfusco.xyz (never the token).</summary>
        static string ServerHost
        {
            get
            {
                string url = ServerConfig.Load().ActiveUrl;
                int scheme = url.IndexOf("://", System.StringComparison.Ordinal);
                return scheme >= 0 ? url.Substring(scheme + 3) : url;
            }
        }

        static void CheckForUpdates()
        {
            if (RoundDirector.Instance) GameUpdater.Check(RoundDirector.Instance, force: true);
        }

        static string UpdateStatus() => GameUpdater.State switch
        {
            GameUpdater.Status.Off => "Updates come with the installed game (not in the editor or a development build).",
            GameUpdater.Status.Checking => "Checking…",
            GameUpdater.Status.Available or GameUpdater.Status.Failed when GameUpdater.Latest != null =>
                $"Version {GameUpdater.Latest.version} is ready: install it from the Update card on the main menu.",
            GameUpdater.Status.Downloading or GameUpdater.Status.Installing => "Updating…",
            GameUpdater.Status.UpToDate when !string.IsNullOrEmpty(GameUpdater.Error) => $"Couldn't check ({GameUpdater.Error}).",
            GameUpdater.Status.UpToDate => "This is the latest version.",
            _ => "Not checked yet.",
        };
    }
}
