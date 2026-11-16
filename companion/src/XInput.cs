using System;
using System.Runtime.InteropServices;

namespace HandsCompanion
{
    /* XInput polling, translated to the studio's button ids.
       The studio names buttons "g{pad}b{index}" where the index follows
       the W3C standard gamepad mapping (see the "standard" preset in
       index.html). XInput's button bits are therefore translated to
       those same indices:
         A=0 B=1 X=2 Y=3 LB=4 RB=5 LT=6 RT=7 View=8 Menu=9 L3=10 R3=11
         DPadUp=12 DPadDown=13 DPadLeft=14 DPadRight=15
       Triggers count as pressed past the XInput threshold (30/255).
       Index 16 (Home/Guide) cannot be read through XInputGetState and is
       never reported; profiles using g{b}16 will not fire from a pad.
       Pad slots 0..3 become g0..g3. Note the studio's pad number is the
       browser's connection order while XInput's is the player slot; for
       a single controller both are 0. */
    public static class XInput
    {
        public const int ButtonCount = 17;
        private const int ErrorSuccess = 0;
        private const int ErrorDeviceNotConnected = 1167;
        private const byte TriggerThreshold = 30;

        [StructLayout(LayoutKind.Sequential)]
        private struct Gamepad
        {
            public ushort Buttons;
            public byte LeftTrigger;
            public byte RightTrigger;
            public short ThumbLX, ThumbLY, ThumbRX, ThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct State
        {
            public uint PacketNumber;
            public Gamepad Pad;
        }

        private static class XInput14
        {
            [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
            public static extern int GetState(uint userIndex, ref State state);
        }

        private static class XInput910
        {
            [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
            public static extern int GetState(uint userIndex, ref State state);
        }

        private static int which = 0; /* 0 = unprobed, 1 = 1.4, 2 = 9.1.0, -1 = none */

        public static string DllName
        {
            get { return which == 1 ? "xinput1_4.dll" : which == 2 ? "xinput9_1_0.dll" : "(none)"; }
        }

        /* Returns null when an XInput DLL is usable, else the problem. */
        public static string EnsureAvailable()
        {
            if (which != 0) return which < 0 ? "no XInput DLL found (tried xinput1_4.dll and xinput9_1_0.dll)" : null;
            var st = new State();
            try { XInput14.GetState(0, ref st); which = 1; return null; }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            try { XInput910.GetState(0, ref st); which = 2; return null; }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            which = -1;
            return "no XInput DLL found (tried xinput1_4.dll and xinput9_1_0.dll)";
        }

        private static int GetStateRaw(uint slot, ref State st)
        {
            if (which == 1) return XInput14.GetState(slot, ref st);
            if (which == 2) return XInput910.GetState(slot, ref st);
            return ErrorDeviceNotConnected;
        }

        private static readonly ushort[] Bits =
        {
            0x1000, /* 0 A */  0x2000, /* 1 B */  0x4000, /* 2 X */  0x8000, /* 3 Y */
            0x0100, /* 4 LB */ 0x0200, /* 5 RB */
            0,      /* 6 LT (analog) */ 0, /* 7 RT (analog) */
            0x0020, /* 8 View/Back */ 0x0010, /* 9 Menu/Start */
            0x0040, /* 10 L3 */ 0x0080, /* 11 R3 */
            0x0001, /* 12 DPadUp */ 0x0002, /* 13 DPadDown */
            0x0004, /* 14 DPadLeft */ 0x0008  /* 15 DPadRight */
            /* 16 Home: not exposed by XInputGetState */
        };

        /* Fills pressed[0..16]; returns false when the slot has no
           connected controller. */
        public static bool TryGetPressed(int slot, bool[] pressed)
        {
            var st = new State();
            int rc = GetStateRaw((uint)slot, ref st);
            if (rc != ErrorSuccess) return false;
            for (int i = 0; i < 16; i++)
            {
                if (i == 6) pressed[i] = st.Pad.LeftTrigger > TriggerThreshold;
                else if (i == 7) pressed[i] = st.Pad.RightTrigger > TriggerThreshold;
                else pressed[i] = (st.Pad.Buttons & Bits[i]) != 0;
            }
            pressed[16] = false;
            return true;
        }
    }
}
