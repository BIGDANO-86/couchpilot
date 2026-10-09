using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace CouchPilot
{
    /// <summary>One launchable frontend.</summary>
    internal sealed class FrontendEntry
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Arguments { get; set; } = "";

        /// <summary>
        /// The long-lived process to watch, without .exe. Needed because some
        /// launchers are stubs that exit as soon as the real binary is up, and
        /// watching the stub looks like an instant quit.
        /// </summary>
        public string WatchProcess { get; set; } = "";

        public bool Enabled { get; set; } = true;

        public string ResolveWatch()
        {
            if (!string.IsNullOrWhiteSpace(WatchProcess))
                return WatchProcess.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
            try { return System.IO.Path.GetFileNameWithoutExtension(Path) ?? ""; }
            catch { return ""; }
        }

        public bool Exists()
        {
            try { return !string.IsNullOrWhiteSpace(Path) && File.Exists(Path); }
            catch { return false; }
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Finds the frontends people actually use. Each known one carries its own
    /// launch arguments and the process that genuinely stays alive, so the
    /// quit detection works without the user knowing any of this.
    /// </summary>
    internal static class Frontends
    {
        public static List<FrontendEntry> DetectAll()
        {
            var found = new List<FrontendEntry>();

            foreach (var root in LaunchBoxRoots())
            {
                Add(found, "Big Box", System.IO.Path.Combine(root, "BigBox.exe"), "", "BigBox");
                Add(found, "LaunchBox", System.IO.Path.Combine(root, "LaunchBox.exe"), "", "LaunchBox");
            }

            foreach (var root in PlayniteRoots())
            {
                Add(found, "Playnite (fullscreen)",
                    System.IO.Path.Combine(root, "Playnite.FullscreenApp.exe"), "", "Playnite.FullscreenApp");
                Add(found, "Playnite (desktop)",
                    System.IO.Path.Combine(root, "Playnite.DesktopApp.exe"), "", "Playnite.DesktopApp");
            }

            var steam = SteamExe();
            if (!string.IsNullOrEmpty(steam))
            {
                // Big Picture is the same executable with a switch, so both
                // entries share one watch process.
                Add(found, "Steam Big Picture", steam, "-bigpicture", "steam");
                Add(found, "Steam", steam, "", "steam");
            }

            return found;
        }

        private static void Add(List<FrontendEntry> list, string name, string path, string args, string watch)
        {
            try
            {
                if (!File.Exists(path)) return;
                if (list.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))) return;
                list.Add(new FrontendEntry { Name = name, Path = path, Arguments = args, WatchProcess = watch });
            }
            catch { }
        }

        private static IEnumerable<string> LaunchBoxRoots()
        {
            var seen = new List<string>();

            // The uninstall entry is the reliable source; the fixed paths are a
            // fallback for portable installs.
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                foreach (var sub in new[]
                {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                })
                {
                    try
                    {
                        using var k = hive.OpenSubKey(sub);
                        if (k == null) continue;
                        foreach (var name in k.GetSubKeyNames())
                        {
                            using var app = k.OpenSubKey(name);
                            var disp = app?.GetValue("DisplayName") as string;
                            if (disp == null || disp.IndexOf("LaunchBox", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            var loc = app.GetValue("InstallLocation") as string;
                            if (!string.IsNullOrWhiteSpace(loc) && Directory.Exists(loc)) seen.Add(loc);
                        }
                    }
                    catch { }
                }
            }

            foreach (var p in new[]
            {
                @"C:\Launchbox", @"C:\LaunchBox", @"D:\Launchbox", @"D:\LaunchBox",
                @"C:\Program Files\LaunchBox", @"C:\Program Files (x86)\LaunchBox"
            })
            {
                try { if (Directory.Exists(p)) seen.Add(p); } catch { }
            }

            return seen.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> PlayniteRoots()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var seen = new List<string>
            {
                System.IO.Path.Combine(local, "Playnite"),
                @"C:\Program Files\Playnite",
                @"C:\Program Files (x86)\Playnite"
            };

            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Playnite");
                var loc = k?.GetValue("InstallLocation") as string;
                if (!string.IsNullOrWhiteSpace(loc)) seen.Insert(0, loc);
            }
            catch { }

            return seen.Where(p => { try { return Directory.Exists(p); } catch { return false; } })
                       .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static string SteamExe()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
                var path = k?.GetValue("SteamExe") as string;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;

                var dir = k?.GetValue("SteamPath") as string;
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    var exe = System.IO.Path.Combine(dir.Replace('/', '\\'), "steam.exe");
                    if (File.Exists(exe)) return exe;
                }
            }
            catch { }

            foreach (var p in new[]
            {
                @"C:\Program Files (x86)\Steam\steam.exe",
                @"C:\Program Files\Steam\steam.exe"
            })
            {
                try { if (File.Exists(p)) return p; } catch { }
            }
            return "";
        }
    }
}
