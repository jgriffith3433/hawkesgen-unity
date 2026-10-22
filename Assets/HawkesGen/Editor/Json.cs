// A small JSON reader and writer for the Hawkes Gen API: objects are Dictionary<string, object>, arrays List<object>,
// numbers double, plus string, bool and null. Enough for the API's answers, with no package to install.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HawkesGen.Editor
{
    public static class Json
    {
        public static object Parse(string text)
        {
            int i = 0;
            object value = ReadValue(text, ref i);
            SkipSpace(text, ref i);
            if (i != text.Length) throw new FormatException("Unexpected text after the JSON value");
            return value;
        }

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        // Typed reads from a parsed object, with a fallback when the key is missing or of another type.
        public static Dictionary<string, object> Obj(object o, string key) => (o as Dictionary<string, object>) != null && ((Dictionary<string, object>)o).TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;
        public static List<object> Arr(object o, string key) => (o as Dictionary<string, object>) != null && ((Dictionary<string, object>)o).TryGetValue(key, out var v) ? v as List<object> : null;
        public static string Str(object o, string key, string fallback = null) => (o as Dictionary<string, object>) != null && ((Dictionary<string, object>)o).TryGetValue(key, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : fallback;
        public static double Num(object o, string key, double fallback = 0) => (o as Dictionary<string, object>) != null && ((Dictionary<string, object>)o).TryGetValue(key, out var v) && v is double d ? d : fallback;
        public static bool Bool(object o, string key) => (o as Dictionary<string, object>) != null && ((Dictionary<string, object>)o).TryGetValue(key, out var v) && v is bool b && b;

        static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static object ReadValue(string s, ref int i)
        {
            SkipSpace(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
            char c = s[i];
            if (c == '{') return ReadObject(s, ref i);
            if (c == '[') return ReadArray(s, ref i);
            if (c == '"') return ReadString(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (start == i) throw new FormatException($"Unexpected character '{c}' in JSON");
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static Dictionary<string, object> ReadObject(string s, ref int i)
        {
            var obj = new Dictionary<string, object>();
            i++;
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return obj; }
            while (true)
            {
                SkipSpace(s, ref i);
                string key = ReadString(s, ref i);
                SkipSpace(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("Expected ':' in JSON object");
                i++;
                obj[key] = ReadValue(s, ref i);
                SkipSpace(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return obj; }
                throw new FormatException("Expected ',' or '}' in JSON object");
            }
        }

        static List<object> ReadArray(string s, ref int i)
        {
            var list = new List<object>();
            i++;
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ReadValue(s, ref i));
                SkipSpace(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return list; }
                throw new FormatException("Expected ',' or ']' in JSON array");
            }
        }

        static string ReadString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("Expected a string in JSON");
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("Unterminated string in JSON");
        }

        static void WriteValue(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string str: WriteString(sb, str); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case IDictionary<string, object> d:
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in d)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, kv.Key);
                        sb.Append(':');
                        WriteValue(sb, kv.Value);
                    }
                    sb.Append('}');
                    break;
                case System.Collections.IEnumerable list:
                    sb.Append('[');
                    bool firstItem = true;
                    foreach (var item in list)
                    {
                        if (!firstItem) sb.Append(',');
                        firstItem = false;
                        WriteValue(sb, item);
                    }
                    sb.Append(']');
                    break;
                case IFormattable f: sb.Append(f.ToString(null, CultureInfo.InvariantCulture)); break;
                default: WriteString(sb, v.ToString()); break;
            }
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
