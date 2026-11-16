using System.Collections.Generic;

namespace HandsCompanion
{
    public struct KeyStroke
    {
        public ushort Scan;      /* PS/2 set-1 scan code */
        public bool Extended;    /* true when the key needs the E0 prefix */
        public KeyStroke(ushort scan, bool extended) { Scan = scan; Extended = extended; }
    }

    /* Maps the studio's key tokens to Windows scan codes.
       The studio records KeyboardEvent.key values with three normalizations
       (see keyTokenFromEvent in index.html): "Control" -> "Ctrl",
       " " -> "Space", "Escape" -> "Esc", and single characters uppercased.
       Injection uses SendInput with KEYEVENTF_SCANCODE (per the design
       memo), so games that read raw input accept the keys.
       "Ctrl" / "Alt" / "Shift" mean the LEFT modifier; "Meta" is the left
       Windows key. Punctuation follows the US layout: the studio stores the
       character the key produces, and a shifted character (e.g. "!") is
       sent as its base key — profiles normally pair it with a "Shift"
       token, exactly as captured. Tokens with no scan-code equivalent
       (PrintScreen, Pause, media keys, F13+) are rejected at load time so
       the failure is loud at startup, never silent at press time. */
    public static class KeyMap
    {
        private static readonly Dictionary<string, KeyStroke> Table = Build();

        private static void Add(Dictionary<string, KeyStroke> d, string token, ushort scan)
        {
            d[token] = new KeyStroke(scan, false);
        }
        private static void AddX(Dictionary<string, KeyStroke> d, string token, ushort scan)
        {
            d[token] = new KeyStroke(scan, true);
        }

        private static Dictionary<string, KeyStroke> Build()
        {
            var d = new Dictionary<string, KeyStroke>();
            Add(d, "Esc", 0x01);
            Add(d, "1", 0x02); Add(d, "2", 0x03); Add(d, "3", 0x04); Add(d, "4", 0x05);
            Add(d, "5", 0x06); Add(d, "6", 0x07); Add(d, "7", 0x08); Add(d, "8", 0x09);
            Add(d, "9", 0x0A); Add(d, "0", 0x0B);
            Add(d, "-", 0x0C); Add(d, "=", 0x0D); Add(d, "Backspace", 0x0E); Add(d, "Tab", 0x0F);
            Add(d, "Q", 0x10); Add(d, "W", 0x11); Add(d, "E", 0x12); Add(d, "R", 0x13);
            Add(d, "T", 0x14); Add(d, "Y", 0x15); Add(d, "U", 0x16); Add(d, "I", 0x17);
            Add(d, "O", 0x18); Add(d, "P", 0x19); Add(d, "[", 0x1A); Add(d, "]", 0x1B);
            Add(d, "Enter", 0x1C); Add(d, "Ctrl", 0x1D);
            Add(d, "A", 0x1E); Add(d, "S", 0x1F); Add(d, "D", 0x20); Add(d, "F", 0x21);
            Add(d, "G", 0x22); Add(d, "H", 0x23); Add(d, "J", 0x24); Add(d, "K", 0x25);
            Add(d, "L", 0x26); Add(d, ";", 0x27); Add(d, "'", 0x28); Add(d, "`", 0x29);
            Add(d, "Shift", 0x2A); Add(d, "\\", 0x2B);
            Add(d, "Z", 0x2C); Add(d, "X", 0x2D); Add(d, "C", 0x2E); Add(d, "V", 0x2F);
            Add(d, "B", 0x30); Add(d, "N", 0x31); Add(d, "M", 0x32);
            Add(d, ",", 0x33); Add(d, ".", 0x34); Add(d, "/", 0x35);
            Add(d, "Alt", 0x38); Add(d, "Space", 0x39); Add(d, "CapsLock", 0x3A);
            Add(d, "F1", 0x3B); Add(d, "F2", 0x3C); Add(d, "F3", 0x3D); Add(d, "F4", 0x3E);
            Add(d, "F5", 0x3F); Add(d, "F6", 0x40); Add(d, "F7", 0x41); Add(d, "F8", 0x42);
            Add(d, "F9", 0x43); Add(d, "F10", 0x44);
            Add(d, "NumLock", 0x45); Add(d, "ScrollLock", 0x46);
            Add(d, "F11", 0x57); Add(d, "F12", 0x58);
            /* Extended keys (E0 prefix). */
            AddX(d, "Home", 0x47); AddX(d, "ArrowUp", 0x48); AddX(d, "PageUp", 0x49);
            AddX(d, "ArrowLeft", 0x4B); AddX(d, "ArrowRight", 0x4D);
            AddX(d, "End", 0x4F); AddX(d, "ArrowDown", 0x50); AddX(d, "PageDown", 0x51);
            AddX(d, "Insert", 0x52); AddX(d, "Delete", 0x53);
            AddX(d, "Meta", 0x5B); AddX(d, "ContextMenu", 0x5D);
            AddX(d, "AltGraph", 0x38);
            /* Shifted punctuation (US layout) -> base key. */
            Add(d, "!", 0x02); Add(d, "@", 0x03); Add(d, "#", 0x04); Add(d, "$", 0x05);
            Add(d, "%", 0x06); Add(d, "^", 0x07); Add(d, "&", 0x08); Add(d, "*", 0x09);
            Add(d, "(", 0x0A); Add(d, ")", 0x0B); Add(d, "_", 0x0C); Add(d, "+", 0x0D);
            Add(d, "{", 0x1A); Add(d, "}", 0x1B); Add(d, "|", 0x2B); Add(d, ":", 0x27);
            Add(d, "\"", 0x28); Add(d, "~", 0x29); Add(d, "<", 0x33); Add(d, ">", 0x34);
            Add(d, "?", 0x35);
            /* Raw KeyboardEvent.key spellings, accepted so hand-written
               profiles using un-normalized names still load. */
            d["Escape"] = d["Esc"];
            d["Control"] = d["Ctrl"];
            d[" "] = d["Space"];
            return d;
        }

        public static bool TryMap(string token, out KeyStroke stroke)
        {
            stroke = new KeyStroke();
            if (string.IsNullOrEmpty(token)) return false;
            if (Table.TryGetValue(token, out stroke)) return true;
            /* The studio uppercases single characters; accept a lowercase
               letter from a hand-edited profile too. */
            if (token.Length == 1)
            {
                char c = token[0];
                if (c >= 'a' && c <= 'z')
                    return Table.TryGetValue(new string(new char[] { (char)(c - 'a' + 'A') }), out stroke);
            }
            return false;
        }

        /* Every key token a profile uses must be sendable; unknown tokens
           are startup errors (with the rule named), never silent drops. */
        public static List<string> ValidateProfileKeys(Profile profile)
        {
            var errors = new List<string>();
            if (profile == null) return errors;
            foreach (var rule in profile.Rules)
            {
                if (rule.Action == null) continue;
                if (rule.Action.Kind == "keys")
                    CheckTokens(rule, rule.Action.Keys, errors);
                else if (rule.Action.Kind == "macro")
                    foreach (var step in rule.Action.Steps) CheckTokens(rule, step.Keys, errors);
            }
            return errors;
        }

        private static void CheckTokens(Rule rule, List<string> tokens, List<string> errors)
        {
            foreach (var tok in tokens)
            {
                KeyStroke s;
                if (!TryMap(tok, out s))
                    errors.Add("rule \"" + (rule.Name ?? "") + "\": key \"" + tok + "\" has no scan-code mapping and cannot be sent");
            }
        }
    }
}
