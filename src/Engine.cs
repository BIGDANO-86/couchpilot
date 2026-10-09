using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CouchPilot
{
    /// <summary>
    /// The whole behaviour: notice a controller being switched on, launch the
    /// frontend, then notice the frontend being closed deliberately and sleep.
    /// </summary>
    internal sealed class Engine : IDisposable
    {
        private AppConfig _cfg;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task _loop;

        private bool _lastControllerState;
        private DateTime _lastTrigger = DateTime.MinValue;
        private DateTime _frontendStarted = DateTime.MinValue;
        private Process _watched;
        private bool _sleepPending;

        public bool Paused { get; set; }

        /// <summary>Raised for anything the user might want to see as a notification.</summary>
        public event Action<string> Notify;

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

        // ---- entry points for the Try it buttons in the settings window
        public void TestLaunch() { Log.Write("manual launch requested"); LaunchFrontend(); }

        public void TestSleep()
        {
            Log.Write("manual sleep requested");
            _lastControllerState = false;
            _lastTrigger = DateTime.MinValue;
            Task.Run(() => { try { Native.Sleep(); } catch (Exception ex) { Log.Write("sleep failed: " + ex.Message); } });
        }

        public bool TestWebhook(string url, int timeoutSeconds)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)) };
                using var resp = http.PostAsync(url, new StringContent("")).GetAwaiter().GetResult();
                Log.Write("webhook test " + url + " -> " + (resp.IsSuccessStatusCode ? "ok" : "failure status"));
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Log.Write("webhook test " + url + " failed: " + ex.Message);
                return false;
            }
        }

        public void Start()
        {
            // Baseline WITHOUT acting on it. A controller that is already on when
            // we start must not trigger a launch, or every boot would launch the
            // frontend.
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
                        await OnControllerSwitchedOn().ConfigureAwait(false);
                    else if (!now && _lastControllerState)
                        Log.Write("controller off");

                    _lastControllerState = now;

                    AttachToFrontendIfNeeded();
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { Log.Write("loop error: " + ex.Message); }
            }
        }

        /// <summary>
        /// On resume the poll loop has been frozen, so its remembered controller
        /// state is stale. Re-read it WITHOUT acting, unless the user has asked
        /// for a launch when the PC wakes with a pad already on. A resume caused
        /// by a keyboard would otherwise hijack the TV.
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
                    _lastControllerState = false; // let the normal path treat it as new
                    _ = OnControllerSwitchedOn();
                }
                else
                {
                    _lastControllerState = pad;
                }
            }
            catch (Exception ex) { Log.Write("resume handling failed: " + ex.Message); }
        }

        private async Task OnControllerSwitchedOn()
        {
            var since = (DateTime.Now - _lastTrigger).TotalSeconds;
            if (since < _cfg.CooldownSeconds)
            {
                Log.Write($"controller on, suppressed ({since:N0}s since last trigger)");
                return;
            }
            _lastTrigger = DateTime.Now;
            Say("Controller on, opening your frontend");

            if (!_cfg.LaunchOnControllerConnect) { Log.Write("     launching is disabled"); return; }

            await Fire(_cfg.WebhookOnLaunch, "launch webhook").ConfigureAwait(false);
            LaunchFrontend();
        }

        private void LaunchFrontend()
        {
            var path = _cfg.FrontendPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Log.Write("     no frontend configured or the path does not exist: " + path);
                return;
            }

            var watchName = _cfg.ResolveWatchName();
            try
            {
                var existing = Process.GetProcessesByName(watchName);
                if (existing.Length > 0)
                {
                    Log.Write($"     {watchName} already running (pid {existing[0].Id}), bringing it forward");
                    Native.BringToFront(existing[0].MainWindowHandle);
                    Track(existing[0]);
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    WorkingDirectory = Path.GetDirectoryName(path) ?? "",
                    UseShellExecute = true
                };
                Process.Start(psi);
                _frontendStarted = DateTime.Now;
                Log.Write("     launched " + Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                Log.Write("     launch FAILED: " + ex.Message);
            }
        }

        /// <summary>
        /// Find the real long-lived frontend process and hold a handle on it, so
        /// its exit code can be read. Polling for the process to disappear cannot
        /// tell a deliberate quit from a crash; the exit code can.
        /// </summary>
        private void AttachToFrontendIfNeeded()
        {
            if (_watched != null && !_watched.HasExited) return;

            var watchName = _cfg.ResolveWatchName();
            if (string.IsNullOrWhiteSpace(watchName)) return;

            try
            {
                var procs = Process.GetProcessesByName(watchName);
                if (procs.Length == 0) return;
                Track(procs[0]);
            }
            catch (Exception ex) { Log.Write("attach failed: " + ex.Message); }
        }

        private void Track(Process p)
        {
            try
            {
                if (_watched != null && _watched.Id == p.Id) return;

                _watched = p;
                if (_frontendStarted == DateTime.MinValue) _frontendStarted = DateTime.Now;
                p.EnableRaisingEvents = true;
                p.Exited += OnFrontendExited;
                Log.Write($"watching {p.ProcessName} pid {p.Id}");
            }
            catch (Exception ex) { Log.Write("could not watch process: " + ex.Message); }
        }

        private void OnFrontendExited(object sender, EventArgs e)
        {
            var p = sender as Process;
            int code = -1;
            try { code = p?.ExitCode ?? -1; } catch { }

            var ranFor = (DateTime.Now - _frontendStarted).TotalSeconds;
            _frontendStarted = DateTime.MinValue;
            _watched = null;

            Log.Write($"frontend exited, code {code}, after {ranFor:N0}s");

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
                await Fire(_cfg.WebhookOnExit, "exit webhook").ConfigureAwait(false);

                var wait = Math.Max(0, _cfg.SleepDelaySeconds);
                if (wait > 0) await Task.Delay(TimeSpan.FromSeconds(wait)).ConfigureAwait(false);

                // Re-arm the controller baseline. The pad is on right now, and the
                // poll loop is frozen while suspended, so without this a pad still
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
            catch (Exception ex)
            {
                Log.Write($"     {label} failed: " + ex.Message);
            }
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
