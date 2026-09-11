using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace TechBench
{
    public sealed class UserCode
    {
        public string Brand = "";
        public string Code = "";
        public string Title = "";
        public string Description = "";
        public string Severity = "";
        public string Controller = "";
        public readonly List<string> Causes = new List<string>();
        public readonly List<string> Checks = new List<string>();
        public string Reset = "";
        public string Safety = "";
        /// <summary>Which shop shard this came from. Not written to JSON.</summary>
        public string Author = "";

        public bool IsUsable()
        {
            return Code.Trim().Length > 0 || Title.Trim().Length > 0;
        }
    }

    /// <summary>
    /// Read/write for data\user-codes.json. The loader already merged this file into the index but
    /// there was no way to add to it, so anything a tech worked out on the bench stayed in their head.
    /// </summary>
    public static class UserCodes
    {
        public static string PathFor(string kbRoot)
        {
            return Path.Combine(Path.Combine(kbRoot ?? "", "data"), "user-codes.json");
        }

        public static string KeyOf(UserCode c)
        {
            return (c.Brand ?? "").Trim().ToLowerInvariant() + "\u0001" + (c.Code ?? "").Trim().ToLowerInvariant();
        }

        public static string Fingerprint(UserCode c)
        {
            if (c == null) return "";
            var sb = new StringBuilder();
            sb.Append(c.Brand).Append('\n').Append(c.Code).Append('\n').Append(c.Title).Append('\n');
            sb.Append(c.Description).Append('\n').Append(c.Severity).Append('\n').Append(c.Controller).Append('\n');
            sb.Append(c.Reset).Append('\n').Append(c.Safety).Append('\n');
            foreach (string x in c.Causes) sb.Append(x).Append('\n');
            sb.Append("---\n");
            foreach (string x in c.Checks) sb.Append(x).Append('\n');
            return ShopSync.HashText(sb.ToString());
        }

        public static List<UserCode> Load(string kbRoot)
        {
            return LoadFrom(PathFor(kbRoot));
        }

        public static List<UserCode> LoadFrom(string path)
        {
            var list = new List<UserCode>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return list;
            // Do not swallow parse failures: a failed load that looks empty would let Save
            // overwrite a full shop file with a single new entry.
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            object codes;
            if (root == null || !root.TryGetValue("codes", out codes)) return list;
            var arr = codes as ArrayList;
            if (arr == null) return list;
            foreach (object item in arr)
            {
                var d = item as Dictionary<string, object>;
                if (d == null) continue;
                var uc = new UserCode
                {
                    Brand = Str(d, "brand_id"),
                    Code = Str(d, "code"),
                    Title = Str(d, "title"),
                    Description = Str(d, "description"),
                    Severity = Str(d, "severity"),
                    Controller = Str(d, "controller_id"),
                    Reset = Str(d, "reset_notes"),
                    Safety = Str(d, "safety")
                };
                uc.Causes.AddRange(StrList(d, "likely_causes"));
                uc.Checks.AddRange(StrList(d, "checks"));
                list.Add(uc);
            }
            return list;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : "";
        }

        static List<string> StrList(Dictionary<string, object> d, string key)
        {
            var list = new List<string>();
            object v;
            if (!d.TryGetValue(key, out v)) return list;
            var arr = v as ArrayList;
            if (arr == null) return list;
            foreach (object o in arr)
            {
                string s = o == null ? "" : Convert.ToString(o).Trim();
                if (s.Length > 0) list.Add(s);
            }
            return list;
        }

        /// <summary>
        /// Upsert by brand+code so editing an entry replaces it instead of piling up duplicates.
        /// </summary>
        public static List<UserCode> Upsert(List<UserCode> existing, UserCode entry)
        {
            var list = existing ?? new List<UserCode>();
            if (entry == null || !entry.IsUsable()) return list;
            string key = KeyOf(entry);
            for (int i = 0; i < list.Count; i++)
            {
                if (KeyOf(list[i]) == key) { list[i] = entry; return list; }
            }
            list.Add(entry);
            return list;
        }

        public static void Save(string kbRoot, List<UserCode> codes)
        {
            SaveTo(PathFor(kbRoot), codes);
        }

        public static void SaveTo(string path, List<UserCode> codes)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            // Keep a single rolling backup: this file is hand-editable and losing it loses shop knowledge.
            try
            {
                if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            }
            catch { }
            File.WriteAllText(path, ToJson(codes), new UTF8Encoding(false));
        }

        /// <summary>Indented output because this file is meant to stay hand-editable.</summary>
        public static string ToJson(List<UserCode> codes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"codes\": [");
            var list = codes ?? new List<UserCode>();
            for (int i = 0; i < list.Count; i++)
            {
                UserCode c = list[i];
                sb.AppendLine("    {");
                Field(sb, "brand_id", c.Brand, true);
                Field(sb, "code", c.Code, true);
                Field(sb, "title", c.Title, true);
                Field(sb, "description", c.Description, true);
                Field(sb, "severity", c.Severity, true);
                Field(sb, "controller_id", c.Controller, true);
                Array(sb, "likely_causes", c.Causes);
                Array(sb, "checks", c.Checks);
                Field(sb, "reset_notes", c.Reset, true);
                Field(sb, "safety", c.Safety, true);
                Field(sb, "source", "tech-bench", false);
                sb.AppendLine("    }" + (i < list.Count - 1 ? "," : ""));
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        static void Field(StringBuilder sb, string name, string value, bool comma)
        {
            sb.AppendLine("      \"" + name + "\": " + Quote(value) + (comma ? "," : ""));
        }

        static void Array(StringBuilder sb, string name, List<string> values)
        {
            if (values == null || values.Count == 0)
            {
                sb.AppendLine("      \"" + name + "\": [],");
                return;
            }
            sb.AppendLine("      \"" + name + "\": [");
            for (int i = 0; i < values.Count; i++)
                sb.AppendLine("        " + Quote(values[i]) + (i < values.Count - 1 ? "," : ""));
            sb.AppendLine("      ],");
        }

        public static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        public static List<string> SplitLines(string text)
        {
            var list = new List<string>();
            if (text == null) return list;
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string t = raw.Trim().TrimStart('•', '-', '*').Trim();
                if (t.Length > 0) list.Add(t);
            }
            return list;
        }
    }
}
