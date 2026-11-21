using System;
using System.Collections.Generic;
using System.IO;
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

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder text, ref uint length);

        private const uint ProcessQueryLimitedInformation = 0x1000;

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

        /* File name (e.g. "notepad.exe") of the process that owns the
           foreground window, or "" when it cannot be determined. Used
           only for the profile target filter's processName check: it
           reads the foreground window's owning process image name with
           query-only access and never opens, inspects or alters the
           target process itself. */
        public static string ForegroundProcessName()
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero) return "";
                uint pid;
                if (GetWindowThreadProcessId(h, out pid) == 0 || pid == 0) return "";
                IntPtr proc = OpenProcess(ProcessQueryLimitedInformation, false, pid);
                if (proc == IntPtr.Zero) return "";
                try
                {
                    var sb = new StringBuilder(1024);
                    uint len = (uint)sb.Capacity;
                    if (!QueryFullProcessImageName(proc, 0, sb, ref len)) return "";
                    return Path.GetFileName(sb.ToString());
                }
                finally { CloseHandle(proc); }
            }
            catch (Exception)
            {
                return "";
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
