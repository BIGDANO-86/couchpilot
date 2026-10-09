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

        private readonly Dictionary<string, Panel> _pages = new Dictionary<string, Panel>();
        private readonly List<Label> _navItems = new List<Label>();
        private string _current = "";

        // frontends page
        private CheckedListBox _list;
        private TextBox _fName, _fPath, _fArgs, _fWatch;
        private Label _defaultLabel;
        private CheckBox _showChooser;
        private NumericUpDown _chooserTimeout;
        private bool _loadingEntry;

        // behaviour page
        private CheckBox _launchOnConnect, _sleepOnExit, _requireClean, _requireController;
        private CheckBox _startWithWindows, _showNotifications, _launchOnWake, _disconnectOnlyClosed;
        private NumericUpDown _minRun, _sleepDelay, _poll, _cooldown, _disconnectDelay;
        private ComboBox _disconnectAction;
        private TextBox _disconnectPath, _disconnectArgs;

        // integrations page
        private TextBox _hookLaunch, _hookExit;
        private CheckBox _mqttEnabled, _mqttRetain;
        private TextBox _mqttHost, _mqttUser, _mqttPass, _mqttClientId,
                        _mqttTopicLaunch, _mqttTopicExit, _mqttPayloadLaunch, _mqttPayloadExit;
        private NumericUpDown _mqttPort;

        // status
        private Label _sController, _sFrontend, _sWake, _sSleepState;
        private System.Windows.Forms.Timer _statusTimer;

        public SettingsForm(AppConfig cfg, Engine engine, Action<AppConfig> onSaved)
        {
            _cfg = cfg; _engine = engine; _onSaved = onSaved;
            Build();
        }

        // ============================================================== layout
        private void Build()
        {
            Text = "CouchPilot";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(880, 640);
            try { Icon = Program.AppIcon; } catch { }

            var nav = new Panel
            {
                BackColor = Color.FromArgb(18, 20, 23),
                Location = new Point(0, 0),
                Size = new Size(196, 640)
            };
            Controls.Add(nav);

            var brand = new Label
            {
                Text = "CouchPilot",
                Font = new Font("Segoe UI Semibold", 14f),
                ForeColor = Theme.Accent,
                AutoSize = true,
                Location = new Point(20, 22)
            };
            nav.Controls.Add(brand);

            var version = new Label
            {
                Text = "v" + Program.VersionString,
                Font = new Font("Segoe UI", 8f),
                ForeColor = Theme.Dim,
                AutoSize = true,
                Location = new Point(22, 48)
            };
            nav.Controls.Add(version);

            var names = new[] { "Frontends", "Behaviour", "Integrations", "Controller wake", "Status" };
            var ny = 86;
            foreach (var n in names)
            {
                var item = new Label
                {
                    Text = "   " + n,
                    Font = Theme.Body,
                    ForeColor = Theme.Dim,
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Size = new Size(196, 36),
                    Location = new Point(0, ny),
                    Cursor = Cursors.Hand,
                    Tag = n
                };
                item.Click += (s, e) => ShowPage((string)((Label)s).Tag);
                nav.Controls.Add(item);
                _navItems.Add(item);
                ny += 38;
            }

            // bottom bar
            var save = Theme.Btn("Save", 650, 596, 104, primary: true);
            save.Click += (s, e) => { Apply(); _cfg.Save(); _onSaved?.Invoke(_cfg); Close(); };
            Controls.Add(save);

            var close = Theme.Btn("Close", 762, 596, 100);
            close.Click += (s, e) => Close();
            Controls.Add(close);

            BuildFrontendsPage();
            BuildBehaviourPage();
            BuildIntegrationsPage();
            BuildWakePage();
            BuildStatusPage();

            ShowPage("Frontends");

            _statusTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            _statusTimer.Tick += (s, e) => RefreshStatus();
            _statusTimer.Start();
        }

        private Panel NewPage(string name)
        {
            var p = new Panel
            {
                BackColor = Theme.Bg,
                Location = new Point(196, 0),
                Size = new Size(684, 586),
                AutoScroll = true,
                Visible = false
            };
            Controls.Add(p);
            _pages[name] = p;
            return p;
        }

        private void ShowPage(string name)
        {
            if (!_pages.ContainsKey(name)) return;
            foreach (var kv in _pages) kv.Value.Visible = kv.Key == name;
            foreach (var n in _navItems)
            {
                var on = (string)n.Tag == name;
                n.ForeColor = on ? Theme.Text : Theme.Dim;
                n.BackColor = on ? Color.FromArgb(28, 31, 36) : Color.Transparent;
                n.Font = on ? Theme.BodyBold : Theme.Body;
            }
            _current = name;
            if (name == "Status" || name == "Controller wake") { RefreshStatus(); RefreshWake(); }
        }

        // ========================================================== frontends
        private void BuildFrontendsPage()
        {
            var p = NewPage("Frontends");
            var y = 24;

            p.Controls.Add(Theme.Head("Frontends", 24, y)); y += 28;
            p.Controls.Add(Theme.Say("Tick the ones to offer. If more than one is ticked you get a picker, " +
                                     "driveable from the pad.", 24, y, 620, dim: true));
            y += 24;

            _list = new CheckedListBox
            {
                Location = new Point(24, y),
                Size = new Size(380, 150),
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                CheckOnClick = true,
                IntegralHeight = false,
                Font = Theme.Body
            };
            _list.SelectedIndexChanged += (s, e) => LoadEntry();
            _list.ItemCheck += (s, e) =>
            {
                BeginInvoke(new Action(() =>
                {
                    if (e.Index >= 0 && e.Index < _cfg.Frontends.Count)
                        _cfg.Frontends[e.Index].Enabled = e.NewValue == CheckState.Checked;
                    _cfg.EnsureDefault();
                    RefreshList(_list.SelectedIndex);
                }));
            };
            p.Controls.Add(_list);

            var bx = 414;
            var add = Theme.Btn("Add", bx, y, 110);
            add.Click += (s, e) => AddFrontend();
            p.Controls.Add(add);

            var remove = Theme.Btn("Remove", bx, y + 34, 110);
            remove.Click += (s, e) => RemoveFrontend();
            p.Controls.Add(remove);

            var mkDefault = Theme.Btn("Make default", bx, y + 68, 110);
            mkDefault.Click += (s, e) => MakeDefault();
            p.Controls.Add(mkDefault);

            var redetect = Theme.Btn("Detect again", bx, y + 102, 110);
            redetect.Click += (s, e) => Redetect();
            p.Controls.Add(redetect);

            var up = Theme.Btn("Up", bx + 118, y, 52);
            up.Click += (s, e) => MoveEntry(-1);
            p.Controls.Add(up);

            var down = Theme.Btn("Down", bx + 118, y + 34, 52);
            down.Click += (s, e) => MoveEntry(1);
            p.Controls.Add(down);

            var test = Theme.Btn("Open it", bx + 118, y + 68, 52);
            test.Click += (s, e) =>
            {
                Apply();
                var sel = Selected();
                if (sel != null) _engine.TestLaunch(sel);
            };
            p.Controls.Add(test);

            y += 162;

            _defaultLabel = Theme.Say("", 24, y, 620);
            _defaultLabel.ForeColor = Theme.Accent;
            p.Controls.Add(_defaultLabel);
            y += 26;

            p.Controls.Add(Theme.Say("Name", 24, y + 3, 70, dim: true));
            _fName = Theme.Input(96, y, 230, "");
            _fName.TextChanged += (s, e) => EditSelected(f => f.Name = _fName.Text);
            p.Controls.Add(_fName);
            y += 30;

            p.Controls.Add(Theme.Say("Program", 24, y + 3, 70, dim: true));
            _fPath = Theme.Input(96, y, 450, "");
            _fPath.TextChanged += (s, e) => EditSelected(f => f.Path = _fPath.Text);
            p.Controls.Add(_fPath);
            var browse = Theme.Btn("Browse", 552, y - 2, 80);
            browse.Click += (s, e) => BrowseFor();
            p.Controls.Add(browse);
            y += 30;

            p.Controls.Add(Theme.Say("Arguments", 24, y + 3, 70, dim: true));
            _fArgs = Theme.Input(96, y, 230, "");
            _fArgs.TextChanged += (s, e) => EditSelected(f => f.Arguments = _fArgs.Text);
            p.Controls.Add(_fArgs);

            p.Controls.Add(Theme.Say("Watch process", 340, y + 3, 96, dim: true));
            _fWatch = Theme.Input(442, y, 190, "");
            _fWatch.TextChanged += (s, e) => EditSelected(f => f.WatchProcess = _fWatch.Text);
            p.Controls.Add(_fWatch);
            y += 28;

            p.Controls.Add(Theme.Say("Watch process is the one that stays alive. Some launchers are stubs " +
                                     "that exit immediately, which would look like quitting.",
                                     96, y, 540, dim: true));
            y += 34;

            _showChooser = Theme.Check("Ask which frontend when more than one is ticked", 26, y, _cfg.ShowChooser, 420);
            p.Controls.Add(_showChooser);
            y += 26;

            p.Controls.Add(Theme.Say("Pick the default by itself after", 44, y + 3, 190, dim: true));
            _chooserTimeout = Theme.Num(238, y, 0, 120, _cfg.ChooserTimeoutSeconds);
            p.Controls.Add(_chooserTimeout);
            p.Controls.Add(Theme.Say("seconds, 0 waits forever", 316, y + 3, 200, dim: true));

            RefreshList(0);
        }

        private FrontendEntry Selected()
        {
            var i = _list.SelectedIndex;
            return (i >= 0 && i < _cfg.Frontends.Count) ? _cfg.Frontends[i] : null;
        }

        private void EditSelected(Action<FrontendEntry> change)
        {
            if (_loadingEntry) return;
            var f = Selected();
            if (f == null) return;
            change(f);
            var keep = _list.SelectedIndex;
            RefreshList(keep, keepEditing: true);
        }

        private void RefreshList(int select, bool keepEditing = false)
        {
            _loadingEntry = true;
            try
            {
                _list.Items.Clear();
                foreach (var f in _cfg.Frontends)
                {
                    var label = f.Name;
                    if (string.Equals(f.Name, _cfg.DefaultFrontend, StringComparison.OrdinalIgnoreCase))
                        label += "   (default)";
                    if (!f.Exists()) label += "   [missing]";
                    _list.Items.Add(label, f.Enabled);
                }
                if (_cfg.Frontends.Count > 0)
                    _list.SelectedIndex = Math.Max(0, Math.Min(select, _cfg.Frontends.Count - 1));

                _defaultLabel.Text = string.IsNullOrWhiteSpace(_cfg.DefaultFrontend)
                    ? "No default set"
                    : "Default: " + _cfg.DefaultFrontend;
            }
            finally { _loadingEntry = false; }

            if (!keepEditing) LoadEntry();
        }

        private void LoadEntry()
        {
            var f = Selected();
            _loadingEntry = true;
            try
            {
                _fName.Text = f?.Name ?? "";
                _fPath.Text = f?.Path ?? "";
                _fArgs.Text = f?.Arguments ?? "";
                _fWatch.Text = f?.WatchProcess ?? "";
            }
            finally { _loadingEntry = false; }
        }

        private void AddFrontend()
        {
            using var d = new OpenFileDialog { Title = "Pick a program", Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*" };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            var name = Path.GetFileNameWithoutExtension(d.FileName);
            _cfg.Frontends.Add(new FrontendEntry { Name = name, Path = d.FileName, Enabled = true });
            _cfg.EnsureDefault();
            RefreshList(_cfg.Frontends.Count - 1);
        }

        private void RemoveFrontend()
        {
            var i = _list.SelectedIndex;
            if (i < 0 || i >= _cfg.Frontends.Count) return;
            _cfg.Frontends.RemoveAt(i);
            _cfg.EnsureDefault();
            RefreshList(Math.Max(0, i - 1));
        }

        private void MakeDefault()
        {
            var f = Selected();
            if (f == null) return;
            _cfg.DefaultFrontend = f.Name;
            if (!f.Enabled) f.Enabled = true;
            RefreshList(_list.SelectedIndex);
        }

        private void MoveEntry(int delta)
        {
            var i = _list.SelectedIndex;
            var j = i + delta;
            if (i < 0 || j < 0 || j >= _cfg.Frontends.Count) return;
            var tmp = _cfg.Frontends[i];
            _cfg.Frontends[i] = _cfg.Frontends[j];
            _cfg.Frontends[j] = tmp;
            RefreshList(j);
        }

        private void Redetect()
        {
            var found = FrontendCatalog.DetectAll();
            var added = 0;
            foreach (var f in found)
            {
                if (_cfg.Frontends.Any(e => string.Equals(e.Path, f.Path, StringComparison.OrdinalIgnoreCase)
                                            && string.Equals(e.Arguments ?? "", f.Arguments ?? "",
                                                             StringComparison.OrdinalIgnoreCase)))
                    continue;
                _cfg.Frontends.Add(f);
                added++;
            }
            _cfg.EnsureDefault();
            RefreshList(_list.SelectedIndex < 0 ? 0 : _list.SelectedIndex);
            MessageBox.Show(this,
                added == 0 ? "Nothing new found." : $"Added {added}.",
                "CouchPilot");
        }

        private void BrowseFor()
        {
            var f = Selected();
            if (f == null) return;
            using var d = new OpenFileDialog { Title = "Pick a program", Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*" };
            try
            {
                var dir = Path.GetDirectoryName(f.Path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) d.InitialDirectory = dir;
            }
            catch { }
            if (d.ShowDialog(this) == DialogResult.OK) _fPath.Text = d.FileName;
        }

        // ========================================================== behaviour
        private void BuildBehaviourPage()
        {
            var p = NewPage("Behaviour");
            var y = 24;

            p.Controls.Add(Theme.Head("When a controller comes on", 24, y)); y += 30;
            _launchOnConnect = Theme.Check("Open a frontend", 26, y, _cfg.LaunchOnControllerConnect); p.Controls.Add(_launchOnConnect); y += 24;
            _launchOnWake = Theme.Check("Also open if the PC wakes with a pad already on (off by default, " +
                                        "so a keyboard wake does not hijack the TV)", 26, y, _cfg.LaunchOnWakeWithController, 600);
            p.Controls.Add(_launchOnWake); y += 32;

            p.Controls.Add(Theme.Head("When the frontend closes", 24, y)); y += 30;
            _sleepOnExit = Theme.Check("Sleep the PC", 26, y, _cfg.SleepOnFrontendExit); p.Controls.Add(_sleepOnExit); y += 24;
            _requireClean = Theme.Check("Only after a clean exit, so a crash does not sleep the PC", 46, y, _cfg.RequireCleanExit, 560);
            p.Controls.Add(_requireClean); y += 24;
            _requireController = Theme.Check("Only if a controller is still connected", 46, y, _cfg.RequireControllerForSleep, 560);
            p.Controls.Add(_requireController); y += 32;

            p.Controls.Add(Theme.Head("When the controller goes off", 24, y)); y += 30;
            p.Controls.Add(Theme.Say("Pads idle off on their own, so give this a generous wait.", 26, y, 580, dim: true));
            y += 22;

            p.Controls.Add(Theme.Say("Do", 26, y + 3, 30, dim: true));
            _disconnectAction = new ComboBox
            {
                Location = new Point(60, y),
                Size = new Size(170, 23),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                FlatStyle = FlatStyle.Flat,
                Font = Theme.Body
            };
            _disconnectAction.Items.AddRange(new object[] { "nothing", "run a program", "sleep the PC" });
            _disconnectAction.SelectedIndex = (int)_cfg.OnDisconnect;
            _disconnectAction.SelectedIndexChanged += (s, e) => SyncDisconnectEnabled();
            p.Controls.Add(_disconnectAction);

            p.Controls.Add(Theme.Say("after", 244, y + 3, 40, dim: true));
            _disconnectDelay = Theme.Num(286, y, 0, 3600, _cfg.DisconnectDelaySeconds);
            p.Controls.Add(_disconnectDelay);
            p.Controls.Add(Theme.Say("seconds", 362, y + 3, 70, dim: true));
            y += 30;

            p.Controls.Add(Theme.Say("Program", 26, y + 3, 60, dim: true));
            _disconnectPath = Theme.Input(90, y, 410, _cfg.DisconnectPath);
            p.Controls.Add(_disconnectPath);
            var dBrowse = Theme.Btn("Browse", 506, y - 2, 80);
            dBrowse.Click += (s, e) =>
            {
                using var d = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*" };
                if (d.ShowDialog(this) == DialogResult.OK) _disconnectPath.Text = d.FileName;
            };
            p.Controls.Add(dBrowse);
            y += 30;

            p.Controls.Add(Theme.Say("Arguments", 26, y + 3, 60, dim: true));
            _disconnectArgs = Theme.Input(90, y, 410, _cfg.DisconnectArguments);
            p.Controls.Add(_disconnectArgs);
            y += 28;

            _disconnectOnlyClosed = Theme.Check("Skip it while a frontend is still running", 26, y,
                _cfg.DisconnectOnlyWhenFrontendClosed, 560);
            p.Controls.Add(_disconnectOnlyClosed);
            y += 34;

            p.Controls.Add(Theme.Head("Timing", 24, y)); y += 30;

            p.Controls.Add(Theme.Say("Ignore an exit sooner than", 26, y + 3, 170, dim: true));
            _minRun = Theme.Num(200, y, 0, 3600, _cfg.MinimumRunSeconds); p.Controls.Add(_minRun);
            p.Controls.Add(Theme.Say("s", 276, y + 3, 20, dim: true));
            p.Controls.Add(Theme.Say("Wait before sleeping", 320, y + 3, 130, dim: true));
            _sleepDelay = Theme.Num(452, y, 0, 180, _cfg.SleepDelaySeconds); p.Controls.Add(_sleepDelay);
            p.Controls.Add(Theme.Say("s", 528, y + 3, 20, dim: true));
            y += 30;

            p.Controls.Add(Theme.Say("Check for controllers every", 26, y + 3, 170, dim: true));
            _poll = Theme.Num(200, y, 1, 60, _cfg.ControllerPollSeconds); p.Controls.Add(_poll);
            p.Controls.Add(Theme.Say("s", 276, y + 3, 20, dim: true));
            p.Controls.Add(Theme.Say("Trigger cooldown", 320, y + 3, 130, dim: true));
            _cooldown = Theme.Num(452, y, 0, 600, _cfg.CooldownSeconds); p.Controls.Add(_cooldown);
            p.Controls.Add(Theme.Say("s", 528, y + 3, 20, dim: true));
            y += 36;

            p.Controls.Add(Theme.Head("App", 24, y)); y += 30;
            _showNotifications = Theme.Check("Show a brief notification when something happens", 26, y, _cfg.ShowNotifications, 560);
            p.Controls.Add(_showNotifications); y += 24;
            _startWithWindows = Theme.Check("Start CouchPilot with Windows", 26, y, _cfg.StartWithWindows, 560);
            p.Controls.Add(_startWithWindows); y += 30;

            var sleepNow = Theme.Btn("Sleep now", 26, y, 110);
            sleepNow.Click += (s, e) =>
            {
                if (MessageBox.Show(this, "Put this PC to sleep now?", "CouchPilot",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    _engine.TestSleep();
            };
            p.Controls.Add(sleepNow);

            var openLog = Theme.Btn("Open log", 144, y, 110);
            openLog.Click += (s, e) => Reveal(Log.Path_);
            p.Controls.Add(openLog);

            SyncDisconnectEnabled();
        }

        private void SyncDisconnectEnabled()
        {
            var runMode = _disconnectAction.SelectedIndex == 1;
            _disconnectPath.Enabled = runMode;
            _disconnectArgs.Enabled = runMode;
        }

        // ======================================================= integrations
        private void BuildIntegrationsPage()
        {
            var p = NewPage("Integrations");
            var y = 24;

            p.Controls.Add(Theme.Head("Webhooks", 24, y)); y += 28;
            p.Controls.Add(Theme.Say("Optional. Leave blank unless you run Home Assistant or similar and want " +
                                     "your TV handled too.", 24, y, 620, dim: true));
            y += 24;

            p.Controls.Add(Theme.Say("On launch", 26, y + 3, 70, dim: true));
            _hookLaunch = Theme.Input(100, y, 430, _cfg.WebhookOnLaunch); p.Controls.Add(_hookLaunch);
            var t1 = Theme.Btn("Test", 538, y - 2, 94);
            t1.Click += (s, e) => TestHook(_hookLaunch.Text);
            p.Controls.Add(t1); y += 30;

            p.Controls.Add(Theme.Say("On exit", 26, y + 3, 70, dim: true));
            _hookExit = Theme.Input(100, y, 430, _cfg.WebhookOnExit); p.Controls.Add(_hookExit);
            var t2 = Theme.Btn("Test", 538, y - 2, 94);
            t2.Click += (s, e) => TestHook(_hookExit.Text);
            p.Controls.Add(t2); y += 38;

            var m = _cfg.Mqtt ?? new MqttSettings();
            p.Controls.Add(Theme.Head("MQTT", 24, y)); y += 28;
            p.Controls.Add(Theme.Say("An alternative to webhooks. Connects only to publish, so nothing is held " +
                                     "open across sleep.", 24, y, 620, dim: true));
            y += 24;

            _mqttEnabled = Theme.Check("Publish over MQTT", 26, y, m.Enabled, 300);
            p.Controls.Add(_mqttEnabled); y += 28;

            p.Controls.Add(Theme.Say("Broker", 26, y + 3, 60, dim: true));
            _mqttHost = Theme.Input(90, y, 240, m.Host); p.Controls.Add(_mqttHost);
            p.Controls.Add(Theme.Say("Port", 344, y + 3, 32, dim: true));
            _mqttPort = Theme.Num(380, y, 1, 65535, m.Port <= 0 ? 1883 : m.Port, 76); p.Controls.Add(_mqttPort);
            y += 30;

            p.Controls.Add(Theme.Say("Username", 26, y + 3, 60, dim: true));
            _mqttUser = Theme.Input(90, y, 180, m.Username); p.Controls.Add(_mqttUser);
            p.Controls.Add(Theme.Say("Password", 284, y + 3, 60, dim: true));
            _mqttPass = Theme.Input(350, y, 180, m.Password);
            _mqttPass.UseSystemPasswordChar = true;
            p.Controls.Add(_mqttPass);
            y += 30;

            p.Controls.Add(Theme.Say("Client id", 26, y + 3, 60, dim: true));
            _mqttClientId = Theme.Input(90, y, 180, m.ClientId); p.Controls.Add(_mqttClientId);
            _mqttRetain = Theme.Check("Retain messages", 284, y, m.Retain, 200);
            p.Controls.Add(_mqttRetain);
            y += 32;

            p.Controls.Add(Theme.Say("Launch topic", 26, y + 3, 80, dim: true));
            _mqttTopicLaunch = Theme.Input(110, y, 260, m.TopicLaunch); p.Controls.Add(_mqttTopicLaunch);
            p.Controls.Add(Theme.Say("payload", 382, y + 3, 54, dim: true));
            _mqttPayloadLaunch = Theme.Input(440, y, 90, m.PayloadLaunch); p.Controls.Add(_mqttPayloadLaunch);
            var t3 = Theme.Btn("Test", 538, y - 2, 94);
            t3.Click += (s, e) => TestMqtt(_mqttTopicLaunch.Text, _mqttPayloadLaunch.Text);
            p.Controls.Add(t3);
            y += 30;

            p.Controls.Add(Theme.Say("Exit topic", 26, y + 3, 80, dim: true));
            _mqttTopicExit = Theme.Input(110, y, 260, m.TopicExit); p.Controls.Add(_mqttTopicExit);
            p.Controls.Add(Theme.Say("payload", 382, y + 3, 54, dim: true));
            _mqttPayloadExit = Theme.Input(440, y, 90, m.PayloadExit); p.Controls.Add(_mqttPayloadExit);
            var t4 = Theme.Btn("Test", 538, y - 2, 94);
            t4.Click += (s, e) => TestMqtt(_mqttTopicExit.Text, _mqttPayloadExit.Text);
            p.Controls.Add(t4);
        }

        // =============================================================== wake
        private void BuildWakePage()
        {
            var p = NewPage("Controller wake");
            var y = 24;

            p.Controls.Add(Theme.Head("Controller wake", 24, y)); y += 28;
            p.Controls.Add(Theme.Say("Waking a sleeping PC with a pad is usually assumed to be hardware only. " +
                                     "It is not: the receiver also has to be armed in Windows, and often is not.",
                                     24, y, 630, dim: true));
            y += 36;

            var armBtn = Theme.Btn("Set up controller wake", 26, y, 180);
            armBtn.Click += (s, e) => ArmWake();
            p.Controls.Add(armBtn);

            var listBtn = Theme.Btn("Show wake devices", 214, y, 150);
            listBtn.Click += (s, e) => ShowWakeDevices();
            p.Controls.Add(listBtn);
            y += 44;

            p.Controls.Add(Theme.Say("If arming works but nothing happens, some boards gate it behind a BIOS " +
                                     "option called USB wake or wake on USB. Wired pads generally cannot wake a " +
                                     "PC at all; an Xbox Wireless Adapter or Bluetooth receiver generally can.",
                                     24, y, 630, dim: true));
        }

        // ============================================================= status
        private void BuildStatusPage()
        {
            var p = NewPage("Status");
            var y = 24;
            p.Controls.Add(Theme.Head("Status", 24, y)); y += 30;

            var card = Theme.Card(24, y, 620, 112);
            p.Controls.Add(card);
            _sController = Theme.Say("", 14, 12, 590);
            _sFrontend = Theme.Say("", 14, 34, 590);
            _sWake = Theme.Say("", 14, 56, 590);
            _sSleepState = Theme.Say("", 14, 80, 590, dim: true);
            card.Controls.AddRange(new Control[] { _sController, _sFrontend, _sWake, _sSleepState });
            y += 128;

            var openCfg = Theme.Btn("Open config file", 24, y, 130);
            openCfg.Click += (s, e) => Reveal(AppConfig.FilePath);
            p.Controls.Add(openCfg);

            var openLog = Theme.Btn("Open log", 162, y, 110);
            openLog.Click += (s, e) => Reveal(Log.Path_);
            p.Controls.Add(openLog);
        }

        private void RefreshStatus()
        {
            if (_sController == null) return;
            try
            {
                var pad = Native.AnyControllerConnected();
                _sController.Text = pad ? "Controller: connected" : "Controller: none connected";
                _sController.ForeColor = pad ? Theme.Good : Theme.Dim;

                var running = _engine.FrontendRunning();
                _sFrontend.Text = running ? "Frontend: running" : "Frontend: not running";
                _sFrontend.ForeColor = running ? Theme.Good : Theme.Dim;
            }
            catch { }
        }

        private void RefreshWake()
        {
            if (_sWake == null) return;
            try
            {
                var armed = WakeSetup.ControllersArmed();
                if (armed.Count > 0)
                {
                    _sWake.Text = "Controller wake: armed (" + armed[0] + ")";
                    _sWake.ForeColor = Theme.Good;
                }
                else
                {
                    var fixable = WakeSetup.ControllersNotArmed();
                    _sWake.Text = fixable.Count > 0
                        ? "Controller wake: not armed, and it can be"
                        : "Controller wake: no receiver reports wake support";
                    _sWake.ForeColor = fixable.Count > 0 ? Theme.Warn : Theme.Dim;
                }
                _sSleepState.Text = "Sleep: " + WakeSetup.SleepStateSummary();
            }
            catch (Exception ex) { Log.Write("wake check failed: " + ex.Message); }
        }

        // ============================================================ actions
        private void TestHook(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show(this, "That field is empty.", "CouchPilot"); return;
            }
            var ok = _engine.TestWebhook(url, Math.Max(1, _cfg.WebhookTimeoutSeconds));
            MessageBox.Show(this, ok ? "The webhook responded." :
                "No success response. The log has the exact error.", "CouchPilot");
        }

        private void TestMqtt(string topic, string payload)
        {
            ApplyMqtt();
            if (!_cfg.Mqtt.Enabled)
            {
                MessageBox.Show(this, "Tick Publish over MQTT first.", "CouchPilot"); return;
            }
            if (string.IsNullOrWhiteSpace(_cfg.Mqtt.Host))
            {
                MessageBox.Show(this, "Set the broker address first.", "CouchPilot"); return;
            }
            Cursor = Cursors.WaitCursor;
            bool ok;
            try { ok = Mqtt.Test(_cfg.Mqtt, topic, payload); }
            finally { Cursor = Cursors.Default; }

            MessageBox.Show(this, ok ? "Published." :
                "Could not publish. The log has the exact error.", "CouchPilot");
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
                        : "No receiver on this PC reports that it can wake the machine.\n\n" +
                          "Wired pads usually cannot. An Xbox Wireless Adapter or a Bluetooth\n" +
                          "receiver generally can, unless it is disabled in the BIOS.",
                    "CouchPilot");
                RefreshWake();
                return;
            }

            if (MessageBox.Show(this,
                    "These can wake the PC but are not armed:\n\n" + string.Join("\n", fixable) +
                    "\n\nArm them now? Windows will ask for administrator rights, because\n" +
                    "changing a device's wake setting requires it.",
                    "CouchPilot", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            foreach (var d in fixable) WakeSetup.Arm(d);
            System.Threading.Thread.Sleep(1200);
            RefreshWake();

            var still = WakeSetup.ControllersNotArmed();
            MessageBox.Show(this,
                still.Count == 0
                    ? "Done. Your controller can now wake this PC."
                    : "Still not armed:\n\n" + string.Join("\n", still) +
                      "\n\nThese often also need enabling in the BIOS, listed as USB wake.",
                "CouchPilot");
        }

        private void ShowWakeDevices()
        {
            var armed = WakeSetup.WakeArmed();
            var capable = WakeSetup.WakeCapable();
            var text = "ARMED - can wake the PC right now:\r\n" +
                       (armed.Count > 0 ? string.Join("\r\n", armed.Select(a => "  " + a)) : "  (none)") +
                       "\r\n\r\nCAPABLE - could be armed:\r\n" +
                       (capable.Count > 0 ? string.Join("\r\n", capable.Select(a => "  " + a)) : "  (none)");

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

        private void ApplyMqtt()
        {
            _cfg.Mqtt ??= new MqttSettings();
            _cfg.Mqtt.Enabled = _mqttEnabled.Checked;
            _cfg.Mqtt.Host = _mqttHost.Text.Trim();
            _cfg.Mqtt.Port = (int)_mqttPort.Value;
            _cfg.Mqtt.Username = _mqttUser.Text;
            _cfg.Mqtt.Password = _mqttPass.Text;
            _cfg.Mqtt.ClientId = _mqttClientId.Text.Trim();
            _cfg.Mqtt.Retain = _mqttRetain.Checked;
            _cfg.Mqtt.TopicLaunch = _mqttTopicLaunch.Text.Trim();
            _cfg.Mqtt.TopicExit = _mqttTopicExit.Text.Trim();
            _cfg.Mqtt.PayloadLaunch = _mqttPayloadLaunch.Text;
            _cfg.Mqtt.PayloadExit = _mqttPayloadExit.Text;
        }

        private void Apply()
        {
            _cfg.ShowChooser = _showChooser.Checked;
            _cfg.ChooserTimeoutSeconds = (int)_chooserTimeout.Value;

            _cfg.LaunchOnControllerConnect = _launchOnConnect.Checked;
            _cfg.LaunchOnWakeWithController = _launchOnWake.Checked;
            _cfg.SleepOnFrontendExit = _sleepOnExit.Checked;
            _cfg.RequireCleanExit = _requireClean.Checked;
            _cfg.RequireControllerForSleep = _requireController.Checked;

            _cfg.OnDisconnect = (DisconnectAction)Math.Max(0, _disconnectAction.SelectedIndex);
            _cfg.DisconnectPath = _disconnectPath.Text.Trim();
            _cfg.DisconnectArguments = _disconnectArgs.Text;
            _cfg.DisconnectDelaySeconds = (int)_disconnectDelay.Value;
            _cfg.DisconnectOnlyWhenFrontendClosed = _disconnectOnlyClosed.Checked;

            _cfg.MinimumRunSeconds = (int)_minRun.Value;
            _cfg.SleepDelaySeconds = (int)_sleepDelay.Value;
            _cfg.ControllerPollSeconds = (int)_poll.Value;
            _cfg.CooldownSeconds = (int)_cooldown.Value;

            _cfg.ShowNotifications = _showNotifications.Checked;
            _cfg.StartWithWindows = _startWithWindows.Checked;

            _cfg.WebhookOnLaunch = _hookLaunch.Text.Trim();
            _cfg.WebhookOnExit = _hookExit.Text.Trim();
            ApplyMqtt();

            _cfg.EnsureDefault();
            _cfg.FirstRunDone = true;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { _statusTimer?.Stop(); _statusTimer?.Dispose(); } catch { }
            base.OnFormClosed(e);
        }
    }
}
