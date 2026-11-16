using System.Collections.Generic;

namespace HandsCompanion
{
    /* One low-level key operation to perform, in order. DelayBeforeMs is
       slept before the operation (macro step timing). */
    public struct SendOp
    {
        public ushort Scan;
        public bool Extended;
        public bool IsUp;
        public int DelayBeforeMs;
        public SendOp(ushort scan, bool extended, bool isUp, int delayBeforeMs)
        {
            Scan = scan; Extended = extended; IsUp = isUp; DelayBeforeMs = delayBeforeMs;
        }
    }

    /* Turns a fired action into the concrete key operations to send.
       Pure and platform-free so the exact sequence can be unit-tested.
       - keys:  press every key down in listed order, release in reverse
                order (a tap of the combination).
       - macro: for each step, wait the step's delayMs, then tap the step's
                keys. This matches the studio's own simulation, where step
                i is shown at the cumulative sum of delays 0..i.
       - none:  nothing to send. */
    public static class Planner
    {
        public static List<SendOp> Plan(RuleAction action)
        {
            var ops = new List<SendOp>();
            if (action == null) return ops;
            if (action.Kind == "keys")
            {
                AddTap(ops, action.Keys, 0);
            }
            else if (action.Kind == "macro")
            {
                foreach (var step in action.Steps)
                    AddTap(ops, step.Keys, (int)step.DelayMs);
            }
            return ops;
        }

        private static void AddTap(List<SendOp> ops, List<string> tokens, int delayBeforeMs)
        {
            var strokes = new List<KeyStroke>();
            foreach (var tok in tokens)
            {
                KeyStroke s;
                if (KeyMap.TryMap(tok, out s)) strokes.Add(s);
                /* Unmappable tokens are rejected at load time; a token that
                   still slips through is skipped here only because Plan has
                   no error channel — ValidateProfileKeys runs first. */
            }
            for (int i = 0; i < strokes.Count; i++)
                ops.Add(new SendOp(strokes[i].Scan, strokes[i].Extended, false, i == 0 ? delayBeforeMs : 0));
            for (int i = strokes.Count - 1; i >= 0; i--)
                ops.Add(new SendOp(strokes[i].Scan, strokes[i].Extended, true, 0));
        }
    }
}
