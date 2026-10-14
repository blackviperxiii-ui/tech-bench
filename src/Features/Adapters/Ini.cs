using System;
using System.Collections.Generic;
using System.IO;

namespace J1939Reader
{
    /// <summary>
    /// Just enough INI reading for the RP1210 vendor files. Hand-rolled rather than
    /// GetPrivateProfileString so it can be unit tested off Windows.
    /// </summary>
    internal sealed class Ini
    {
        readonly Dictionary<string, Dictionary<string, string>> _sections =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public static Ini Parse(string text)
        {
            var ini = new Ini();
            if (text == null) return ini;
            var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ini._sections[""] = current;
            foreach (string raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[')
                {
                    int close = line.IndexOf(']');
                    string name = close > 1 ? line.Substring(1, close - 1).Trim() : line.Substring(1).Trim();
                    if (!ini._sections.TryGetValue(name, out current))
                    {
                        current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        ini._sections[name] = current;
                    }
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq < 1) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                // Vendor INIs quote some values; RP1210 readers are expected to tolerate that.
                if (val.Length >= 2 && val[0] == '"' && val[val.Length - 1] == '"')
                    val = val.Substring(1, val.Length - 2);
                current[key] = val;
            }
            return ini;
        }

        public static Ini Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return Parse(File.ReadAllText(path));
            }
            catch { return null; }
        }

        public bool HasSection(string section)
        {
            return _sections.ContainsKey(section ?? "");
        }

        public string Get(string section, string key)
        {
            Dictionary<string, string> s;
            if (!_sections.TryGetValue(section ?? "", out s)) return "";
            string v;
            return s.TryGetValue(key ?? "", out v) ? v : "";
        }

        public int GetInt(string section, string key, int fallback)
        {
            int v;
            return int.TryParse(Get(section, key).Trim(), out v) ? v : fallback;
        }

        /// <summary>Comma-separated value list, empties dropped.</summary>
        public List<string> GetList(string section, string key)
        {
            var list = new List<string>();
            foreach (string part in Get(section, key).Split(','))
            {
                string t = part.Trim();
                if (t.Length > 0) list.Add(t);
            }
            return list;
        }

        public List<int> GetIntList(string section, string key)
        {
            var list = new List<int>();
            foreach (string s in GetList(section, key))
            {
                int v;
                if (int.TryParse(s, out v) && !list.Contains(v)) list.Add(v);
            }
            return list;
        }
    }
}
