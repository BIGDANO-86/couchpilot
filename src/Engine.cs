using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CouchPilot
{
    /// <summary>
    /// The whole behaviour: notice a controller being switched on, open a
    /// frontend (asking which, if there is more than one), then notice the
    /// frontend being closed deliberately and sleep.
    /// </summary>
    internal sealed class Engine : IDisposable
    {
        private AppConfig _cfg;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task _loop;

        private bool _lastControllerState;
        private DateTime _lastTrigger = DateTime.MinValue;
        private DateTime _frontendStarted = DateTime.MinValue;
        private DateTime _disconnectedAt = DateTime.MinValue;
        private bool _disconnectActionDone;
        private Process _watched;
        private FrontendEntry _watchedEntry;
        private bool _sleepPending;
        private bool _chooserOpen;

        public bool Paused { get; set; }

        /// <summary>Raised for anything worth a brief notification.</summary>
        public event Action<string> Notify;

        /// <summary>
        /// Set by the tray app. The chooser is a window, so it has to be created
        /// on the UI thread, while the engine runs on a worker.
        /// </summary>
        public Func<List<FrontendEntry>, string, int, FrontendEntry> AskWhich { get; set; }

        public Engine(AppConfig cfg) { _cfg = cfg; }

        public void UpdateConfig(AppConfig cfg) { _cfg = cfg; }

        private void Say(string message)
        {
            Log.Write(message);
            if (_cfg.ShowNotifications)
            {
                try { Notify?.Invoke(message); } catch { }
            }
        }

        // ------------------------------------------------------- test entry points
        public void TestLaunch(FrontendEntry f = null)
        {
            Log.Write("manual launch requested");
            Launch(f ?? _cfg.Default());
        }

        public void TestSleep()
        {
            Log.Write("manual sleep requested");
            _lastControllerState = false;
            _lastTrigger = DateTime.MinValue;
            Task.Run(() => Native.Sleep());
        }

        public bool TestWebhook(string url, int timeoutSeconds)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)) };
                using var resp = http.PostAsync(url, new StringContent("")).GetAwaiter().GetResult();
                Log.Write("webhook test -> " + (resp.IsSuccessStatusCode ? "ok" : "failure status"));
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Log.Write("webhook test failed: " + ex.Message);
                return false;
            }
        }

        // ------------------------------------------------------------------ loop
        public void Start()
        {
            // Baseline WITHOUT acting on it. A controller already on when we
            // start must not launch anything, or every boot would.
            _lastControllerState = Native.AnyControllerConnected();
            Log.Write($"engine starting, controller present at start: {_lastControllerState}");

            try { Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged; }
            catch (Exception ex) { Log.Write("could not hook power events: " + ex.Message); }

            _loop = Task.Run(() => Loop(_cts.Token));
        }

        private async Task Loop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _cfg.ControllerPollSeconds)), ct)
                              .ConfigureAwait(false);
                    if (Paused) continue;

                    var now = Native.AnyControllerConnected();

                    if (now && !_lastControllerState)
                    {
                        _disconnectedAt = DateTime.MinValue;
                        _disconnectActionDone = false;
                        await OnControllerSwitchedOn().ConfigureAwait(false);
                    }
                    else if (!now && _lastControllerState)
                    {
                        _disconnectedAt = DateTime.Now;
                        _disconnectActionDone = false;
                        Log.Write("controller off");
                    }

                    _lastControllerState = now;

                    AttachToFrontendIfNeeded();
                    CheckDisconnectAction(now);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { Log.Write("loop error: " + ex.Message); }
            }
        }

        /// <summary>
        /// After a resume the remembered controller state is stale, because the
        /// loop was frozen. Re-read it WITHOUT acting, otherwise a keyboard wake
        /// would hijack the TV. Launching on a wake is an explicit opt-in.
        /// </summary>
        private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode != Microsoft.Win32.PowerModes.Resume) return;
            try
            {
                var pad = Native.AnyControllerConnected();
                Log.Write($"resumed from sleep, controller present: {pad}");

                if (pad && _cfg.LaunchOnWakeWithController && !Paused)
                {
                    _lastControllerState = false;
                    _ = OnControllerSwitchedOn();
                }
                else _lastControllerState = pad;
            }
            catch (Exception ex) { Log.Write("resume handling failed: " + ex.Message); }
        }

        // ------------------------------------------------------- controller on
        private async Task OnControllerSwitchedOn()
        {
            var since = (DateTime.Now - _lastTrigger).TotalSeconds;
            if (since < _cfg.CooldownSeconds)
            {
                Log.Write($"controller on, suppressed ({since:N0}s since last trigger)");
                return;
            }
            _lastTrigger = DateTime.Now;

            if (!_cfg.LaunchOnControllerConnect) { Log.Write("controller on, launching is disabled"); return; }

            var choices = _cfg.EnabledFrontends();
            if (choices.Count == 0)
            {
                Say("Controller on, but no frontend is set up");
                return;
            }

            FrontendEntry pick;
            if (choices.Count > 1 && _cfg.ShowChooser && AskWhich != null && !_chooserOpen)
            {
                _chooserOpen = true;
                try
                {
                    Log.Write("controller on, asking which frontend");
                    pick = AskWhich(choices, _cfg.Default()?.Name ?? choices[0].Name, _cfg.ChooserTimeoutSeconds);
                }
                finally { _chooserOpen = false; }

                if (pick == null) { Log.Write("     chooser cancelled"); return; }
            }
            else
            {
                pick = _cfg.Default() ?? choices[0];
            }

            Say("Opening " + pick.Name);
            await FireAll(_cfg.WebhookOnLaunch, _cfg.Mqtt?.TopicLaunch, _cfg.Mqtt?.PayloadLaunch, "launch")
                .ConfigureAwait(false);
            Launch(pick);
        }

        private void Launch(FrontendEntry f)
        {
            if (f == null) { Log.Write("     nothing to launch"); return; }
            if (!f.Exists()) { Log.Write("     path does not exist: " + f.Path); return; }

            var watch = f.ResolveWatch();
            try
            {
                var existing = Process.GetProcessesByName(watch);
                if (existing.Length > 0)
                {
                    Log.Write($"     {watch} already running (pid {existing[0].Id}), bringing it forward");
                    Native.BringToFront(existing[0].MainWindowHandle);
                    Track(existing[0], f);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = f.Path,
                    Arguments = f.Arguments ?? "",
                    WorkingDirectory = Path.GetDirectoryName(f.Path) ?? "",
                    UseShellExecute = true
                });
                _watchedEntry = f;
                _frontendStarted = DateTime.Now;
                Log.Write("     launched " + f.Name);
            }
            catch (Exception ex) { Log.Write("     launch FAILED: " + ex.Message); }
        }

        // --------------------------------------------------------- frontend exit
        /// <summary>
        /// Hold a handle on the real frontend process so its EXIT CODE can be
        /// read. Polling for the process to vanish cannot tell a deliberate quit
        /// from a crash; the exit code can.
        /// </summary>
        private void AttachToFrontendIfNeeded()
        {
            if (_watched != null && !_watched.HasExited) return;

            foreach (var f in _cfg.EnabledFrontends())
            {
                var watch = f.ResolveWatch();
                if (string.IsNullOrWhiteSpace(watch)) continue;
                try
                {
                    var procs = Process.GetProcessesByName(watch);
                    if (procs.Length == 0) continue;
                    Track(procs[0], f);
                    return;
                }
                catch (Exception ex) { Log.Write("attach failed: " + ex.Message); }
            }
        }

        private void Track(Process p, FrontendEntry f)
        {
            try
            {
                if (_watched != null && !_watched.HasExited && _watched.Id == p.Id) return;

                _watched = p;
                _watchedEntry = f;
                if (_frontendStarted == DateTime.MinValue) _frontendStarted = DateTime.Now;
                p.EnableRaisingEvents = true;
                p.Exited += OnFrontendExited;
                Log.Write($"watching {p.ProcessName} pid {p.Id} ({f.Name})");
            }
            catch (Exception ex) { Log.Write("could not watch process: " + ex.Message); }
        }

        private void OnFrontendExited(object sender, EventArgs e)
        {
            var p = sender as Process;
            int code = -1;
            try { code = p?.ExitCode ?? -1; } catch { }

            var ranFor = (DateTime.Now - _frontendStarted).TotalSeconds;
            var name = _watchedEntry?.Name ?? "frontend";
            _frontendStarted = DateTime.MinValue;
            _watched = null;

            Log.Write($"{name} exited, code {code}, after {ranFor:N0}s");

            if (!_cfg.SleepOnFrontendExit) { Log.Write("     sleeping is disabled"); return; }

            if (_cfg.RequireCleanExit && code != 0)
            {
                Log.Write("     not a clean exit, leaving the PC awake");
                return;
            }
            if (ranFor < _cfg.MinimumRunSeconds)
            {
                Log.Write($"     only ran {ranFor:N0}s, under the {_cfg.MinimumRunSeconds}s minimum, ignoring");
                return;
            }
            if (_cfg.RequireControllerForSleep && !Native.AnyControllerConnected())
            {
                Log.Write("     no controller connected, leaving the PC awake");
                return;
            }

            if (_sleepPending) return;
            _sleepPending = true;
            _ = Task.Run(SleepSoon);
        }

        private async Task SleepSoon()
        {
            try
            {
                await FireAll(_cfg.WebhookOnExit, _cfg.Mqtt?.TopicExit, _cfg.Mqtt?.PayloadExit, "exit")
                    .ConfigureAwait(false);

                var wait = Math.Max(0, _cfg.SleepDelaySeconds);
                if (wait > 0) await Task.Delay(TimeSpan.FromSeconds(wait)).ConfigureAwait(false);

                // Re-arm the controller baseline. The pad is on right now, and the
                // poll loop freezes while suspended, so without this a pad still
                // present on resume would look unchanged and would never trigger
                // the next launch.
                _lastControllerState = false;
                _lastTrigger = DateTime.MinValue;

                Say("Going to sleep");
                Native.Sleep();
                Log.Write("resumed from sleep");
            }
            catch (Exception ex) { Log.Write("sleep failed: " + ex.Message); }
            finally { _sleepPending = false; }
        }

        // ---------------------------------------------------- controller off
        /// <summary>
        /// Do something when the pad goes away. Pads idle off on their own after
        /// a quarter of an hour, so this waits a good while and, by default,
        /// skips entirely while the frontend is still up.
        /// </summary>
        private void CheckDisconnectAction(bool controllerNow)
        {
            if (_cfg.OnDisconnect == DisconnectAction.None) return;
            if (controllerNow || _disconnectActionDone) return;
            if (_disconnectedAt == DateTime.MinValue) return;

            if ((DateTime.Now - _disconnectedAt).TotalSeconds < Math.Max(0, _cfg.DisconnectDelaySeconds)) return;

            if (_cfg.DisconnectOnlyWhenFrontendClosed && FrontendRunning())
            {
                Log.Write("controller still off, but the frontend is running, so no disconnect action");
                _disconnectActionDone = true;
                return;
            }

            _disconnectActionDone = true;

            if (_cfg.OnDisconnect == DisconnectAction.Sleep)
            {
                Say("Controller off, sleeping");
                _lastControllerState = false;
                _lastTrigger = DateTime.MinValue;
                Task.Run(() => Native.Sleep());
                return;
            }

            if (string.IsNullOrWhiteSpace(_cfg.DisconnectPath)) return;
            try
            {
                Say("Controller off, running your disconnect action");
                Process.Start(new ProcessStartInfo
                {
                    FileName = _cfg.DisconnectPath,
                    Arguments = _cfg.DisconnectArguments ?? "",
                    UseShellExecute = true
                });
            }
            catch (Exception ex) { Log.Write("disconnect action failed: " + ex.Message); }
        }

        public bool FrontendRunning()
        {
            foreach (var f in _cfg.EnabledFrontends())
            {
                var w = f.ResolveWatch();
                if (string.IsNullOrWhiteSpace(w)) continue;
                try { if (Process.GetProcessesByName(w).Length > 0) return true; } catch { }
            }
            return false;
        }

        // --------------------------------------------------------- integrations
        private async Task FireAll(string url, string topic, string payload, string label)
        {
            await Fire(url, label + " webhook").ConfigureAwait(false);
            if (_cfg.Mqtt != null && _cfg.Mqtt.Enabled)
                await Mqtt.Publish(_cfg.Mqtt, topic, payload).ConfigureAwait(false);
        }

        private async Task Fire(string url, string label)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                using var http = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(Math.Max(1, _cfg.WebhookTimeoutSeconds))
                };
                using var resp = await http.PostAsync(url, new StringContent("")).ConfigureAwait(false);
                Log.Write($"     {label} " + (resp.IsSuccessStatusCode ? "ok" : "returned a failure status"));
            }
            catch (Exception ex) { Log.Write($"     {label} failed: " + ex.Message); }
        }

        public void Dispose()
        {
            try { Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged; } catch { }
            try { _cts.Cancel(); } catch { }
            try { _loop?.Wait(2000); } catch { }
            _cts.Dispose();
        }
    }
}
