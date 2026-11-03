using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

namespace SessionSync
{
    static class Json
    {
        static JavaScriptSerializer Ser()
        {
            var s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            s.RecursionLimit = 512;
            return s;
        }

        public static object Parse(string text) { return Ser().DeserializeObject(text); }

        public static string Write(object value) { return Ser().Serialize(value); }

        public static Dictionary<string, object> ParseObject(string text)
        {
            var d = Parse(text) as Dictionary<string, object>;
            if (d == null) throw new FormatException(L.T("JSON 不是物件"));
            return d;
        }

        // Reads only the named top-level fields whose values are strings, numbers,
        // booleans or null. Everything else is skipped without being built.
        // Session cards can be hundreds of KB; the list view needs a dozen small fields.
        public static Dictionary<string, object> TopLevel(string s, ICollection<string> wanted)
        {
            var result = new Dictionary<string, object>();
            int i = 0;
            Ws(s, ref i);
            Expect(s, ref i, '{');
            while (true)
            {
                Ws(s, ref i);
                if (Peek(s, i) == '}') break;
                string key = ReadString(s, ref i);
                Ws(s, ref i);
                Expect(s, ref i, ':');
                Ws(s, ref i);
                if (wanted.Contains(key)) result[key] = ReadPrimitiveOrSkip(s, ref i);
                else SkipValue(s, ref i);
                Ws(s, ref i);
                char c = Peek(s, i);
                if (c == ',') { i++; continue; }
                if (c == '}') break;
                throw new FormatException(L.F("JSON 格式不正確，位置 {0}", i));
            }
            return result;
        }

        static char Peek(string s, int i)
        {
            if (i >= s.Length) throw new FormatException(L.T("JSON 提早結束"));
            return s[i];
        }

        static void Ws(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n' || s[i] == '﻿')) i++;
        }

        static void Expect(string s, ref int i, char c)
        {
            if (Peek(s, i) != c) throw new FormatException(L.F("JSON 預期 '{0}'，位置 {1}", c, i));
            i++;
        }

        static string ReadString(string s, ref int i)
        {
            Expect(s, ref i, '"');
            var sb = new StringBuilder();
            while (true)
            {
                char c = Peek(s, i++);
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = Peek(s, i++);
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
        }

        static object ReadPrimitiveOrSkip(string s, ref int i)
        {
            char c = Peek(s, i);
            if (c == '"') return ReadString(s, ref i);
            if (c == '{' || c == '[') { SkipValue(s, ref i); return null; }
            int start = i;
            while (i < s.Length && ",}] \t\r\n".IndexOf(s[i]) < 0) i++;
            string tok = s.Substring(start, i - start);
            if (tok == "true") return true;
            if (tok == "false") return false;
            if (tok == "null") return null;
            long l;
            if (long.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l;
            return double.Parse(tok, CultureInfo.InvariantCulture);
        }

        static void SkipValue(string s, ref int i)
        {
            char c = Peek(s, i);
            if (c == '"') { SkipString(s, ref i); return; }
            if (c != '{' && c != '[')
            {
                while (i < s.Length && ",}] \t\r\n".IndexOf(s[i]) < 0) i++;
                return;
            }
            int depth = 0;
            while (true)
            {
                c = Peek(s, i);
                if (c == '"') { SkipString(s, ref i); continue; }
                i++;
                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') { depth--; if (depth == 0) return; }
            }
        }

        static void SkipString(string s, ref int i)
        {
            i++;
            while (true)
            {
                char c = Peek(s, i++);
                if (c == '\\') { i++; continue; }
                if (c == '"') return;
            }
        }

        // Small typed readers for loosely typed JavaScriptSerializer output.
        public static long Long(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return 0;
            if (v is long) return (long)v;
            if (v is int) return (int)v;
            if (v is double) return (long)(double)v;
            if (v is decimal) return (long)(decimal)v;
            long l;
            return long.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out l) ? l : 0;
        }

        public static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return null;
            return v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static bool Bool(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v is bool && (bool)v;
        }
    }
}
