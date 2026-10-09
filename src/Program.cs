using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

        public static Icon AppIcon { get; private set; }

        public static string VersionString
        {
            get
            {
                try
                {
                    var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    return v == null ? "0.1.0" : $"{v.Major}.{v.Minor}.{v.Build}";
                }
                catch { return "0.1.0"; }
            }
        }

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
            _engine.Notify += m => Notify(m);
            _engine.AskWhich = AskWhichFrontend;
            _engine.Start();

            _hidden = new Form
            {
                ShowInTaskbar = false,
                WindowState = FormWindowState.Minimized,
                FormBorderStyle = FormBorderStyle.None,
                Size = new Size(1, 1),
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000)
            };
            _hidden.Load += (s, e) => _hidden.Hide();
            _hidden.Show();

            BuildTray();

            // First run, or nothing usable detected, is the one case where
            // sitting silently in the tray would look broken.
            if (!_cfg.FirstRunDone || _cfg.EnabledFrontends().Count == 0)
                OpenSettings();

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

            var settings = new ToolStripMenuItem("Settings...");
            settings.Font = new Font(settings.Font, FontStyle.Bold);
            settings.Click += (s, e) => OpenSettings();
            menu.Items.Add(settings);

            var editRaw = new ToolStripMenuItem("Edit config file");
            editRaw.Click += (s, e) => OpenInEditor(AppConfig.FilePath);
            menu.Items.Add(editRaw);

            var reload = new ToolStripMenuItem("Reload config file");
            reload.Click += (s, e) =>
            {
                _cfg = AppConfig.Load();
                _engine.UpdateConfig(_cfg);
                ApplyStartWithWindows(_cfg.StartWithWindows);
                Log.Write("config reloaded from disk");
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
            _tray.DoubleClick += (s, e) => OpenSettings();
            UpdateTip();
        }

        /// <summary>
        /// Shows the pad-navigable picker. The engine calls this from a worker
        /// thread, so it is marshalled onto the UI thread and waited on, because
        /// the answer decides what gets launched.
        /// </summary>
        private static FrontendEntry AskWhichFrontend(List<FrontendEntry> choices, string defaultName, int timeout)
        {
            try
            {
                FrontendEntry result = null;

                var show = new Action(() =>
                {
                    try
                    {
                        using var chooser = new ChooserForm(choices, defaultName, timeout);
                        chooser.ShowDialog();
                        result = chooser.Chosen;
                    }
                    catch (Exception ex)
                    {
                        Log.Write("chooser failed, falling back to the default: " + ex.Message);
                        result = choices.FirstOrDefault(f =>
                            string.Equals(f.Name, defaultName, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
                    }
                });

                if (_tray != null && _hidden != null && _hidden.InvokeRequired)
                    _hidden.Invoke(show);
                else
                    show();

                return result;
            }
            catch (Exception ex)
            {
                Log.Write("could not ask which frontend: " + ex.Message);
                return choices.Count > 0 ? choices[0] : null;
            }
        }

        /// <summary>
        /// An invisible window purely to own the UI thread, so worker threads
        /// have something to Invoke onto even with no settings window open.
        /// </summary>
        private static Form _hidden;

        private static SettingsForm _settingsWindow;

        private static void OpenSettings()
        {
            try
            {
                if (_settingsWindow != null && !_settingsWindow.IsDisposed)
                {
                    _settingsWindow.Activate();
                    return;
                }

                _settingsWindow = new SettingsForm(_cfg, _engine, saved =>
                {
                    _cfg = saved;
                    _engine.UpdateConfig(_cfg);
                    ApplyStartWithWindows(_cfg.StartWithWindows);
                    UpdateTip();
                });
                _settingsWindow.FormClosed += (s, e) => _settingsWindow = null;
                _settingsWindow.Show();
                _settingsWindow.Activate();
            }
            catch (Exception ex)
            {
                Log.Write("could not open settings: " + ex.Message);
                MessageBox.Show("Could not open the settings window.\n\n" + ex.Message, "CouchPilot");
            }
        }

        private static void UpdateTip()
        {
            if (_tray == null) return;
            var enabled = _cfg.EnabledFrontends();
            var what = enabled.Count == 0 ? "no frontend set up"
                     : enabled.Count == 1 ? enabled[0].Name
                     : enabled.Count + " frontends";
            // Tooltips are capped at 63 characters by the shell.
            var tip = "CouchPilot" + (_engine.Paused ? " (paused)" : "") + " - " + what;
            _tray.Text = tip.Length > 62 ? tip.Substring(0, 62) : tip;
        }

        private static void Notify(string message)
        {
            // The engine raises these from a worker thread; NotifyIcon must be
            // touched on the UI thread.
            try
            {
                if (_tray == null) return;
                var show = new Action(() =>
                {
                    try
                    {
                        _tray.BalloonTipTitle = "CouchPilot";
                        _tray.BalloonTipText = message;
                        _tray.ShowBalloonTip(3000);
                    }
                    catch { }
                });

                if (_hidden != null && !_hidden.IsDisposed && _hidden.InvokeRequired)
                    _hidden.BeginInvoke(show);
                else
                    show();
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
                    if (ico != null) { AppIcon = ico; return ico; }
                }
            }
            catch { }
            AppIcon = SystemIcons.Application;
            return AppIcon;
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
