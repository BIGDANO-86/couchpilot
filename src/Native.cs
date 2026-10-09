using System;
using System.Runtime.InteropServices;

namespace CouchPilot
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [Flags]
    internal enum PadButton : ushort
    {
        None = 0,
        DPadUp = 0x0001,
        DPadDown = 0x0002,
        DPadLeft = 0x0004,
        DPadRight = 0x0008,
        Start = 0x0010,
        Back = 0x0020,
        LeftThumb = 0x0040,
        RightThumb = 0x0080,
        LeftShoulder = 0x0100,
        RightShoulder = 0x0200,
        A = 0x1000,
        B = 0x2000,
        X = 0x4000,
        Y = 0x8000
    }

    /// <summary>All P/Invoke in one place so the surface is easy to audit.</summary>
    internal static class Native
    {
        // xinput1_4.dll ships with Windows 8 and later. xinput9_1_0.dll is the
        // older fallback and is present on practically everything.
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint GetState14(uint index, out XInputState state);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern uint GetState910(uint index, out XInputState state);

        private static bool _use910;
        private static bool _probed;
        private static bool _unavailable;

        private static uint Query(uint index, out XInputState state)
        {
            state = default;
            if (_unavailable) return 1;

            try
            {
                if (!_probed)
                {
                    try { var r = GetState14(index, out state); _use910 = false; _probed = true; return r; }
                    catch (DllNotFoundException)
                    {
                        var r = GetState910(index, out state);
                        _use910 = true; _probed = true;
                        return r;
                    }
                    catch (EntryPointNotFoundException)
                    {
                        var r = GetState910(index, out state);
                        _use910 = true; _probed = true;
                        return r;
                    }
                }
                return _use910 ? GetState910(index, out state) : GetState14(index, out state);
            }
            catch (Exception ex)
            {
                _unavailable = true;
                Log.Write("XInput unavailable, controller features are off: " + ex.Message);
                return 1;
            }
        }

        /// <summary>True if any of the four XInput slots has a pad connected.</summary>
        public static bool AnyControllerConnected()
        {
            for (uint i = 0; i < 4; i++)
                if (Query(i, out _) == 0) return true;   // ERROR_SUCCESS
            return false;
        }

        /// <summary>Buttons held across every connected pad, merged together.</summary>
        public static PadButton PressedButtons()
        {
            ushort merged = 0;
            for (uint i = 0; i < 4; i++)
                if (Query(i, out var s) == 0) merged |= s.Gamepad.Buttons;
            return (PadButton)merged;
        }

        /// <summary>
        /// Left stick as a coarse direction, with a deadzone, merged across pads.
        /// Returned as -1, 0 or 1 on each axis so menus can treat the stick and
        /// the d-pad identically.
        /// </summary>
        public static void StickDirection(out int x, out int y)
        {
            const short Dead = 16000;
            x = 0; y = 0;
            for (uint i = 0; i < 4; i++)
            {
                if (Query(i, out var s) != 0) continue;
                if (s.Gamepad.ThumbLX > Dead) x = 1;
                else if (s.Gamepad.ThumbLX < -Dead) x = -1;
                if (s.Gamepad.ThumbLY > Dead) y = 1;
                else if (s.Gamepad.ThumbLY < -Dead) y = -1;
            }
        }

        // ---- Suspend
        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern bool SetSuspendStateNative(bool hibernate, bool forceCritical, bool disableWakeEvent);

        /// <summary>Puts the machine to sleep. Does not return until it wakes.</summary>
        public static void Sleep()
        {
            try { SetSuspendStateNative(false, false, false); }
            catch (Exception ex) { Log.Write("suspend failed: " + ex.Message); }
        }

        // ---- Window helpers
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        public static void BringToFront(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            try { ShowWindow(hWnd, SW_RESTORE); SetForegroundWindow(hWnd); } catch { }
        }
    }
}
