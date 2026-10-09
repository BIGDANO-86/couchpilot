using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CouchPilot
{
    internal static class Program
    {
        private static Mutex _single;
        private static NotifyIcon _tray;
        private static Engine _engine;
        private static AppConfig _cfg;

        [STAThread]
        private static void Main()
        {
            // One instance only. Two engines would both try to launch and sleep.
            _single = new Mutex(true, "CouchPilot.SingleInstance", out var isNew);
            if (!isNew)
            {
                MessageBox.Show("CouchPilot is already running. Look for it in the notification area.",
                    "CouchPilot", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _cfg = AppConfig.Load();
            ApplyStartWithWindows(_cfg.StartWithWindows);

            Log.Write("---- CouchPilot starting ----");
            Log.Write("frontend: " + (string.IsNullOrWhiteSpace(_cfg.FrontendPath) ? "(none detected)" : _cfg.FrontendPath));

            _engine = new Engine(_cfg);
            _engine.Start();

            BuildTray();
            Application.Run();
        }

        private static void BuildTray()
        {
            var menu = new ContextMenuStrip();

            var pause = new ToolStripMenuItem("Pause") { CheckOnClick = true };
            pause.CheckedChanged += (s, e) =>
            {
                _engine.Paused = pause.Checked;
                pause.Text = pause.Checked ? "Paused" : "Pause";
                Log.Write(pause.Checked ? "paused by user" : "resumed by user");
                UpdateTip();
            };
            menu.Items.Add(pause);

            menu.Items.Add(new ToolStripSeparator());

            var open = new ToolStripMenuItem("Edit settings");
            open.Click += (s, e) => OpenInEditor(AppConfig.FilePath);
            menu.Items.Add(open);

            var reload = new ToolStripMenuItem("Reload settings");
            reload.Click += (s, e) =>
            {
                _cfg = AppConfig.Load();
                _engine.UpdateConfig(_cfg);
                ApplyStartWithWindows(_cfg.StartWithWindows);
                Log.Write("settings reloaded");
                Notify("Settings reloaded");
                UpdateTip();
            };
            menu.Items.Add(reload);

            var log = new ToolStripMenuItem("Open log");
            log.Click += (s, e) => OpenInEditor(Log.Path_);
            menu.Items.Add(log);

            menu.Items.Add(new ToolStripSeparator());

            var quit = new ToolStripMenuItem("Quit CouchPilot");
            quit.Click += (s, e) =>
            {
                Log.Write("---- quit by user ----");
                _tray.Visible = false;
                _engine.Dispose();
                Application.Exit();
            };
            menu.Items.Add(quit);

            _tray = new NotifyIcon
            {
                Icon = LoadIcon(),
                Visible = true,
                ContextMenuStrip = menu
            };
            _tray.DoubleClick += (s, e) => OpenInEditor(AppConfig.FilePath);
            UpdateTip();
        }

        private static void UpdateTip()
        {
            if (_tray == null) return;
            var what = string.IsNullOrWhiteSpace(_cfg.FrontendPath)
                ? "no frontend configured"
                : Path.GetFileNameWithoutExtension(_cfg.FrontendPath);
            // Tooltips are capped at 63 characters by the shell.
            var tip = "CouchPilot" + (_engine.Paused ? " (paused)" : "") + " - " + what;
            _tray.Text = tip.Length > 62 ? tip.Substring(0, 62) : tip;
        }

        private static void Notify(string message)
        {
            try
            {
                _tray.BalloonTipTitle = "CouchPilot";
                _tray.BalloonTipText = message;
                _tray.ShowBalloonTip(3000);
            }
            catch { }
        }

        private static Icon LoadIcon()
        {
            try
            {
                var exe = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exe))
                {
                    var ico = Icon.ExtractAssociatedIcon(exe);
                    if (ico != null) return ico;
                }
            }
            catch { }
            return SystemIcons.Application;
        }

        private static void OpenInEditor(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                if (!File.Exists(path)) File.WriteAllText(path, "");
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open " + path + "\n\n" + ex.Message, "CouchPilot");
            }
        }

        /// <summary>
        /// Run key rather than a scheduled task, on purpose. A task needs either
        /// elevation to create or a fragile XML schema dance, and it buys nothing
        /// here because the app has to run in the interactive session anyway.
        /// </summary>
        private static void ApplyStartWithWindows(bool enable)
        {
            const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
            try
            {
                var exe = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exe)) return;

                using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
                if (key == null) return;

                if (enable) key.SetValue("CouchPilot", "\"" + exe + "\"");
                else if (key.GetValue("CouchPilot") != null) key.DeleteValue("CouchPilot", false);
            }
            catch (Exception ex)
            {
                Log.Write("start-with-windows change failed: " + ex.Message);
            }
        }
    }
}
