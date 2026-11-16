using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace HandsCompanion
{
    /* Hands companion (Windows, minimal): plays a Hands profile on the
       real desktop. Reads the profile JSON the studio exported, polls
       XInput controllers, runs the detection engine (the HANDS CORE
       semantics), and sends the fired keys to the focused window with
       SendInput. It has no settings UI — authoring stays in the studio —
       and it holds no data beyond its log file.
       Stop it with Ctrl+C, by closing the window, or with the panic
       chord Ctrl+Alt+F12 (works even when another window has focus). */
    internal static class Program
    {
        private static volatile bool stopRequested = false;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        private const int VkControl = 0x11, VkMenu = 0x12, VkF12 = 0x7B;

        private static int Main(string[] args)
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            Log.Init(Path.Combine(exeDir, "hands-companion.log"));
            try
            {
                return Run(args, exeDir);
            }
            catch (Exception e)
            {
                Log.Error("fatal: " + e);
                return 1;
            }
        }

        private static int Run(string[] args, string exeDir)
        {
            bool dryRun = false;
            bool selfTest = false;
            string profilePath = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--dry-run") dryRun = true;
                else if (a == "--selftest-send") selfTest = true;
                else if (a == "--help" || a == "-h" || a == "/?")
                {
                    PrintUsage();
                    return 0;
                }
                else if (a.StartsWith("--"))
                {
                    Log.Error("unknown option: " + a);
                    PrintUsage();
                    return 2;
                }
                else if (profilePath == null) profilePath = a;
                else
                {
                    Log.Error("unexpected extra argument: " + a);
                    PrintUsage();
                    return 2;
                }
            }

            if (selfTest) return RunSelfTest();

            if (profilePath == null)
            {
                profilePath = Path.Combine(exeDir, "profile.json");
                Log.Info("no profile argument; looking for " + profilePath);
            }
            if (!File.Exists(profilePath))
            {
                Log.Error("profile file not found: " + profilePath);
                Log.Error("export a profile from the Hands studio (index.html) and pass its path, or save it as profile.json next to this program.");
                return 2;
            }

            string text;
            try { text = File.ReadAllText(profilePath, Encoding.UTF8); }
            catch (Exception e)
            {
                Log.Error("cannot read profile file: " + e.Message);
                return 2;
            }
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);

            var loaded = ProfileIO.ImportProfile(text);
            if (loaded.Profile == null || loaded.Errors.Count > 0)
            {
                Log.Error("profile rejected (" + profilePath + "):");
                foreach (var err in loaded.Errors) Log.Error("  - " + err);
                if (loaded.Profile == null && loaded.Errors.Count == 0)
                    Log.Error("  - not a Hands profile file");
                return 2;
            }
            var profile = loaded.Profile;

            var keyErrors = KeyMap.ValidateProfileKeys(profile);
            if (keyErrors.Count > 0)
            {
                Log.Error("profile uses keys this companion cannot send:");
                foreach (var err in keyErrors) Log.Error("  - " + err);
                return 2;
            }

            var engine = new Engine(ExtractRawRules(text));
            if (engine.Errors.Count > 0)
            {
                Log.Error("profile rejected by the detection engine:");
                foreach (var err in engine.Errors) Log.Error("  - " + err);
                return 2;
            }

            string xerr = XInput.EnsureAvailable();
            if (xerr != null)
            {
                Log.Error("input source unavailable: " + xerr);
                return 3;
            }

            Log.Info("Hands companion starting");
            Log.Info("profile: \"" + profile.Name + "\" (" + engine.RuleCount + " active rules) from " + profilePath);
            foreach (var rule in profile.Rules)
            {
                if (rule.Enabled)
                    Log.Info("  [" + rule.Name + "] " + ProfileIO.DescribeTrigger(rule.Trigger) + " -> " + ProfileIO.DescribeAction(rule.Action));
                else
                    Log.Info("  [" + rule.Name + "] (disabled)");
            }
            Log.Info("input: XInput via " + XInput.DllName + " (slots g0..g3, standard mapping)");
            Log.Info("output: SendInput (scan codes) to the focused window" + (dryRun ? " — DRY RUN, nothing will be sent" : ""));
            Log.Info("stop: Ctrl+C, close this window, or hold Ctrl+Alt+F12");

            Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                stopRequested = true;
            };

            PollLoop(engine, dryRun);
            Log.Info("stopped");
            return 0;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Hands companion — plays a Hands profile on this PC");
            Console.WriteLine("usage: hands-companion.exe [profile.json] [--dry-run]");
            Console.WriteLine("       hands-companion.exe --selftest-send");
            Console.WriteLine("  profile.json     a profile exported from the Hands studio;");
            Console.WriteLine("                   defaults to profile.json next to this program");
            Console.WriteLine("  --dry-run        log what would be sent, send nothing");
            Console.WriteLine("  --selftest-send  send one harmless Shift tap and report whether");
            Console.WriteLine("                   SendInput accepted it, then exit");
        }

        private static int RunSelfTest()
        {
            var ops = Planner.Plan(new RuleAction { Kind = "keys", Keys = new List<string> { "Shift" } });
            var result = WindowsSender.Send(ops);
            if (result.Ok)
            {
                Log.Info("self-test OK: SendInput accepted a Shift tap (" + result.Sent + " events). Injection works from this process.");
                return 0;
            }
            Log.Error("self-test FAILED: " + result.Error);
            return 4;
        }

        /* The engine consumes the raw rule values from the file, exactly
           like HC.createEngine consumes the studio's rule objects. The
           profile was already validated by ImportProfile above. */
        private static List<object> ExtractRawRules(string jsonText)
        {
            var root = Json.Parse(jsonText) as Dictionary<string, object>;
            if (root == null) return new List<object>();
            object inner = root;
            if (root.ContainsKey("app") || root.ContainsKey("profile"))
            {
                object p;
                inner = root.TryGetValue("profile", out p) ? p : null;
            }
            var innerDict = inner as Dictionary<string, object>;
            if (innerDict == null) return new List<object>();
            object rules;
            if (!innerDict.TryGetValue("rules", out rules)) return new List<object>();
            return rules as List<object> ?? new List<object>();
        }

        private static void PollLoop(Engine engine, bool dryRun)
        {
            var clock = Stopwatch.StartNew();
            var prev = new bool[4][];
            var connected = new bool[4];
            for (int s = 0; s < 4; s++) { prev[s] = new bool[XInput.ButtonCount]; connected[s] = false; }
            var cur = new bool[XInput.ButtonCount];

            while (!stopRequested)
            {
                long iterStart = clock.ElapsedMilliseconds;
                long now = iterStart;
                for (int slot = 0; slot < 4; slot++)
                {
                    bool ok = XInput.TryGetPressed(slot, cur);
                    if (ok && !connected[slot])
                    {
                        connected[slot] = true;
                        Log.Info("controller connected: slot " + slot + " (buttons g" + slot + "b*)");
                    }
                    if (!ok && connected[slot])
                    {
                        connected[slot] = false;
                        Log.Info("controller disconnected: slot " + slot);
                        /* Buttons still marked down would stay down in the
                           engine forever (a chord could latch, a long press
                           could fire later). Release them synthetically. */
                        for (int i = 0; i < XInput.ButtonCount; i++)
                        {
                            if (prev[slot][i])
                            {
                                prev[slot][i] = false;
                                HandleFired(engine.HandleEvent("g" + slot + "b" + i, false, now), dryRun);
                            }
                        }
                        continue;
                    }
                    if (!ok) continue;
                    for (int i = 0; i < XInput.ButtonCount; i++)
                    {
                        if (cur[i] != prev[slot][i])
                        {
                            prev[slot][i] = cur[i];
                            HandleFired(engine.HandleEvent("g" + slot + "b" + i, cur[i], now), dryRun);
                        }
                    }
                }
                HandleFired(engine.AdvanceTo(now), dryRun);

                if ((GetAsyncKeyState(VkControl) & 0x8000) != 0
                    && (GetAsyncKeyState(VkMenu) & 0x8000) != 0
                    && (GetAsyncKeyState(VkF12) & 0x8000) != 0)
                {
                    Log.Info("panic chord Ctrl+Alt+F12 pressed; stopping");
                    break;
                }

                long spent = clock.ElapsedMilliseconds - iterStart;
                int sleep = (int)Math.Max(1, 4 - spent); /* ~250 Hz poll */
                Thread.Sleep(sleep);
            }
        }

        private static void HandleFired(List<FiredEntry> fired, bool dryRun)
        {
            foreach (var f in fired)
            {
                var ops = Planner.Plan(f.Action);
                Log.Info("FIRED [" + f.Name + "] " + f.TriggerKind + " -> " + ProfileIO.DescribeAction(f.Action));
                if (ops.Count == 0) continue;
                if (dryRun)
                {
                    Log.Info("  dry-run: would send " + DescribeOps(ops));
                    continue;
                }
                var result = WindowsSender.Send(ops);
                if (!result.Ok)
                    Log.Error("  injection problem: " + result.Error);
            }
        }

        private static string DescribeOps(List<SendOp> ops)
        {
            var parts = new List<string>();
            foreach (var op in ops)
            {
                string s = "";
                if (op.DelayBeforeMs > 0) s += "wait " + op.DelayBeforeMs + "ms, ";
                s += (op.IsUp ? "up " : "down ") + "0x" + op.Scan.ToString("X2") + (op.Extended ? " (E0)" : "");
                parts.Add(s);
            }
            return string.Join(", ", parts.ToArray());
        }
    }

    internal static class Log
    {
        private static StreamWriter file;
        private static readonly object Gate = new object();

        public static void Init(string path)
        {
            try
            {
                file = new StreamWriter(path, true, Encoding.UTF8);
                file.AutoFlush = true;
            }
            catch (Exception)
            {
                file = null; /* console-only logging; startup continues */
            }
        }

        public static void Info(string msg) { Write("INFO", msg); }
        public static void Error(string msg) { Write("ERROR", msg); }

        private static void Write(string level, string msg)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + msg;
            lock (Gate)
            {
                Console.WriteLine(line);
                if (file != null) file.WriteLine(line);
            }
        }
    }
}
