using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CouchPilot
{
    internal enum DisconnectAction { None = 0, Run = 1, Sleep = 2 }

    internal sealed class MqttSettings
    {
        public bool Enabled { get; set; } = false;
        public string Host { get; set; } = "";
        public int Port { get; set; } = 1883;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string ClientId { get; set; } = "couchpilot";
        public string TopicLaunch { get; set; } = "couchpilot/launch";
        public string TopicExit { get; set; } = "couchpilot/exit";
        public string PayloadLaunch { get; set; } = "ON";
        public string PayloadExit { get; set; } = "OFF";
        public bool Retain { get; set; } = false;
    }

    internal sealed class AppConfig
    {
        // ---- frontends
        public List<FrontendEntry> Frontends { get; set; } = new List<FrontendEntry>();

        /// <summary>Name of the frontend used when the chooser is off or times out.</summary>
        public string DefaultFrontend { get; set; } = "";

        /// <summary>Offer a controller-navigable picker when more than one is enabled.</summary>
        public bool ShowChooser { get; set; } = true;

        /// <summary>The chooser picks the default by itself after this long. 0 waits forever.</summary>
        public int ChooserTimeoutSeconds { get; set; } = 10;

        // ---- behaviour
        public bool LaunchOnControllerConnect { get; set; } = true;
        public bool SleepOnFrontendExit { get; set; } = true;
        public bool RequireCleanExit { get; set; } = true;
        public bool RequireControllerForSleep { get; set; } = true;
        public int MinimumRunSeconds { get; set; } = 30;
        public int SleepDelaySeconds { get; set; } = 6;
        public int ControllerPollSeconds { get; set; } = 2;
        public int CooldownSeconds { get; set; } = 45;

        // ---- what to do when the pad goes away
        public DisconnectAction OnDisconnect { get; set; } = DisconnectAction.None;
        public string DisconnectPath { get; set; } = "";
        public string DisconnectArguments { get; set; } = "";

        /// <summary>
        /// Controllers idle off after a quarter of an hour or so, so this needs a
        /// generous wait or putting the pad down mid film would fire it.
        /// </summary>
        public int DisconnectDelaySeconds { get; set; } = 120;

        /// <summary>Skip the disconnect action while the frontend is still running.</summary>
        public bool DisconnectOnlyWhenFrontendClosed { get; set; } = true;

        // ---- optional integrations
        public string WebhookOnLaunch { get; set; } = "";
        public string WebhookOnExit { get; set; } = "";
        public int WebhookTimeoutSeconds { get; set; } = 5;
        public MqttSettings Mqtt { get; set; } = new MqttSettings();

        // ---- app
        public bool StartWithWindows { get; set; } = true;
        public bool ShowNotifications { get; set; } = true;
        public bool LaunchOnWakeWithController { get; set; } = false;
        public bool FirstRunDone { get; set; } = false;

        // ---- legacy, read once then migrated into Frontends
        public string FrontendPath { get; set; } = "";
        public string WatchProcessName { get; set; } = "";

        [JsonIgnore] public static string Dir => Log.Dir;
        [JsonIgnore] public static string FilePath => Path.Combine(Dir, "config.json");

        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static AppConfig Load()
        {
            AppConfig cfg = null;
            try
            {
                if (File.Exists(FilePath))
                    cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Opts);
            }
            catch (Exception ex)
            {
                Log.Write("config load failed, starting fresh: " + ex.Message);
            }

            cfg ??= new AppConfig();
            cfg.Migrate();

            if (cfg.Frontends.Count == 0)
            {
                cfg.Frontends = Frontends.DetectAll();
                Log.Write($"detected {cfg.Frontends.Count} frontend(s)");
            }

            cfg.EnsureDefault();
            cfg.Save();
            return cfg;
        }

        /// <summary>Carry a stage-1 single-frontend config forward without losing it.</summary>
        private void Migrate()
        {
            if (string.IsNullOrWhiteSpace(FrontendPath)) return;
            if (!Frontends.Any(f => string.Equals(f.Path, FrontendPath, StringComparison.OrdinalIgnoreCase)))
            {
                var name = "";
                try { name = Path.GetFileNameWithoutExtension(FrontendPath); } catch { }
                Frontends.Insert(0, new FrontendEntry
                {
                    Name = string.IsNullOrWhiteSpace(name) ? "Frontend" : name,
                    Path = FrontendPath,
                    WatchProcess = WatchProcessName
                });
                if (string.IsNullOrWhiteSpace(DefaultFrontend)) DefaultFrontend = Frontends[0].Name;
                Log.Write("migrated the old single-frontend setting into the list");
            }
            FrontendPath = "";
            WatchProcessName = "";
        }

        public void EnsureDefault()
        {
            var enabled = EnabledFrontends();
            if (enabled.Count == 0) { DefaultFrontend = ""; return; }
            if (!enabled.Any(f => string.Equals(f.Name, DefaultFrontend, StringComparison.OrdinalIgnoreCase)))
                DefaultFrontend = enabled[0].Name;
        }

        public List<FrontendEntry> EnabledFrontends() =>
            Frontends.Where(f => f.Enabled && f.Exists()).ToList();

        public FrontendEntry Default()
        {
            var enabled = EnabledFrontends();
            if (enabled.Count == 0) return null;
            return enabled.FirstOrDefault(f =>
                       string.Equals(f.Name, DefaultFrontend, StringComparison.OrdinalIgnoreCase))
                   ?? enabled[0];
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opts));
            }
            catch (Exception ex) { Log.Write("config save failed: " + ex.Message); }
        }
    }
}
