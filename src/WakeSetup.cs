using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace CouchPilot
{
    /// <summary>
    /// Controller wake is commonly assumed to be a hardware-only trait. It is
    /// not: the device also has to be ARMED in Windows. powercfg exposes both
    /// the armed list and the ability to arm a device, so this surfaces that
    /// and offers to fix it.
    /// </summary>
    internal static class WakeSetup
    {
        /// <summary>Words that usually mean "this is a gamepad or its receiver".</summary>
        private static readonly string[] Hints =
        {
            "xbox", "controller", "gamepad", "wireless adapter", "dualshock",
            "dualsense", "8bitdo", "hid-compliant game"
        };

        public static string Run(string args, bool elevated = false)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = args,
                    UseShellExecute = elevated,
                    CreateNoWindow = true,
                    RedirectStandardOutput = !elevated,
                    RedirectStandardError = !elevated
                };
                if (elevated) psi.Verb = "runas";

                using var p = Process.Start(psi);
                if (p == null) return "";
                if (elevated) { p.WaitForExit(20000); return ""; }

                var sb = new StringBuilder();
                sb.Append(p.StandardOutput.ReadToEnd());
                sb.Append(p.StandardError.ReadToEnd());
                p.WaitForExit(15000);
                return sb.ToString();
            }
            catch (Exception ex)
            {
                Log.Write("powercfg " + args + " failed: " + ex.Message);
                return "";
            }
        }

        public static List<string> WakeArmed() => Lines(Run("-devicequery wake_armed"));

        public static List<string> WakeCapable() => Lines(Run("-devicequery wake_programmable"));

        private static List<string> Lines(string raw) =>
            (raw ?? "")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("NONE", StringComparison.OrdinalIgnoreCase))
                .ToList();

        public static bool LooksLikeController(string device) =>
            Hints.Any(h => device.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>Controller-ish devices that CAN wake the PC but are not armed.</summary>
        public static List<string> ControllersNotArmed()
        {
            var armed = WakeArmed();
            return WakeCapable()
                .Where(LooksLikeController)
                .Where(d => !armed.Any(a => a.Equals(d, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        public static List<string> ControllersArmed() =>
            WakeArmed().Where(LooksLikeController).ToList();

        /// <summary>Arming a device needs administrator rights, so this elevates.</summary>
        public static void Arm(string device)
        {
            Log.Write("arming wake device: " + device);
            Run("-deviceenablewake \"" + device + "\"", elevated: true);
        }

        public static void Disarm(string device)
        {
            Log.Write("disarming wake device: " + device);
            Run("-devicedisablewake \"" + device + "\"", elevated: true);
        }

        /// <summary>
        /// Modern Standby machines behave differently: they stay partly awake
        /// and keep answering the network, so "is it asleep" checks and some
        /// wake paths do not work the way they do on S3.
        /// </summary>
        public static string SleepStateSummary()
        {
            var raw = Run("/a");
            if (string.IsNullOrWhiteSpace(raw)) return "could not read sleep states";

            var s3 = raw.IndexOf("Standby (S3)", StringComparison.OrdinalIgnoreCase) >= 0;
            var s0 = raw.IndexOf("S0 Low Power Idle", StringComparison.OrdinalIgnoreCase) >= 0;

            // powercfg /a lists both available AND unavailable states, so the
            // available block has to be isolated before concluding anything.
            var availablePart = raw;
            var notIdx = raw.IndexOf("are not available", StringComparison.OrdinalIgnoreCase);
            if (notIdx > 0) availablePart = raw.Substring(0, notIdx);

            var hasS3 = availablePart.IndexOf("Standby (S3)", StringComparison.OrdinalIgnoreCase) >= 0;
            var hasS0 = availablePart.IndexOf("S0 Low Power Idle", StringComparison.OrdinalIgnoreCase) >= 0;

            if (hasS3) return "Standby (S3) - normal sleep, controller wake works well";
            if (hasS0) return "Modern Standby (S0) - the PC stays partly awake; wake behaviour varies";
            if (s3 || s0) return "sleep states found but none appear available";
            return "no sleep state detected - the PC may not be able to sleep";
        }
    }
}
