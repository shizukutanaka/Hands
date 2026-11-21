using System;
using System.Collections.Generic;
using System.IO;
using HandsCompanion;

/* Companion logic tests: plain console harness, zero dependencies, in
   the same style as tests/run-tests.js for the web core.
   Covers: the JSON parser, profile normalization (mirroring the core
   tests), the key map, the send planner, parity with the JavaScript
   engine via generated vectors, and an end-to-end pass over the shipped
   sample profile (profile -> input sequence -> exact key operations).
   Run: dotnet run --project companion/tests/logic -- \
          companion/tests/vectors.json companion/sample-profile.json */
internal static class Tests
{
    private static int passed = 0, failed = 0;

    private static void Ok(bool cond, string name)
    {
        if (cond) passed++;
        else { failed++; Console.WriteLine("FAIL: " + name); }
    }

    private static void Eq(object a, object b, string name)
    {
        string ca = Json.Canon(a), cb = Json.Canon(b);
        Ok(ca == cb, name + " (got " + ca + ", want " + cb + ")");
    }

    private static void EqStr(string a, string b, string name)
    {
        Ok(a == b, name + " (got \"" + a + "\", want \"" + b + "\")");
    }

    private static Dictionary<string, object> Dict(params object[] kv)
    {
        var d = new Dictionary<string, object>();
        for (int i = 0; i < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
        return d;
    }

    private static List<object> List(params object[] items)
    {
        return new List<object>(items);
    }

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("usage: logic-tests <vectors.json> <sample-profile.json>");
            return 1;
        }
        TestJson();
        TestNormalization();
        TestKeyMap();
        TestPlanner();
        TestVectors(args[0]);
        TestSampleProfile(args[1]);
        TestLoaderRefusals();
        TestTargetFilter();
        Console.WriteLine("companion logic tests: " + passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }

    /* ---------------- JSON parser ---------------- */

    private static void TestJson()
    {
        var v = Json.Parse("{\"a\": [1, 2.5, \"x\\n\\u0041\", true, null], \"b\": {}}");
        var d = (Dictionary<string, object>)v;
        var a = (List<object>)d["a"];
        Ok((double)a[0] == 1.0 && (double)a[1] == 2.5, "json: numbers");
        EqStr((string)a[2], "x\nA", "json: escapes and \\u");
        Ok((bool)a[3] && a[4] == null, "json: bool and null");
        Ok(((Dictionary<string, object>)d["b"]).Count == 0, "json: empty object");
        Ok(Throws("{"), "json: unterminated object rejected");
        Ok(Throws("[1,]"), "json: trailing comma rejected");
        Ok(Throws("{\"a\":}"), "json: missing value rejected");
        Ok(Throws("nul"), "json: bad literal rejected");
        Ok(Throws("1 2"), "json: trailing characters rejected");
        EqStr(Json.Canon(Json.Parse("{\"b\":1,\"a\":[true,null]}")), "{\"a\":[true,null],\"b\":1}", "json: canon sorts keys");
    }

    private static bool Throws(string text)
    {
        try { Json.Parse(text); return false; }
        catch (FormatException) { return true; }
    }

    /* ---------------- normalization (mirrors the core tests) ---------------- */

    private static Dictionary<string, object> KeysAction()
    {
        return Dict("kind", "keys", "keys", List("Ctrl", "C"));
    }

    private static Dictionary<string, object> RawRule(string kind, List<object> buttons, Dictionary<string, object> extra, object action)
    {
        var t = Dict("kind", kind, "buttons", buttons);
        if (extra != null) foreach (var kv in extra) t[kv.Key] = kv.Value;
        return Dict("id", "r-" + kind, "name", kind, "enabled", true, "trigger", t, "action", action ?? KeysAction());
    }

    private static void TestNormalization()
    {
        var errs = new List<string>();
        Ok(ProfileIO.NormalizeRule(RawRule("chord", List("A"), null, null), errs) == null, "norm: chord with 1 button rejected");
        Ok(errs.Count >= 1, "norm: rejection carries an error message");
        Ok(ProfileIO.NormalizeRule(RawRule("press", List("A", "B"), null, null), new List<string>()) == null, "norm: press with 2 buttons rejected");
        Ok(ProfileIO.NormalizeRule(RawRule("nope", List("A"), null, null), new List<string>()) == null, "norm: unknown kind rejected");
        Ok(ProfileIO.NormalizeRule(RawRule("longpress", List("A"), Dict("holdMs", 5.0), null), new List<string>()) == null, "norm: holdMs below range rejected");
        Ok(ProfileIO.NormalizeRule(RawRule("press", List("A"), null, Dict("kind", "keys", "keys", List())), new List<string>()) == null, "norm: empty action keys rejected");
        Ok(ProfileIO.NormalizeRule(RawRule("press", List("A"), null,
            Dict("kind", "macro", "steps", List(Dict("keys", List(), "delayMs", 10.0)))), new List<string>()) == null,
            "norm: macro step without keys rejected");
        var dbl = ProfileIO.NormalizeRule(RawRule("double", List("A"), null, null), new List<string>());
        Ok(dbl != null && dbl.Trigger.IntervalMs == 350, "norm: double interval default applied");
        var c0 = ProfileIO.NormalizeRule(RawRule("chord", List("A", "B"), Dict("windowMs", 0.0), null), new List<string>());
        Ok(c0 != null && c0.Trigger.WindowMs == 0, "norm: chord windowMs 0 preserved (modifier style)");
        var cd = ProfileIO.NormalizeRule(RawRule("chord", List("A", "A", "B"), null, null), new List<string>());
        EqStr(string.Join(",", cd.Trigger.Buttons.ToArray()), "A,B", "norm: chord buttons deduped");
        var sq = ProfileIO.NormalizeRule(RawRule("sequence", List("A", "A", "B"), null, null), new List<string>());
        EqStr(string.Join(",", sq.Trigger.Buttons.ToArray()), "A,A,B", "norm: sequence keeps repeated buttons");
        var mc = ProfileIO.NormalizeRule(RawRule("press", List("A"), null,
            Dict("kind", "macro", "steps", List(Dict("keys", List("Ctrl", "S"))))), new List<string>());
        Ok(mc != null && mc.Action.Steps[0].DelayMs == 50, "norm: macro step delay default applied");
        var en = ProfileIO.NormalizeRule(RawRule("press", List("A"), null, Dict("kind", "none")), new List<string>());
        Ok(en != null && en.Action.Kind == "none" && en.Enabled, "norm: none action, enabled defaults true");
        var off = ProfileIO.NormalizeRule(
            Dict("id", "x", "enabled", false, "trigger", Dict("kind", "press", "buttons", List("A")), "action", KeysAction()),
            new List<string>());
        Ok(off != null && !off.Enabled, "norm: enabled:false honored");

        /* profile + envelope level */
        var prof = Dict("id", "p1", "name", "Test", "rules", List(
            RawRule("press", List("A"), null, null), Dict("bogus", 1.0),
            RawRule("chord", List("A", "B"), Dict("windowMs", 0.0), null)));
        var n = ProfileIO.NormalizeProfile(prof);
        Ok(n.Profile.Rules.Count == 2, "profile: invalid rules dropped, valid kept");
        Ok(n.Errors.Count == 1, "profile: one error for the dropped rule");
        Ok(ProfileIO.ImportProfile("not json {").Profile == null, "json: garbage rejected");
        Ok(ProfileIO.ImportProfile("{\"app\":\"other\",\"version\":1,\"profile\":{\"rules\":[]}}").Profile == null, "json: foreign app rejected");
        Ok(ProfileIO.ImportProfile("{\"app\":\"hands\",\"version\":99,\"profile\":{\"rules\":[]}}").Profile == null, "json: newer version rejected");
        var bare = ProfileIO.ImportProfile("{\"name\":\"Bare\",\"rules\":[]}");
        Ok(bare.Profile != null && bare.Profile.Name == "Bare", "json: bare profile object accepted");
        var env = ProfileIO.ImportProfile("{\"app\":\"hands\",\"version\":1,\"profile\":{\"name\":\"Env\",\"rules\":[]}}");
        Ok(env.Profile != null && env.Profile.Name == "Env" && env.Errors.Count == 0, "json: envelope accepted");
    }

    /* ---------------- key map ---------------- */

    private static void ExpectScan(string token, int scan, bool ext)
    {
        KeyStroke s;
        Ok(KeyMap.TryMap(token, out s) && s.Scan == scan && s.Extended == ext,
            "keymap: " + token + " -> 0x" + scan.ToString("X2") + (ext ? " E0" : ""));
    }

    private static void TestKeyMap()
    {
        ExpectScan("Ctrl", 0x1D, false);
        ExpectScan("Shift", 0x2A, false);
        ExpectScan("Alt", 0x38, false);
        ExpectScan("Meta", 0x5B, true);
        ExpectScan("Enter", 0x1C, false);
        ExpectScan("Esc", 0x01, false);
        ExpectScan("Escape", 0x01, false);
        ExpectScan("Space", 0x39, false);
        ExpectScan("Tab", 0x0F, false);
        ExpectScan("Backspace", 0x0E, false);
        ExpectScan("A", 0x1E, false);
        ExpectScan("Z", 0x2C, false);
        ExpectScan("a", 0x1E, false); /* hand-edited lowercase */
        ExpectScan("0", 0x0B, false);
        ExpectScan("F1", 0x3B, false);
        ExpectScan("F5", 0x3F, false);
        ExpectScan("F12", 0x58, false);
        ExpectScan("ArrowUp", 0x48, true);
        ExpectScan("ArrowDown", 0x50, true);
        ExpectScan("Delete", 0x53, true);
        ExpectScan("Home", 0x47, true);
        ExpectScan("PageDown", 0x51, true);
        ExpectScan(";", 0x27, false);
        ExpectScan("!", 0x02, false); /* shifted punctuation -> base key */
        ExpectScan("?", 0x35, false);
        KeyStroke s;
        Ok(!KeyMap.TryMap("Banana", out s), "keymap: unknown token has no mapping");
        Ok(!KeyMap.TryMap("PrintScreen", out s), "keymap: PrintScreen deliberately unmapped");
        Ok(!KeyMap.TryMap("F13", out s), "keymap: F13 unmapped");
    }

    /* ---------------- planner ---------------- */

    private static string OpsCanon(List<SendOp> ops)
    {
        var parts = new List<string>();
        foreach (var op in ops)
            parts.Add((op.DelayBeforeMs > 0 ? "w" + op.DelayBeforeMs + " " : "") +
                      (op.IsUp ? "up " : "dn ") + "0x" + op.Scan.ToString("X2") + (op.Extended ? "e" : ""));
        return string.Join(", ", parts.ToArray());
    }

    private static void TestPlanner()
    {
        var keys = new RuleAction { Kind = "keys", Keys = new List<string> { "Ctrl", "Shift", "P" } };
        EqStr(OpsCanon(Planner.Plan(keys)), "dn 0x1D, dn 0x2A, dn 0x19, up 0x19, up 0x2A, up 0x1D",
            "planner: keys tap downs in order, ups in reverse");
        var macro = new RuleAction
        {
            Kind = "macro",
            Steps = new List<MacroStep>
            {
                new MacroStep { Keys = new List<string> { "Ctrl", "S" }, DelayMs = 50 },
                new MacroStep { Keys = new List<string> { "Enter" }, DelayMs = 150 }
            }
        };
        EqStr(OpsCanon(Planner.Plan(macro)), "w50 dn 0x1D, dn 0x1F, up 0x1F, up 0x1D, w150 dn 0x1C, up 0x1C",
            "planner: macro waits each step delay before the step (studio cumulative semantics)");
        Ok(Planner.Plan(new RuleAction { Kind = "none" }).Count == 0, "planner: none action sends nothing");
    }

    /* ---------------- parity vectors vs the JavaScript engine ---------------- */

    private static void TestVectors(string path)
    {
        var root = (Dictionary<string, object>)Json.Parse(File.ReadAllText(path));
        var scenarios = (List<object>)root["scenarios"];
        foreach (var scObj in scenarios)
        {
            var sc = (Dictionary<string, object>)scObj;
            string name = (string)sc["name"];
            var rules = (List<object>)sc["rules"];
            var engine = new Engine(rules);
            Ok(engine.Errors.Count == (int)(double)sc["expectErrors"], "vectors/" + name + ": engine error count");
            var fired = new List<FiredEntry>();
            foreach (var stObj in (List<object>)sc["steps"])
            {
                var st = (List<object>)stObj;
                string kind = (string)st[0];
                if (kind == "advance")
                    fired.AddRange(engine.AdvanceTo((long)(double)st[1]));
                else
                    fired.AddRange(engine.HandleEvent((string)st[1], kind == "down", (long)(double)st[2]));
            }
            var got = new List<object>();
            foreach (var f in fired)
            {
                got.Add(Dict("ruleId", f.RuleId, "triggerKind", f.TriggerKind,
                             "time", (double)f.Time, "action", ProfileIO.ActionToRaw(f.Action)));
            }
            Eq(got, sc["expectFired"], "vectors/" + name + ": fired entries identical to the JS engine");
        }
    }

    /* ---------------- sample profile end-to-end ---------------- */

    private static void TestSampleProfile(string path)
    {
        string text = File.ReadAllText(path);
        var loaded = ProfileIO.ImportProfile(text);
        Ok(loaded.Profile != null && loaded.Errors.Count == 0, "sample: loads with no errors");
        Ok(KeyMap.ValidateProfileKeys(loaded.Profile).Count == 0, "sample: every key token is sendable");

        var root = (Dictionary<string, object>)Json.Parse(text);
        var inner = (Dictionary<string, object>)root["profile"];
        var engine = new Engine((List<object>)inner["rules"]);
        Ok(engine.RuleCount == 5, "sample: 5 active rules");

        var fired = new List<FiredEntry>();
        fired.AddRange(engine.HandleEvent("g0b0", true, 0));            /* press A */
        fired.AddRange(engine.HandleEvent("g0b0", false, 50));
        fired.AddRange(engine.HandleEvent("g0b4", true, 100));          /* LB held... */
        fired.AddRange(engine.HandleEvent("g0b5", true, 900));          /* ...RB tapped: modifier chord */
        fired.AddRange(engine.HandleEvent("g0b5", false, 950));
        fired.AddRange(engine.HandleEvent("g0b4", false, 960));
        fired.AddRange(engine.HandleEvent("g0b1", true, 1000));         /* hold B */
        fired.AddRange(engine.AdvanceTo(1599));
        Ok(fired.Count == 2, "sample: longpress has not fired at 599ms");
        fired.AddRange(engine.AdvanceTo(1600));                          /* fires at 600ms */
        fired.AddRange(engine.HandleEvent("g0b1", false, 1700));
        fired.AddRange(engine.HandleEvent("g0b2", true, 2000));         /* double X */
        fired.AddRange(engine.HandleEvent("g0b2", false, 2050));
        fired.AddRange(engine.HandleEvent("g0b2", true, 2300));
        fired.AddRange(engine.HandleEvent("g0b2", false, 2350));
        fired.AddRange(engine.HandleEvent("g0b12", true, 3000));        /* sequence Up, Down */
        fired.AddRange(engine.HandleEvent("g0b13", true, 3500));

        Ok(fired.Count == 5, "sample: all five bindings fired (got " + fired.Count + ")");
        if (fired.Count == 5)
        {
            EqStr(ProfileIO.DescribeAction(fired[0].Action), "Space", "sample: A -> Space");
            EqStr(OpsCanon(Planner.Plan(fired[0].Action)), "dn 0x39, up 0x39", "sample: Space ops");
            EqStr(ProfileIO.DescribeAction(fired[1].Action), "Esc", "sample: LB+RB -> Esc");
            EqStr(OpsCanon(Planner.Plan(fired[2].Action)), "dn 0x1C, up 0x1C", "sample: hold B -> Enter ops");
            EqStr(OpsCanon(Planner.Plan(fired[3].Action)), "dn 0x1D, dn 0x2F, up 0x2F, up 0x1D", "sample: double X -> Ctrl+V ops");
            EqStr(OpsCanon(Planner.Plan(fired[4].Action)),
                "w50 dn 0x1D, dn 0x1F, up 0x1F, up 0x1D, w150 dn 0x1C, up 0x1C", "sample: sequence -> save macro ops");
        }
    }

    /* ---------------- target filter (profile target + send decision) ---------------- */

    private static void TestTargetFilter()
    {
        /* normalization: absent = no filter (back compat with old profiles) */
        var old = ProfileIO.ImportProfile("{\"app\":\"hands\",\"version\":1,\"profile\":{\"name\":\"Old\",\"rules\":[]}}");
        Ok(old.Profile != null && old.Profile.Target == null && old.Errors.Count == 0,
            "target: profile without target loads with no filter");
        var bareOld = ProfileIO.ImportProfile("{\"name\":\"Bare\",\"rules\":[]}");
        Ok(bareOld.Profile != null && bareOld.Profile.Target == null, "target: bare old profile has no filter");

        /* normalization: values are cleaned like the studio cleans them */
        var withT = ProfileIO.ImportProfile(
            "{\"app\":\"hands\",\"version\":1,\"profile\":{\"name\":\"T\",\"rules\":[]," +
            "\"target\":{\"titleContains\":\"  Notepad  \",\"processName\":\"notepad.exe\"}}}");
        Ok(withT.Profile != null && withT.Errors.Count == 0, "target: valid target loads");
        Ok(withT.Profile.Target != null && withT.Profile.Target.TitleContains == "Notepad",
            "target: titleContains trimmed");
        Ok(withT.Profile.Target != null && withT.Profile.Target.ProcessName == "notepad",
            "target: processName .exe stripped");
        var titleOnly = ProfileIO.ImportProfile("{\"profile\":{\"rules\":[],\"target\":{\"titleContains\":\"Game\"}},\"app\":\"hands\",\"version\":1}");
        Ok(titleOnly.Profile.Target != null && titleOnly.Profile.Target.ProcessName == null, "target: title-only accepted");

        /* normalization: invalid targets are load errors (companion refuses to start) */
        Ok(ProfileIO.ImportProfile("{\"app\":\"hands\",\"version\":1,\"profile\":{\"rules\":[],\"target\":{}}}").Errors.Count >= 1,
            "target: empty target object rejected");
        Ok(ProfileIO.ImportProfile("{\"app\":\"hands\",\"version\":1,\"profile\":{\"rules\":[],\"target\":{\"titleContains\":\"\"}}}").Errors.Count >= 1,
            "target: empty titleContains rejected");
        Ok(ProfileIO.ImportProfile("{\"app\":\"hands\",\"version\":1,\"profile\":{\"rules\":[],\"target\":{\"processName\":\"C:\\\\apps\\\\notepad\"}}}").Errors.Count >= 1,
            "target: process path rejected");
        Ok(ProfileIO.ImportProfile("{\"app\":\"hands\",\"version\":1,\"profile\":{\"rules\":[],\"target\":{\"processName\":\"note*\"}}}").Errors.Count >= 1,
            "target: wildcard process rejected");
        Ok(ProfileIO.ImportProfile("{\"app\":\"hands\",\"version\":1,\"profile\":{\"rules\":[],\"target\":\"notepad\"}}").Errors.Count >= 1,
            "target: non-object target rejected");

        /* decision: the three required cases */
        var target = new ProfileTarget { TitleContains = "Notepad", ProcessName = "notepad" };
        Ok(TargetFilter.Matches(target, "Untitled - Notepad", "notepad.exe"),
            "target decision: match (title substring + process, .exe on foreground side) -> send");
        Ok(!TargetFilter.Matches(target, "Untitled - Notepad", "code.exe"),
            "target decision: title matches but process differs -> do not send");
        Ok(!TargetFilter.Matches(target, "Calculator", "notepad.exe"),
            "target decision: process matches but title differs -> do not send (AND)");
        Ok(TargetFilter.Matches(null, "Anything at all", "whatever.exe"),
            "target decision: no target specified -> send (historical behaviour)");
        Ok(TargetFilter.Matches(null, "", ""), "target decision: no target, no foreground info -> send");

        /* decision: single-field and case handling */
        Ok(TargetFilter.Matches(new ProfileTarget { TitleContains = "note" }, "My NOTEbook", "anything.exe"),
            "target decision: title match is case-insensitive substring");
        Ok(TargetFilter.Matches(new ProfileTarget { ProcessName = "NotePad" }, "any title", "NOTEPAD.EXE"),
            "target decision: process match is case-insensitive and .exe-insensitive");
        Ok(!TargetFilter.Matches(new ProfileTarget { TitleContains = "Notepad" }, "", "notepad.exe"),
            "target decision: empty foreground title cannot satisfy a title filter");
        Ok(!TargetFilter.Matches(new ProfileTarget { ProcessName = "notepad" }, "Notepad", ""),
            "target decision: unknown foreground process cannot satisfy a process filter");

        /* a skipped send carries a reason that names both sides */
        string reason = TargetFilter.MismatchReason(target, "Calculator", "calc.exe");
        Ok(reason.IndexOf("Calculator") >= 0 && reason.IndexOf("Notepad") >= 0 && reason.IndexOf("not sending") >= 0,
            "target decision: mismatch reason names the foreground window and the target");
        Ok(TargetFilter.Describe(target).IndexOf("Notepad") >= 0, "target: describe names the target");
    }

    /* ---------------- loader refusals (fail loudly, never partially) ---------------- */

    private static void TestLoaderRefusals()
    {
        var badKey = ProfileIO.ImportProfile(
            "{\"app\":\"hands\",\"version\":1,\"profile\":{\"name\":\"Bad\",\"rules\":[" +
            "{\"id\":\"r1\",\"trigger\":{\"kind\":\"press\",\"buttons\":[\"g0b0\"]},\"action\":{\"kind\":\"keys\",\"keys\":[\"Banana\"]}}]}}");
        Ok(badKey.Profile != null && badKey.Errors.Count == 0, "refusal: structurally valid profile loads");
        Ok(KeyMap.ValidateProfileKeys(badKey.Profile).Count == 1, "refusal: unmappable key is a load-time error");
        var broken = ProfileIO.ImportProfile(
            "{\"app\":\"hands\",\"version\":1,\"profile\":{\"rules\":[{\"trigger\":{\"kind\":\"chord\",\"buttons\":[\"g0b0\"]},\"action\":{\"kind\":\"keys\",\"keys\":[\"A\"]}}]}}");
        Ok(broken.Errors.Count == 1, "refusal: invalid rule produces a load error (companion refuses to start)");
    }
}
