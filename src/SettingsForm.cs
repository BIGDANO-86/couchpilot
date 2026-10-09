using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CouchPilot
{
    internal sealed class SettingsForm : Form
    {
        private readonly AppConfig _cfg;
        private readonly Engine _engine;
        private readonly Action<AppConfig> _onSaved;

        // frontend
        private TextBox _frontend;
        private TextBox _watchName;

        // behaviour
        private CheckBox _launchOnConnect, _sleepOnExit, _requireClean, _requireController;
        private CheckBox _startWithWindows, _showNotifications, _launchOnWake;
        private NumericUpDown _minRun, _sleepDelay, _poll, _cooldown;

        // webhooks
        private TextBox _hookLaunch, _hookExit;

        // status
        private Label _statusController, _statusFrontend, _statusWake, _statusSleepState;
        private System.Windows.Forms.Timer _statusTimer;

        public SettingsForm(AppConfig cfg, Engine engine, Action<AppConfig> onSaved)
        {
            _cfg = cfg;
            _engine = engine;
            _onSaved = onSaved;
            Build();
        }

        private void Build()
        {
            Text = "CouchPilot";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(640, 700);
            AutoScroll = true;

            var y = 14;

            // ---------------------------------------------------------- status
            Controls.Add(Theme.Head("Status", 18, y)); y += 26;
            var statusCard = Theme.Card(18, y, 604, 92);
            Controls.Add(statusCard);

            _statusController = Theme.Say("", 12, 10, 580);
            _statusFrontend   = Theme.Say("", 12, 30, 580);
            _statusWake       = Theme.Say("", 12, 50, 580);
            _statusSleepState = Theme.Say("", 12, 70, 580, dim: true);
            statusCard.Controls.AddRange(new Control[]
                { _statusController, _statusFrontend, _statusWake, _statusSleepState });
            y += 104;

            // -------------------------------------------------------- frontend
            Controls.Add(Theme.Head("Frontend", 18, y)); y += 26;
            Controls.Add(Theme.Say("The app to open when you switch a controller on.", 18, y, 600, dim: true));
            y += 20;

            _frontend = Theme.Input(18, y, 468, _cfg.FrontendPath);
            Controls.Add(_frontend);

            var browse = Theme.Btn("Browse", 492, y - 2, 60);
            browse.Click += (s, e) => Browse();
            Controls.Add(browse);

            var detect = Theme.Btn("Detect", 558, y - 2, 64);
            detect.Click += (s, e) =>
            {
                var found = AppConfig.DetectFrontend();
                if (string.IsNullOrEmpty(found))
                    MessageBox.Show(this, "No frontend found in the usual places. Use Browse.", "CouchPilot");
                else
                    _frontend.Text = found;
            };
            Controls.Add(detect);
            y += 30;

            Controls.Add(Theme.Say("Process to watch (leave blank unless the launcher differs)", 18, y, 420, dim: true));
            _watchName = Theme.Input(448, y - 3, 174, _cfg.WatchProcessName);
            Controls.Add(_watchName);
            y += 32;

            // ------------------------------------------------------- behaviour
            Controls.Add(Theme.Head("Behaviour", 18, y)); y += 26;

            _launchOnConnect = Theme.Check("Open the frontend when a controller is switched on", 20, y, _cfg.LaunchOnControllerConnect);
            Controls.Add(_launchOnConnect); y += 24;

            _sleepOnExit = Theme.Check("Sleep the PC when the frontend is closed", 20, y, _cfg.SleepOnFrontendExit);
            Controls.Add(_sleepOnExit); y += 24;

            _requireClean = Theme.Check("Only sleep after a clean exit, so a crash does not sleep the PC", 40, y, _cfg.RequireCleanExit);
            Controls.Add(_requireClean); y += 24;

            _requireController = Theme.Check("Only sleep if a controller is still connected", 40, y, _cfg.RequireControllerForSleep);
            Controls.Add(_requireController); y += 24;

            _launchOnWake = Theme.Check("Also open the frontend if the PC wakes with a controller already on", 20, y, _cfg.LaunchOnWakeWithController);
            Controls.Add(_launchOnWake); y += 24;

            _showNotifications = Theme.Check("Show a brief notification when something happens", 20, y, _cfg.ShowNotifications);
            Controls.Add(_showNotifications); y += 24;

            _startWithWindows = Theme.Check("Start CouchPilot with Windows", 20, y, _cfg.StartWithWindows);
            Controls.Add(_startWithWindows); y += 30;

            // ---------------------------------------------------------- timing
            Controls.Add(Theme.Head("Timing", 18, y)); y += 26;

            Controls.Add(Theme.Say("Ignore an exit sooner than", 20, y + 3, 190, dim: true));
            _minRun = Theme.Num(214, y, 0, 3600, _cfg.MinimumRunSeconds);
            Controls.Add(_minRun);
            Controls.Add(Theme.Say("seconds", 290, y + 3, 70, dim: true));

            Controls.Add(Theme.Say("Wait before sleeping", 374, y + 3, 130, dim: true));
            _sleepDelay = Theme.Num(506, y, 0, 120, _cfg.SleepDelaySeconds);
            Controls.Add(_sleepDelay);
            Controls.Add(Theme.Say("s", 582, y + 3, 30, dim: true));
            y += 30;

            Controls.Add(Theme.Say("Check for controllers every", 20, y + 3, 190, dim: true));
            _poll = Theme.Num(214, y, 1, 60, _cfg.ControllerPollSeconds);
            Controls.Add(_poll);
            Controls.Add(Theme.Say("seconds", 290, y + 3, 70, dim: true));

            Controls.Add(Theme.Say("Trigger cooldown", 374, y + 3, 130, dim: true));
            _cooldown = Theme.Num(506, y, 0, 600, _cfg.CooldownSeconds);
            Controls.Add(_cooldown);
            Controls.Add(Theme.Say("s", 582, y + 3, 30, dim: true));
            y += 36;

            // -------------------------------------------------------- webhooks
            Controls.Add(Theme.Head("Webhooks (optional)", 18, y)); y += 26;
            Controls.Add(Theme.Say("Leave blank unless you run Home Assistant or similar and want your TV handled too.",
                18, y, 604, dim: true));
            y += 20;

            Controls.Add(Theme.Say("On launch", 20, y + 3, 70, dim: true));
            _hookLaunch = Theme.Input(94, y, 428, _cfg.WebhookOnLaunch);
            Controls.Add(_hookLaunch);
            var testLaunch = Theme.Btn("Test", 528, y - 2, 94);
            testLaunch.Click += (s, e) => TestHook(_hookLaunch.Text);
            Controls.Add(testLaunch);
            y += 30;

            Controls.Add(Theme.Say("On exit", 20, y + 3, 70, dim: true));
            _hookExit = Theme.Input(94, y, 428, _cfg.WebhookOnExit);
            Controls.Add(_hookExit);
            var testExit = Theme.Btn("Test", 528, y - 2, 94);
            testExit.Click += (s, e) => TestHook(_hookExit.Text);
            Controls.Add(testExit);
            y += 38;

            // ------------------------------------------------------ wake setup
            Controls.Add(Theme.Head("Controller wake", 18, y)); y += 26;
            Controls.Add(Theme.Say("Waking the PC needs the controller receiver armed in Windows, not just supported by it.",
                18, y, 604, dim: true));
            y += 22;

            var armBtn = Theme.Btn("Set up controller wake", 20, y, 180);
            armBtn.Click += (s, e) => ArmWake();
            Controls.Add(armBtn);

            var wakeList = Theme.Btn("Show wake devices", 208, y, 150);
            wakeList.Click += (s, e) => ShowWakeDevices();
            Controls.Add(wakeList);
            y += 42;

            // ----------------------------------------------------------- tests
            Controls.Add(Theme.Head("Try it", 18, y)); y += 26;

            var tryLaunch = Theme.Btn("Open frontend now", 20, y, 150);
            tryLaunch.Click += (s, e) =>
            {
                Apply();
                _engine.TestLaunch();
            };
            Controls.Add(tryLaunch);

            var trySleep = Theme.Btn("Sleep now", 178, y, 110);
            trySleep.Click += (s, e) =>
            {
                if (MessageBox.Show(this, "Put this PC to sleep now?", "CouchPilot",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    _engine.TestSleep();
            };
            Controls.Add(trySleep);

            var openLog = Theme.Btn("Open log", 296, y, 110);
            openLog.Click += (s, e) => Reveal(Log.Path_);
            Controls.Add(openLog);
            y += 46;

            // --------------------------------------------------------- buttons
            var save = Theme.Btn("Save", 408, y, 100, primary: true);
            save.Click += (s, e) => { Apply(); Save(); Close(); };
            Controls.Add(save);

            var cancel = Theme.Btn("Close", 518, y, 104);
            cancel.Click += (s, e) => Close();
            Controls.Add(cancel);

            ClientSize = new Size(640, y + 46);

            _statusTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            _statusTimer.Tick += (s, e) => RefreshStatus();
            _statusTimer.Start();
            RefreshStatus();
            RefreshWakeLine();
        }

        private void RefreshStatus()
        {
            try
            {
                var pad = Native.AnyControllerConnected();
                _statusController.Text = pad ? "Controller: connected" : "Controller: none connected";
                _statusController.ForeColor = pad ? Theme.Good : Theme.Dim;

                var name = "";
                try { name = Path.GetFileNameWithoutExtension(_frontend.Text) ?? ""; } catch { }
                var running = !string.IsNullOrWhiteSpace(name) &&
                              Process.GetProcessesByName(_cfg.ResolveWatchName()).Length > 0;
                _statusFrontend.Text = running ? "Frontend: running" : "Frontend: not running";
                _statusFrontend.ForeColor = running ? Theme.Good : Theme.Dim;
            }
            catch { }
        }

        private void RefreshWakeLine()
        {
            try
            {
                var armed = WakeSetup.ControllersArmed();
                if (armed.Count > 0)
                {
                    _statusWake.Text = "Controller wake: armed (" + armed[0] + ")";
                    _statusWake.ForeColor = Theme.Good;
                }
                else
                {
                    var fixable = WakeSetup.ControllersNotArmed();
                    _statusWake.Text = fixable.Count > 0
                        ? "Controller wake: not armed, and it can be. Use Set up controller wake below."
                        : "Controller wake: no controller receiver reports wake support";
                    _statusWake.ForeColor = fixable.Count > 0 ? Theme.Warn : Theme.Dim;
                }

                _statusSleepState.Text = "Sleep: " + WakeSetup.SleepStateSummary();
            }
            catch (Exception ex)
            {
                _statusWake.Text = "Controller wake: could not be checked";
                _statusWake.ForeColor = Theme.Dim;
                Log.Write("wake check failed: " + ex.Message);
            }
        }

        private void Browse()
        {
            using var d = new OpenFileDialog
            {
                Title = "Pick your game frontend",
                Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*"
            };
            try
            {
                var dir = Path.GetDirectoryName(_frontend.Text);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) d.InitialDirectory = dir;
            }
            catch { }

            if (d.ShowDialog(this) == DialogResult.OK) _frontend.Text = d.FileName;
        }

        private void TestHook(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show(this, "Nothing to test, that field is empty.", "CouchPilot");
                return;
            }
            var ok = _engine.TestWebhook(url, Math.Max(1, _cfg.WebhookTimeoutSeconds));
            MessageBox.Show(this,
                ok ? "The webhook responded." : "No success response. Check the log for the exact error.",
                "CouchPilot");
        }

        private void ArmWake()
        {
            var fixable = WakeSetup.ControllersNotArmed();
            if (fixable.Count == 0)
            {
                var armed = WakeSetup.ControllersArmed();
                MessageBox.Show(this,
                    armed.Count > 0
                        ? "Controller wake is already armed:\n\n" + string.Join("\n", armed)
                        : "No controller receiver on this PC reports that it can wake the machine.\n\n" +
                          "Wired pads usually cannot. An Xbox Wireless Adapter or a Bluetooth\n" +
                          "receiver generally can, as long as it is not disabled in the BIOS.",
                    "CouchPilot");
                RefreshWakeLine();
                return;
            }

            var msg = "These devices can wake the PC but are not armed:\n\n" +
                      string.Join("\n", fixable) +
                      "\n\nArm them now? Windows will ask for administrator rights, because\n" +
                      "changing a device's wake setting requires it.";
            if (MessageBox.Show(this, msg, "CouchPilot", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes) return;

            foreach (var d in fixable) WakeSetup.Arm(d);
            System.Threading.Thread.Sleep(1200);
            RefreshWakeLine();

            var still = WakeSetup.ControllersNotArmed();
            MessageBox.Show(this,
                still.Count == 0
                    ? "Done. Your controller can now wake this PC."
                    : "Some devices are still not armed:\n\n" + string.Join("\n", still) +
                      "\n\nThey may also need enabling in the BIOS, often listed as\n" +
                      "USB wake or wake on USB.",
                "CouchPilot");
        }

        private void ShowWakeDevices()
        {
            var armed = WakeSetup.WakeArmed();
            var capable = WakeSetup.WakeCapable();
            var text = "ARMED - these can wake the PC right now:\n" +
                       (armed.Count > 0 ? string.Join("\n", armed.Select(a => "  " + a)) : "  (none)") +
                       "\n\nCAPABLE - these could be armed:\n" +
                       (capable.Count > 0 ? string.Join("\n", capable.Select(a => "  " + a)) : "  (none)");

            using var f = new Form
            {
                Text = "Wake devices",
                BackColor = Theme.Bg,
                ForeColor = Theme.Text,
                ClientSize = new Size(620, 440),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.SizableToolWindow
            };
            f.Controls.Add(new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.None,
                Font = Theme.Mono,
                Text = text
            });
            f.ShowDialog(this);
        }

        private static void Reveal(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                if (!File.Exists(path)) File.WriteAllText(path, "");
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch { }
        }

        /// <summary>Copy the controls back into the config object, without saving.</summary>
        private void Apply()
        {
            _cfg.FrontendPath = _frontend.Text.Trim();
            _cfg.WatchProcessName = _watchName.Text.Trim();
            _cfg.LaunchOnControllerConnect = _launchOnConnect.Checked;
            _cfg.SleepOnFrontendExit = _sleepOnExit.Checked;
            _cfg.RequireCleanExit = _requireClean.Checked;
            _cfg.RequireControllerForSleep = _requireController.Checked;
            _cfg.LaunchOnWakeWithController = _launchOnWake.Checked;
            _cfg.ShowNotifications = _showNotifications.Checked;
            _cfg.StartWithWindows = _startWithWindows.Checked;
            _cfg.MinimumRunSeconds = (int)_minRun.Value;
            _cfg.SleepDelaySeconds = (int)_sleepDelay.Value;
            _cfg.ControllerPollSeconds = (int)_poll.Value;
            _cfg.CooldownSeconds = (int)_cooldown.Value;
            _cfg.WebhookOnLaunch = _hookLaunch.Text.Trim();
            _cfg.WebhookOnExit = _hookExit.Text.Trim();
            _cfg.FirstRunDone = true;
        }

        private void Save()
        {
            _cfg.Save();
            Log.Write("settings saved from the settings window");
            _onSaved?.Invoke(_cfg);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { _statusTimer?.Stop(); _statusTimer?.Dispose(); } catch { }
            base.OnFormClosed(e);
        }
    }
}
