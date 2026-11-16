using System.Collections.Generic;

namespace HandsCompanion
{
    public sealed class FiredEntry
    {
        public string RuleId;
        public string Name;
        public string TriggerKind;
        public RuleAction Action;
        public long Time;
    }

    /* Faithful port of HC.createEngine in index.html (HANDS CORE).
       Same semantics, all boundaries inclusive:
       - press:     fires on a button's down edge.
       - chord:     fires when the last member goes down and every member is
                    down; windowMs > 0 bounds the spread between earliest and
                    latest member down-times; windowMs = 0 is modifier style.
                    Latches until a member is released.
       - longpress: fires once when every member has been continuously down
                    for holdMs, measured from when the set completed. A
                    release exactly at the threshold still counts, because
                    the check runs before the event is applied.
       - double:    fires on the second down edge within intervalMs
                    (down-to-down). After firing, the next down starts a
                    new pair.
       - sequence:  members down in order, each step within windowMs of the
                    previous matched step. A wrong button resets progress,
                    except pressing the first member restarts the attempt.
                    Partial progress expires after windowMs.
       Rules are independent; fired entries come back in rule definition
       order. Duplicate downs and stray ups are ignored. Times are integer
       milliseconds here (the core accepts any number; the companion's
       clock is a monotonic millisecond stopwatch) and are clamped to be
       non-decreasing, exactly like the core.
       Parity with the JavaScript engine is proven by the generated test
       vectors in companion/tests/vectors.json, replayed by the tests. */
    public sealed class Engine
    {
        private sealed class BtnState { public bool Down; public long DownTime; }
        private sealed class RuleState
        {
            public bool Latched;          /* chord */
            public bool Fired;            /* longpress */
            public bool HasLastDown;      /* double */
            public long LastDown;         /* double */
            public int Progress;          /* sequence */
            public long LastTime;         /* sequence */
        }

        private readonly List<Rule> rules = new List<Rule>();
        private readonly Dictionary<string, BtnState> btn = new Dictionary<string, BtnState>();
        private readonly Dictionary<string, RuleState> rs = new Dictionary<string, RuleState>();
        private long now = 0;

        public List<string> Errors { get; private set; }
        public int RuleCount { get { return rules.Count; } }

        /* Takes raw rule values (the shapes Json.Parse produces), exactly
           like HC.createEngine: each rule is normalized, invalid ones are
           reported in Errors and skipped, disabled ones never fire. */
        public Engine(List<object> rawRules)
        {
            Errors = new List<string>();
            var list = rawRules ?? new List<object>();
            for (int i = 0; i < list.Count; i++)
            {
                var e = new List<string>();
                var r = ProfileIO.NormalizeRule(list[i], e);
                if (r != null) { if (r.Enabled) rules.Add(r); }
                else Errors.Add("rules[" + i + "]: " + string.Join("; ", e.ToArray()));
            }
            foreach (var rule in rules) rs[rule.Id] = new RuleState();
        }

        private BtnState Btn(string b)
        {
            BtnState s;
            if (!btn.TryGetValue(b, out s)) { s = new BtnState(); btn[b] = s; }
            return s;
        }

        private bool AllDown(List<string> buttons)
        {
            for (int i = 0; i < buttons.Count; i++) if (!Btn(buttons[i]).Down) return false;
            return true;
        }

        private long DownSpread(List<string> buttons)
        {
            long min = long.MaxValue, max = long.MinValue;
            for (int i = 0; i < buttons.Count; i++)
            {
                long t = Btn(buttons[i]).DownTime;
                if (t < min) min = t;
                if (t > max) max = t;
            }
            return max - min;
        }

        private static void Emit(Rule rule, long time, List<FiredEntry> outList)
        {
            outList.Add(new FiredEntry
            {
                RuleId = rule.Id,
                Name = rule.Name,
                TriggerKind = rule.Trigger.Kind,
                Action = rule.Action,
                Time = time
            });
        }

        private void CheckLongpress(long at, List<FiredEntry> outList)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (rule.Trigger.Kind != "longpress") continue;
                var st = rs[rule.Id];
                if (!AllDown(rule.Trigger.Buttons)) { st.Fired = false; continue; }
                if (st.Fired) continue;
                long start = long.MinValue;
                for (int j = 0; j < rule.Trigger.Buttons.Count; j++)
                {
                    long dt = Btn(rule.Trigger.Buttons[j]).DownTime;
                    if (dt > start) start = dt;
                }
                if (at - start >= rule.Trigger.HoldMs)
                {
                    st.Fired = true;
                    Emit(rule, start + rule.Trigger.HoldMs, outList);
                }
            }
        }

        private void ExpireSequences(long at)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (rule.Trigger.Kind != "sequence") continue;
                var st = rs[rule.Id];
                if (st.Progress > 0 && at - st.LastTime > rule.Trigger.WindowMs) st.Progress = 0;
            }
        }

        public List<FiredEntry> AdvanceTo(long at)
        {
            if (at < now) at = now;
            now = at;
            var outList = new List<FiredEntry>();
            CheckLongpress(at, outList);
            ExpireSequences(at);
            return outList;
        }

        public List<FiredEntry> HandleEvent(string button, bool isDown, long time)
        {
            var outList = new List<FiredEntry>();
            if (string.IsNullOrEmpty(button)) return outList;
            long t = time;
            if (t < now) t = now;
            now = t;
            CheckLongpress(t, outList);
            ExpireSequences(t);
            var bs = Btn(button);
            if (!isDown)
            {
                if (!bs.Down) return outList;
                bs.Down = false;
                for (int i = 0; i < rules.Count; i++)
                {
                    var rule = rules[i];
                    if (rule.Trigger.Kind == "chord" && rule.Trigger.Buttons.Contains(button))
                        rs[rule.Id].Latched = false;
                }
                return outList;
            }
            /* down edge */
            if (bs.Down) return outList; /* duplicate down: not a new edge */
            bs.Down = true;
            bs.DownTime = t;
            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                var st = rs[rule.Id];
                var tr = rule.Trigger;
                if (tr.Kind == "press")
                {
                    if (tr.Buttons[0] == button) Emit(rule, t, outList);
                }
                else if (tr.Kind == "chord")
                {
                    if (!tr.Buttons.Contains(button) || st.Latched) continue;
                    if (AllDown(tr.Buttons) && (tr.WindowMs == 0 || DownSpread(tr.Buttons) <= tr.WindowMs))
                    {
                        st.Latched = true;
                        Emit(rule, t, outList);
                    }
                }
                else if (tr.Kind == "double")
                {
                    if (tr.Buttons[0] != button) continue;
                    if (st.HasLastDown && t - st.LastDown <= tr.IntervalMs)
                    {
                        st.HasLastDown = false;
                        Emit(rule, t, outList);
                    }
                    else
                    {
                        st.HasLastDown = true;
                        st.LastDown = t;
                    }
                }
                else if (tr.Kind == "sequence")
                {
                    if (button == tr.Buttons[st.Progress])
                    {
                        st.Progress += 1;
                        st.LastTime = t;
                        if (st.Progress >= tr.Buttons.Count)
                        {
                            st.Progress = 0;
                            Emit(rule, t, outList);
                        }
                    }
                    else
                    {
                        st.Progress = (button == tr.Buttons[0]) ? 1 : 0;
                        if (st.Progress == 1) st.LastTime = t;
                    }
                }
            }
            return outList;
        }

        public bool IsDown(string b) { return Btn(b).Down; }
    }
}
