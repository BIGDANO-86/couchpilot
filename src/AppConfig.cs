using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CouchPilot
{
    internal sealed class AppConfig
    {
        // ---- what to launch
        public string FrontendPath { get; set; } = "";

        /// <summary>
        /// The process that actually stays alive, WITHOUT the .exe extension.
        /// This matters: LaunchBox ships a small BigBox.exe stub that starts the
        /// real binary and may exit immediately. Watching the stub would look
        /// like an instant quit, so the long-lived process is tracked instead.
        /// Leave blank to derive it from FrontendPath.
        /// </summary>
        public string WatchProcessName { get; set; } = "";

        // ---- behaviour
        public bool LaunchOnControllerConnect { get; set; } = true;
        public bool SleepOnFrontendExit { get; set; } = true;

        /// <summary>Only sleep if the frontend exited cleanly (exit code 0).</summary>
        public bool RequireCleanExit { get; set; } = true;

        /// <summary>Only sleep if a controller is still connected at exit.</summary>
        public bool RequireControllerForSleep { get; set; } = true;

        /// <summary>Ignore an exit that happens sooner than this, to dodge crash loops.</summary>
        public int MinimumRunSeconds { get; set; } = 30;

        /// <summary>Breathing room before sleeping, so webhooks and shutdown screens finish.</summary>
        public int SleepDelaySeconds { get; set; } = 6;

        public int ControllerPollSeconds { get; set; } = 2;

        /// <summary>Ignore repeat triggers inside this window.</summary>
        public int CooldownSeconds { get; set; } = 45;

        // ---- optional integrations, invisible to anyone who leaves them blank
        public string WebhookOnLaunch { get; set; } = "";
        public string WebhookOnExit { get; set; } = "";
        public int WebhookTimeoutSeconds { get; set; } = 5;

        public bool StartWithWindows { get; set; } = true;

        /// <summary>Show a brief tray balloon when something happens.</summary>
        public bool ShowNotifications { get; set; } = true;

        /// <summary>
        /// Launch when the PC resumes and a controller is already connected.
        /// Off by default: a resume caused by something else (a keyboard, a
        /// wake timer) would otherwise launch the frontend unasked. Normal
        /// controller wakes are already covered by the absent-to-present
        /// transition, because sleeping re-arms that baseline.
        /// </summary>
        public bool LaunchOnWakeWithController { get; set; } = false;

        /// <summary>Set once the first-run settings window has been shown.</summary>
        public bool FirstRunDone { get; set; } = false;

        [JsonIgnore]
        public static string Dir => Log.Dir;

        [JsonIgnore]
        public static string FilePath => Path.Combine(Dir, "config.json");

        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Opts);
                    if (cfg != null)
                    {
                        if (string.IsNullOrWhiteSpace(cfg.FrontendPath))
                            cfg.FrontendPath = DetectFrontend();
                        return cfg;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("config load failed, using defaults: " + ex.Message);
            }

            var fresh = new AppConfig { FrontendPath = DetectFrontend() };
            fresh.Save();
            return fresh;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opts));
            }
            catch (Exception ex)
            {
                Log.Write("config save failed: " + ex.Message);
            }
        }

        /// <summary>Best-effort guess at an installed frontend, in rough order of likelihood.</summary>
        public static string DetectFrontend()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var candidates = new List<string>
            {
                @"C:\Launchbox\BigBox.exe",
                @"C:\LaunchBox\BigBox.exe",
                @"D:\Launchbox\BigBox.exe",
                Path.Combine(local, @"Playnite\Playnite.FullscreenApp.exe"),
                @"C:\Program Files\Playnite\Playnite.FullscreenApp.exe",
                @"C:\Program Files (x86)\Playnite\Playnite.FullscreenApp.exe"
            };

            foreach (var c in candidates)
            {
                try { if (File.Exists(c)) return c; } catch { }
            }
            return "";
        }

        /// <summary>
        /// Which process to actually watch. Explicit setting wins; otherwise map
        /// the known launcher-stub cases, then fall back to the file name.
        /// </summary>
        public string ResolveWatchName()
        {
            if (!string.IsNullOrWhiteSpace(WatchProcessName))
                return WatchProcessName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);

            var name = "";
            try { name = Path.GetFileNameWithoutExtension(FrontendPath) ?? ""; } catch { }

            // BigBox.exe in the LaunchBox root is a stub; the real process is
            // also called BigBox, so the name holds. Playnite's fullscreen app
            // keeps its own name too. Listed explicitly so the intent is clear.
            switch (name.ToLowerInvariant())
            {
                case "bigbox": return "BigBox";
                case "playnite.fullscreenapp": return "Playnite.FullscreenApp";
                case "playnite.desktopapp": return "Playnite.DesktopApp";
                default: return name;
            }
        }
    }
}
