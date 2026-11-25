using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Hepta
{
    internal static class DataText
    {
        static JavaScriptSerializer Json()
        {
            return new JavaScriptSerializer
            {
                MaxJsonLength = 16000000,
                RecursionLimit = 48
            };
        }

        public static string Transform(string text, bool yaml)
        {
            if (yaml)
            {
                var value = Json().DeserializeObject(text);
                var writer = new StringBuilder();
                Emit(writer, value, 0);
                return writer.ToString();
            }

            string trimmed = text.Trim();
            if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
                return Json().Serialize(Json().DeserializeObject(trimmed));
            var lines = new List<Line>();
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (raw.Contains('\t'))
                    throw new InvalidDataException("YAML tab indentation");
                string body = raw.TrimStart(' ');
                if (body.Length == 0 || body.StartsWith("#") || body == "---" || body == "...")
                    continue;
                lines.Add(new Line { Depth = raw.Length - body.Length, Value = body.TrimEnd() });
            }

            if (lines.Count == 0)
                throw new InvalidDataException("Empty YAML");
            int index = 0;
            object result = Parse(lines, ref index, 0, 0);
            if (index != lines.Count)
                throw new InvalidDataException("YAML indentation");
            return Json().Serialize(result);
        }

        static bool Container(object v)
        {
            return v is IDictionary<string, object> || v is object[];
        }

        static void Emit(StringBuilder b, object value, int depth)
        {
            string pad = new string (' ', depth);
            var map = value as IDictionary<string, object>;
            var arr = value as object[];
            if (map != null && map.Count > 0)
            {
                foreach (var pair in map)
                    Entry(b, pad + Json().Serialize(pair.Key) + ":", pair.Value, depth);
                return;
            }

            if (arr != null && arr.Length > 0)
            {
                foreach (var item in arr)
                    Entry(b, pad + "-", item, depth);
                return;
            }

            b.AppendLine(pad + Json().Serialize(value));
        }

        static void Entry(StringBuilder b, string prefix, object value, int depth)
        {
            bool nested = Container(value) && ((value is object[] && ((object[])value).Length > 0) || (value is IDictionary<string, object> && ((IDictionary<string, object>)value).Count > 0));
            if (nested)
            {
                b.AppendLine(prefix);
                Emit(b, value, depth + 2);
            }
            else
                b.AppendLine(prefix + " " + Json().Serialize(value));
        }

        class Line
        {
            public int Depth;
            public string Value;
        }

        static int Colon(string s)
        {
            bool quoted = false, escape = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (quoted && c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                    quoted = !quoted;
                if (!quoted && c == ':' && (i + 1 == s.Length || s[i + 1] == ' '))
                    return i;
            }

            return -1;
        }

        static object Parse(List<Line> lines, ref int i, int depth, int level)
        {
            if (level > 48 || i >= lines.Count || lines[i].Depth != depth)
                throw new InvalidDataException("YAML structure");
            string first = lines[i].Value;
            bool list = first == "-" || first.StartsWith("- ");
            if (!list && Colon(first) < 0)
            {
                i++;
                return Scalar(first);
            }

            var arr = new List<object>();
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            while (i < lines.Count && lines[i].Depth == depth)
            {
                string s = lines[i++].Value, key = null, rest;
                if (list)
                {
                    if (s != "-" && !s.StartsWith("- "))
                        throw new InvalidDataException("YAML sequence");
                    rest = s.Substring(1).TrimStart();
                    if (Colon(rest) >= 0)
                        throw new InvalidDataException("Use nested mapping on a separate line");
                }
                else
                {
                    int n = Colon(s);
                    if (n <= 0)
                        throw new InvalidDataException("YAML mapping");
                    key = Scalar(s.Substring(0, n).Trim()) as string;
                    if (key == null || map.ContainsKey(key))
                        throw new InvalidDataException("YAML key");
                    rest = s.Substring(n + 1).TrimStart();
                }

                object value = rest.Length > 0 ? Scalar(rest) : i < lines.Count && lines[i].Depth > depth ? Parse(lines, ref i, lines[i].Depth, level + 1) : null;
                if (list)
                    arr.Add(value);
                else
                    map.Add(key, value);
            }

            return list ? (object)arr.ToArray() : map;
        }

        static object Scalar(string s)
        {
            if (s == "{}" || s == "[]" || s.StartsWith("\"") || s == "null" || s == "true" || s == "false" || Regex.IsMatch(s, @"^-?(0|[1-9]\d*)(\.\d+)?([eE][+-]?\d+)?$"))
                return Json().DeserializeObject(s);
            if (s.StartsWith("'") && s.EndsWith("'") && s.Length >= 2)
                return s.Substring(1, s.Length - 2).Replace("''", "'");
            if (s.Length == 0 || "!&*|>{[}%@".Contains(s[0]) || s.StartsWith("<<") || s.Contains(" #") || s.Contains(": "))
                throw new InvalidDataException("Unsupported YAML syntax");
            return s;
        }
    }
}
