using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace HandsCompanion
{
    public sealed class SendResult
    {
        public int Sent;
        public int Total;
        public string Error;   /* null on full success */
        public bool Ok { get { return Error == null && Sent == Total; } }
    }

    /* Sends planned key operations with SendInput using scan codes
       (KEYEVENTF_SCANCODE), as the design memo prescribes, so games that
       read raw input accept the keys. A partial or refused send is
       reported in the result — including the foreground window's title,
       so "injection refused" is visible per target instead of keys
       silently vanishing (anti-cheat and UIPI can both cause this). */
    public static class WindowsSender
    {
        private const int InputKeyboard = 1;
        private const uint KeyeventfExtendedKey = 0x0001;
        private const uint KeyeventfKeyUp = 0x0002;
        private const uint KeyeventfScancode = 0x0008;

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int Dx, Dy;
            public uint MouseData, Flags, Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeybdInput
        {
            public ushort Vk, Scan;
            public uint Flags, Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HardwareInput
        {
            public uint Msg;
            public ushort ParamL, ParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MouseInput Mouse;
            [FieldOffset(0)] public KeybdInput Keyboard;
            [FieldOffset(0)] public HardwareInput Hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public int Type;
            public InputUnion U;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        public static string ForegroundTitle()
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero) return "(no foreground window)";
                var sb = new StringBuilder(256);
                int n = GetWindowText(h, sb, sb.Capacity);
                return n > 0 ? sb.ToString() : "(untitled window)";
            }
            catch (Exception)
            {
                return "(title unavailable)";
            }
        }

        public static SendResult Send(List<SendOp> ops)
        {
            var result = new SendResult { Sent = 0, Total = ops.Count, Error = null };
            var batch = new List<Input>();
            for (int i = 0; i < ops.Count; i++)
            {
                var op = ops[i];
                if (op.DelayBeforeMs > 0)
                {
                    Flush(batch, result);
                    if (result.Error != null) return result;
                    Thread.Sleep(op.DelayBeforeMs);
                }
                uint flags = KeyeventfScancode;
                if (op.Extended) flags |= KeyeventfExtendedKey;
                if (op.IsUp) flags |= KeyeventfKeyUp;
                var input = new Input
                {
                    Type = InputKeyboard,
                    U = new InputUnion
                    {
                        Keyboard = new KeybdInput
                        {
                            Vk = 0,
                            Scan = op.Scan,
                            Flags = flags,
                            Time = 0,
                            ExtraInfo = UIntPtr.Zero
                        }
                    }
                };
                batch.Add(input);
            }
            Flush(batch, result);
            return result;
        }

        private static void Flush(List<Input> batch, SendResult result)
        {
            if (batch.Count == 0) return;
            var arr = batch.ToArray();
            batch.Clear();
            uint sent = SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(Input)));
            result.Sent += (int)sent;
            if (sent != (uint)arr.Length)
            {
                int err = Marshal.GetLastWin32Error();
                result.Error = "SendInput accepted " + sent + " of " + arr.Length +
                    " key events (win32 error " + err + "); foreground window: " + ForegroundTitle() +
                    ". The target may be elevated or protected (anti-cheat / UIPI).";
            }
        }
    }
}
