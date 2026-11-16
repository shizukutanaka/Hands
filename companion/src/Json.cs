using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HandsCompanion
{
    /* Minimal strict JSON parser: profiles are data, never code, and the
       companion treats the file as untrusted input. Values map to
       Dictionary<string,object> / List<object> / string / double / bool /
       null. Anything malformed raises FormatException. No external
       dependencies, so the shipped build needs only the .NET Framework
       compiler that ships with Windows itself. */
    public static class Json
    {
        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("json: null input");
            var p = new Parser(text);
            p.SkipWs();
            object v = p.ParseValue();
            p.SkipWs();
            if (!p.AtEnd) throw new FormatException("json: trailing characters at offset " + p.Pos);
            return v;
        }

        /* Canonical serialization used by the tests to compare values
           (e.g. action objects) structurally. Object keys are sorted. */
        public static string Canon(object v)
        {
            var sb = new StringBuilder();
            WriteCanon(v, sb);
            return sb.ToString();
        }

        private static void WriteCanon(object v, StringBuilder sb)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is double)
            {
                double d = (double)v;
                if (d == Math.Floor(d) && Math.Abs(d) < 1e15)
                    sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
                else
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (v is string) { WriteString((string)v, sb); return; }
            var list = v as List<object>;
            if (list != null)
            {
                sb.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    WriteCanon(list[i], sb);
                }
                sb.Append(']');
                return;
            }
            var dict = v as Dictionary<string, object>;
            if (dict != null)
            {
                var keys = new List<string>(dict.Keys);
                keys.Sort(StringComparer.Ordinal);
                sb.Append('{');
                for (int i = 0; i < keys.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    WriteString(keys[i], sb);
                    sb.Append(':');
                    WriteCanon(dict[keys[i]], sb);
                }
                sb.Append('}');
                return;
            }
            throw new InvalidOperationException("canon: unsupported type " + v.GetType().Name);
        }

        private static void WriteString(string s, StringBuilder sb)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private sealed class Parser
        {
            private readonly string s;
            private int pos;
            public int Pos { get { return pos; } }
            public bool AtEnd { get { return pos >= s.Length; } }

            public Parser(string text) { s = text; pos = 0; }

            public void SkipWs()
            {
                while (pos < s.Length)
                {
                    char c = s[pos];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') pos++;
                    else break;
                }
            }

            private char Peek()
            {
                if (pos >= s.Length) throw new FormatException("json: unexpected end of input");
                return s[pos];
            }

            public object ParseValue()
            {
                char c = Peek();
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return ParseString();
                if (c == 't') { Expect("true"); return true; }
                if (c == 'f') { Expect("false"); return false; }
                if (c == 'n') { Expect("null"); return null; }
                return ParseNumber();
            }

            private void Expect(string lit)
            {
                if (pos + lit.Length > s.Length || s.Substring(pos, lit.Length) != lit)
                    throw new FormatException("json: invalid literal at offset " + pos);
                pos += lit.Length;
            }

            private Dictionary<string, object> ParseObject()
            {
                var dict = new Dictionary<string, object>();
                pos++; /* { */
                SkipWs();
                if (Peek() == '}') { pos++; return dict; }
                while (true)
                {
                    SkipWs();
                    if (Peek() != '"') throw new FormatException("json: object key must be a string at offset " + pos);
                    string key = ParseString();
                    SkipWs();
                    if (Peek() != ':') throw new FormatException("json: expected ':' at offset " + pos);
                    pos++;
                    SkipWs();
                    dict[key] = ParseValue();
                    SkipWs();
                    char c = Peek();
                    if (c == ',') { pos++; continue; }
                    if (c == '}') { pos++; return dict; }
                    throw new FormatException("json: expected ',' or '}' at offset " + pos);
                }
            }

            private List<object> ParseArray()
            {
                var list = new List<object>();
                pos++; /* [ */
                SkipWs();
                if (Peek() == ']') { pos++; return list; }
                while (true)
                {
                    SkipWs();
                    list.Add(ParseValue());
                    SkipWs();
                    char c = Peek();
                    if (c == ',') { pos++; continue; }
                    if (c == ']') { pos++; return list; }
                    throw new FormatException("json: expected ',' or ']' at offset " + pos);
                }
            }

            private string ParseString()
            {
                var sb = new StringBuilder();
                pos++; /* opening quote */
                while (true)
                {
                    if (pos >= s.Length) throw new FormatException("json: unterminated string");
                    char c = s[pos++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        if (pos >= s.Length) throw new FormatException("json: unterminated escape");
                        char e = s[pos++];
                        switch (e)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (pos + 4 > s.Length) throw new FormatException("json: bad \\u escape");
                                int code = int.Parse(s.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                                pos += 4;
                                /* Surrogate pairs: a high surrogate followed by
                                   a \u low surrogate combines naturally in a
                                   UTF-16 string, so appending both halves is
                                   enough. */
                                sb.Append((char)code);
                                break;
                            default:
                                throw new FormatException("json: bad escape '\\" + e + "'");
                        }
                    }
                    else if (c < 0x20)
                    {
                        throw new FormatException("json: control character in string");
                    }
                    else sb.Append(c);
                }
            }

            private double ParseNumber()
            {
                int start = pos;
                if (pos < s.Length && s[pos] == '-') pos++;
                while (pos < s.Length && s[pos] >= '0' && s[pos] <= '9') pos++;
                if (pos < s.Length && s[pos] == '.')
                {
                    pos++;
                    while (pos < s.Length && s[pos] >= '0' && s[pos] <= '9') pos++;
                }
                if (pos < s.Length && (s[pos] == 'e' || s[pos] == 'E'))
                {
                    pos++;
                    if (pos < s.Length && (s[pos] == '+' || s[pos] == '-')) pos++;
                    while (pos < s.Length && s[pos] >= '0' && s[pos] <= '9') pos++;
                }
                if (pos == start) throw new FormatException("json: invalid value at offset " + start);
                string num = s.Substring(start, pos - start);
                double d;
                if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    throw new FormatException("json: invalid number '" + num + "'");
                return d;
            }
        }
    }
}
