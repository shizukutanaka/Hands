using System;
using System.Collections.Generic;

namespace HandsCompanion
{
    /* Profile model + normalization: a faithful port of the HANDS CORE
       functions normalizeProfile / normalizeRule / normalizeAction /
       importProfile in index.html. The studio is the authoring tool; this
       code only validates and cleans what the studio exported, with the
       same rules and the same error conditions, because the design memo
       requires the shell to reject anything the core would not accept.
       Raw JSON values are the shapes produced by Json.Parse:
       Dictionary<string,object> / List<object> / string / double / bool /
       null. */

    public sealed class MacroStep
    {
        public List<string> Keys;
        public long DelayMs;
    }

    public sealed class RuleAction
    {
        public string Kind;               /* keys | macro | none */
        public List<string> Keys;         /* kind == keys */
        public List<MacroStep> Steps;     /* kind == macro */
    }

    public sealed class Trigger
    {
        public string Kind;               /* press | chord | longpress | double | sequence */
        public List<string> Buttons;
        public long WindowMs;             /* chord, sequence */
        public long HoldMs;               /* longpress */
        public long IntervalMs;           /* double */
    }

    public sealed class Rule
    {
        public string Id;
        public string Name;
        public bool Enabled;
        public Trigger Trigger;
        public RuleAction Action;
    }

    public sealed class Profile
    {
        public string Id;
        public string Name;
        public List<Rule> Rules;
    }

    public sealed class ProfileResult
    {
        public Profile Profile;           /* null when the input is not an object at all */
        public List<string> Errors;
    }

    public static class ProfileIO
    {
        public const int FormatVersion = 1;

        private static readonly string[] TriggerKinds = { "press", "chord", "longpress", "double", "sequence" };
        private static readonly string[] ActionKinds = { "keys", "macro", "none" };

        private const long TimingMin = 10, TimingMax = 60000;
        private const long DefChordWindowMs = 100, DefHoldMs = 500, DefDoubleIntervalMs = 350,
                           DefSequenceWindowMs = 800, DefMacroDelayMs = 50;

        private static int idCounter = 0;
        private static string GenId(string prefix)
        {
            idCounter += 1;
            return prefix + "-auto-" + idCounter;
        }

        /* JS Math.round: halves round toward +infinity (C#'s Math.Round
           would do banker's rounding, so it must not be used here). */
        public static long JsRound(double v) { return (long)Math.Floor(v + 0.5); }

        private static bool IsStr(object v) { return v is string && ((string)v).Length > 0; }

        private static Dictionary<string, object> AsDict(object v)
        {
            return v as Dictionary<string, object>;
        }

        private static object Get(Dictionary<string, object> d, string key)
        {
            if (d == null) return null;
            object v;
            return d.TryGetValue(key, out v) ? v : null;
        }

        private static List<string> CleanTokens(object listObj, bool dedupe, int maxLen)
        {
            var list = listObj as List<object>;
            if (list == null || list.Count == 0) return null;
            if (list.Count > maxLen) return null;
            var outList = new List<string>();
            var seen = new HashSet<string>();
            for (int i = 0; i < list.Count; i++)
            {
                if (!IsStr(list[i])) return null;
                string v = (string)list[i];
                if (dedupe)
                {
                    if (seen.Contains(v)) continue;
                    seen.Add(v);
                }
                outList.Add(v);
            }
            return outList.Count > 0 ? outList : null;
        }

        private static long TimingField(object value, long def, List<string> errors, string field)
        {
            if (value == null) return def;
            if (!(value is double) || double.IsNaN((double)value) || double.IsInfinity((double)value)
                || (double)value < TimingMin || (double)value > TimingMax)
            {
                errors.Add(field + ": must be a number between " + TimingMin + " and " + TimingMax + " (ms)");
                return def;
            }
            return JsRound((double)value);
        }

        public static RuleAction NormalizeAction(object rawObj, List<string> errors)
        {
            var raw = AsDict(rawObj);
            if (raw == null) { errors.Add("action: missing"); return null; }
            string kind = Get(raw, "kind") as string;
            if (kind == null || Array.IndexOf(ActionKinds, kind) < 0)
            {
                errors.Add("action.kind: unknown \"" + (kind ?? "") + "\"");
                return null;
            }
            if (kind == "none") return new RuleAction { Kind = "none" };
            if (kind == "keys")
            {
                var keys = CleanTokens(Get(raw, "keys"), true, 8);
                if (keys == null) { errors.Add("action.keys: must be 1..8 key names"); return null; }
                return new RuleAction { Kind = "keys", Keys = keys };
            }
            /* macro */
            var rawSteps = Get(raw, "steps") as List<object>;
            if (rawSteps == null || rawSteps.Count == 0 || rawSteps.Count > 64)
            {
                errors.Add("action.steps: must be 1..64 steps");
                return null;
            }
            var steps = new List<MacroStep>();
            for (int i = 0; i < rawSteps.Count; i++)
            {
                var s = AsDict(rawSteps[i]);
                var sk = CleanTokens(s == null ? null : Get(s, "keys"), true, 8);
                if (sk == null) { errors.Add("action.steps[" + i + "].keys: must be 1..8 key names"); return null; }
                object dObj = s == null ? null : Get(s, "delayMs");
                double d = DefMacroDelayMs;
                if (dObj != null)
                {
                    if (!(dObj is double) || double.IsNaN((double)dObj) || double.IsInfinity((double)dObj)
                        || (double)dObj < 0 || (double)dObj > TimingMax)
                    {
                        errors.Add("action.steps[" + i + "].delayMs: must be 0.." + TimingMax);
                        return null;
                    }
                    d = (double)dObj;
                }
                steps.Add(new MacroStep { Keys = sk, DelayMs = JsRound(d) });
            }
            return new RuleAction { Kind = "macro", Steps = steps };
        }

        public static Rule NormalizeRule(object rawObj, List<string> errors)
        {
            var raw = AsDict(rawObj);
            if (raw == null) { errors.Add("rule: not an object"); return null; }
            var t = AsDict(Get(raw, "trigger"));
            if (t == null) t = new Dictionary<string, object>();
            string kind = Get(t, "kind") as string;
            if (kind == null || Array.IndexOf(TriggerKinds, kind) < 0)
            {
                errors.Add("trigger.kind: unknown \"" + (kind ?? "") + "\"");
                return null;
            }
            int before = errors.Count;
            var trigger = new Trigger { Kind = kind };
            if (kind == "press" || kind == "double")
            {
                var one = CleanTokens(Get(t, "buttons"), true, 4);
                if (one == null || one.Count != 1)
                {
                    errors.Add("trigger.buttons: " + kind + " needs exactly 1 button");
                    return null;
                }
                trigger.Buttons = one;
                if (kind == "double")
                    trigger.IntervalMs = TimingField(Get(t, "intervalMs"), DefDoubleIntervalMs, errors, "trigger.intervalMs");
            }
            else if (kind == "chord")
            {
                var set = CleanTokens(Get(t, "buttons"), true, 10);
                if (set == null || set.Count < 2)
                {
                    errors.Add("trigger.buttons: chord needs at least 2 buttons");
                    return null;
                }
                trigger.Buttons = set;
                object w = Get(t, "windowMs");
                if (w == null) trigger.WindowMs = DefChordWindowMs;
                else if (!(w is double) || double.IsNaN((double)w) || double.IsInfinity((double)w)
                         || (double)w < 0 || (double)w > TimingMax)
                {
                    errors.Add("trigger.windowMs: must be 0.." + TimingMax);
                    return null;
                }
                else trigger.WindowMs = JsRound((double)w);
            }
            else if (kind == "longpress")
            {
                var held = CleanTokens(Get(t, "buttons"), true, 10);
                if (held == null)
                {
                    errors.Add("trigger.buttons: longpress needs at least 1 button");
                    return null;
                }
                trigger.Buttons = held;
                trigger.HoldMs = TimingField(Get(t, "holdMs"), DefHoldMs, errors, "trigger.holdMs");
            }
            else if (kind == "sequence")
            {
                var seq = CleanTokens(Get(t, "buttons"), false, 16);
                if (seq == null || seq.Count < 2)
                {
                    errors.Add("trigger.buttons: sequence needs at least 2 buttons");
                    return null;
                }
                trigger.Buttons = seq;
                trigger.WindowMs = TimingField(Get(t, "windowMs"), DefSequenceWindowMs, errors, "trigger.windowMs");
            }
            if (errors.Count > before) return null;
            var action = NormalizeAction(Get(raw, "action"), errors);
            if (action == null) return null;
            object enabledRaw = Get(raw, "enabled");
            return new Rule
            {
                Id = IsStr(Get(raw, "id")) ? (string)Get(raw, "id") : GenId("rule"),
                Name = Get(raw, "name") is string ? (string)Get(raw, "name") : "",
                Enabled = !(enabledRaw is bool && (bool)enabledRaw == false),
                Trigger = trigger,
                Action = action
            };
        }

        public static ProfileResult NormalizeProfile(object rawObj)
        {
            var errors = new List<string>();
            var raw = AsDict(rawObj);
            if (raw == null)
            {
                errors.Add("profile: not an object");
                return new ProfileResult { Profile = null, Errors = errors };
            }
            var rules = new List<Rule>();
            var rawRules = Get(raw, "rules") as List<object>;
            if (rawRules == null) rawRules = new List<object>();
            for (int i = 0; i < rawRules.Count; i++)
            {
                var rErrors = new List<string>();
                var r = NormalizeRule(rawRules[i], rErrors);
                if (r != null) rules.Add(r);
                else errors.Add("rules[" + i + "]: " + string.Join("; ", rErrors.ToArray()));
            }
            object nameRaw = Get(raw, "name");
            return new ProfileResult
            {
                Profile = new Profile
                {
                    Id = IsStr(Get(raw, "id")) ? (string)Get(raw, "id") : GenId("profile"),
                    Name = (nameRaw is string && ((string)nameRaw).Length > 0) ? (string)nameRaw : "Profile",
                    Rules = rules
                },
                Errors = errors
            };
        }

        /* Port of HC.importProfile: accepts the exported envelope
           {"app":"hands","version":1,"profile":{...}} or a bare profile. */
        public static ProfileResult ImportProfile(string jsonText)
        {
            object data;
            try { data = Json.Parse(jsonText); }
            catch (Exception e) { return new ProfileResult { Profile = null, Errors = new List<string> { "json: " + e.Message } }; }
            var dict = AsDict(data);
            if (dict == null)
                return new ProfileResult { Profile = null, Errors = new List<string> { "json: not an object" } };
            object inner = dict;
            if (dict.ContainsKey("app") || dict.ContainsKey("profile"))
            {
                object app = Get(dict, "app");
                if (!(app is string) || (string)app != "hands")
                    return new ProfileResult { Profile = null, Errors = new List<string> { "app: not a Hands profile file" } };
                object ver = Get(dict, "version");
                if (ver is double && (double)ver > FormatVersion)
                    return new ProfileResult { Profile = null, Errors = new List<string> { "version: file version " + (double)ver + " is newer than supported " + FormatVersion } };
                inner = Get(dict, "profile");
            }
            return NormalizeProfile(inner);
        }

        /* ---- human-readable one-liners (ports of HC.describe*) ---- */

        public static string DescribeTrigger(Trigger trigger)
        {
            if (trigger == null) return "";
            string b = string.Join(" + ", trigger.Buttons.ToArray());
            switch (trigger.Kind)
            {
                case "press": return "press " + b;
                case "chord": return "chord " + b + (trigger.WindowMs != 0 ? " within " + trigger.WindowMs + "ms" : " (any timing)");
                case "longpress": return "hold " + b + " for " + trigger.HoldMs + "ms";
                case "double": return "double-press " + b + " within " + trigger.IntervalMs + "ms";
                case "sequence": return "sequence " + string.Join(" -> ", trigger.Buttons.ToArray()) + " (" + trigger.WindowMs + "ms/step)";
                default: return trigger.Kind;
            }
        }

        public static string DescribeAction(RuleAction action)
        {
            if (action == null) return "";
            if (action.Kind == "keys") return string.Join("+", action.Keys.ToArray());
            if (action.Kind == "macro")
            {
                var parts = new List<string>();
                foreach (var s in action.Steps) parts.Add(string.Join("+", s.Keys.ToArray()));
                return "macro[" + string.Join(", ", parts.ToArray()) + "]";
            }
            return "(no action)";
        }

        /* Structural view of an action, for structural comparisons. */
        public static object ActionToRaw(RuleAction action)
        {
            var d = new Dictionary<string, object>();
            d["kind"] = action.Kind;
            if (action.Kind == "keys")
            {
                var ks = new List<object>();
                foreach (var k in action.Keys) ks.Add(k);
                d["keys"] = ks;
            }
            else if (action.Kind == "macro")
            {
                var ss = new List<object>();
                foreach (var s in action.Steps)
                {
                    var sd = new Dictionary<string, object>();
                    var ks = new List<object>();
                    foreach (var k in s.Keys) ks.Add(k);
                    sd["keys"] = ks;
                    sd["delayMs"] = (double)s.DelayMs;
                    ss.Add(sd);
                }
                d["steps"] = ss;
            }
            return d;
        }
    }
}
