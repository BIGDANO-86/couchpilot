using System;
using System.Runtime.InteropServices;

namespace CouchPilot
{
    /// <summary>All P/Invoke in one place so the surface is easy to audit.</summary>
    internal static class Native
    {
        // ---- XInput. xinput1_4.dll ships with Windows 8 and later.
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState14(uint dwUserIndex, byte[] pState);

        // Older fallback, present on some systems via the DirectX redist.
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState910(uint dwUserIndex, byte[] pState);

        private static bool _use910;
        private static bool _probed;

        /// <summary>True if any of the four XInput slots has a pad connected.</summary>
        public static bool AnyControllerConnected()
        {
            var buf = new byte[16];
            for (uint i = 0; i < 4; i++)
            {
                uint r;
                try
                {
                    if (!_probed)
                    {
                        try { r = XInputGetState14(i, buf); _use910 = false; }
                        catch (DllNotFoundException) { r = XInputGetState910(i, buf); _use910 = true; }
                        _probed = true;
                    }
                    else
                    {
                        r = _use910 ? XInputGetState910(i, buf) : XInputGetState14(i, buf);
                    }
                }
                catch (Exception)
                {
                    // No XInput at all. Report "not connected" deliberately:
                    // a false negative leaves the user alone, a false positive
                    // would launch things unasked.
                    return false;
                }

                if (r == 0) return true; // ERROR_SUCCESS
            }
            return false;
        }

        // ---- Suspend
        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        /// <summary>Puts the machine to sleep. Does not return until it wakes.</summary>
        public static void Sleep()
        {
            SetSuspendState(false, false, false);
        }

        // ---- Foreground window, used to bring an already-running frontend forward
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        public static void BringToFront(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            ShowWindow(hWnd, SW_RESTORE);
            SetForegroundWindow(hWnd);
        }
    }
}
