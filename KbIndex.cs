using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace TechBench
{
    public sealed class Hit
    {
        public string Kind;
        public string Title;
        public string Subtitle;
        public string Body;
        public string Path;
        public string Hay;
        /// <summary>Primary identifier (fault code, equipment name) lowercased, for exact-match ranking.</summary>
        public string Key = "";
        public string TitleLow = "";
        public int Score;
        public override string ToString()
        {
            return "[" + Kind + "]  " + Title;
        }
    }

    public sealed class KbIndex
    {
        public const int MaxResults = 300;

        public readonly List<Hit> All = new List<Hit>();
        public readonly List<string> Errors = new List<string>();
        public string Root;
        public string Status = "";

        public static string FindRoot()
        {
            bool found;
            return FindRoot(out found);
        }

        /// <summary>
        /// Looks in the usual places, but an install on someone else's bench PC can point at the KB with
        /// a TECHBENCH_KB environment variable or a kb-path.txt next to the exe.
        /// </summary>
        /// <param name="found">false when no candidate actually has data\kb.json.</param>
        public static string FindRoot(out bool found)
        {
            var candidates = new List<string>();

            string env = null;
            try { env = Environment.GetEnvironmentVariable("TECHBENCH_KB"); }
            catch { }
            if (!string.IsNullOrWhiteSpace(env)) candidates.Add(env.Trim());

            try
            {
                string cfg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kb-path.txt");
                if (File.Exists(cfg))
                    foreach (string line in File.ReadAllLines(cfg))
                    {
                        string t = line.Trim();
                        if (t.Length > 0 && !t.StartsWith("#")) candidates.Add(t);
                    }
            }
            catch { }

            AddSpecial(candidates, Environment.SpecialFolder.MyDocuments, "air-compressor-kb");
            AddSpecial(candidates, Environment.SpecialFolder.UserProfile, "Documents", "air-compressor-kb");
            // README says the KB may live under OneDrive Documents, which MyDocuments only returns when
            // Known Folder Move is on.
            AddSpecial(candidates, Environment.SpecialFolder.UserProfile, "OneDrive", "Documents", "air-compressor-kb");
            AddSpecial(candidates, Environment.SpecialFolder.UserProfile, "air-compressor-kb");
            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                candidates.Add(Path.Combine(exeDir, "air-compressor-kb"));
                DirectoryInfo up = Directory.GetParent(exeDir.TrimEnd(Path.DirectorySeparatorChar));
                if (up != null) candidates.Add(Path.Combine(up.FullName, "air-compressor-kb"));
            }
            catch { }

            foreach (string c in candidates)
            {
                try { if (File.Exists(Path.Combine(c, "data", "kb.json"))) { found = true; return c; } }
                catch { }
            }
            found = false;
            // Nothing found: hand back the most likely location so the error message names a path on
            // this machine rather than whoever's PC the default was written on.
            return candidates.Count > 0 ? candidates[0] : "air-compressor-kb";
        }

        static void AddSpecial(List<string> into, Environment.SpecialFolder folder, params string[] parts)
        {
            try
            {
                string root = Environment.GetFolderPath(folder);
                if (string.IsNullOrEmpty(root)) return;
                string p = root;
                foreach (string part in parts) p = Path.Combine(p, part);
                into.Add(p);
            }
            catch { }
        }

        public void Load()
        {
            Load(FindRoot());
        }

        public void Load(string root)
        {
            All.Clear();
            Errors.Clear();
            Root = root;
            if (string.IsNullOrWhiteSpace(root))
            {
                Status = "No knowledge base folder configured.";
                return;
            }
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

            // Each file is loaded on its own so one bad JSON file costs that section only, instead of
            // leaving the tech with a completely empty index and no idea why.
            int nCode = Try("kb.json", delegate { return LoadCodes(ser, Path.Combine(root, "data", "kb.json")); })
                      + Try("user-codes.json", delegate { return LoadCodes(ser, Path.Combine(root, "data", "user-codes.json")); });
            int nPwd = Try("ifix-passwords.json", delegate { return LoadPasswords(ser, Path.Combine(root, "data", "passwords", "ifix-passwords.json")); });
            int nMan = Try("usb-manuals.json", delegate { return LoadManuals(ser, Path.Combine(root, "data", "usb-manuals.json")); });
            int nFil = Try("rental-portable-filters-oil.json", delegate { return LoadFilters(ser, Path.Combine(root, "data", "rental-portable-filters-oil.json")); });
            int nEq = Try("rental-equipment-info.json", delegate { return LoadEquipment(ser, Path.Combine(root, "data", "rental-equipment-info.json")); });

            Status = string.Format("Codes {0}  ·  Passwords {1}  ·  Manuals {2}  ·  Filters {3}  ·  Equipment {4}",
                nCode, nPwd, nMan, nFil, nEq);
            if (All.Count == 0)
                Status += "  ·  nothing loaded from " + root;
            if (Errors.Count > 0)
                Status += "  ·  " + Errors.Count + " file(s) failed: " + string.Join("; ", Errors.ToArray());
        }

        int Try(string label, Func<int> load)
        {
            try { return load(); }
            catch (Exception ex)
            {
                Errors.Add(label + " (" + ex.Message + ")");
                return 0;
            }
        }

        Dictionary<string, object> ReadRoot(JavaScriptSerializer ser, string path)
        {
            if (!File.Exists(path)) return null;
            return ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
        }

        int LoadPasswords(JavaScriptSerializer ser, string path)
        {
            var rootObj = ReadRoot(ser, path);
            if (rootObj == null) return 0;
            int n = 0;
            foreach (var item in Arr(Get(rootObj, "passwords")))
            {
                var d = Dict(item);
                if (d == null) continue;
                string mfg = S(Get(d, "manufacturer"));
                string ctl = S(Get(d, "controller"));
                string lvl = S(Get(d, "access_level"));
                string code = S(Get(d, "code_or_procedure"));
                Add(new Hit
                {
                    Kind = "PASSWORD",
                    Title = mfg + "  ·  " + ctl + "  ·  " + lvl,
                    Subtitle = "Service access — trained use only",
                    Body = code,
                    Hay = (mfg + " " + ctl + " " + lvl + " " + code).ToLowerInvariant()
                }, ctl);
                n++;
            }
            return n;
        }

        int LoadManuals(JavaScriptSerializer ser, string path)
        {
            var rootObj = ReadRoot(ser, path);
            if (rootObj == null) return 0;
            int n = 0;
            foreach (var item in Arr(Get(rootObj, "documents")))
            {
                var d = Dict(item);
                if (d == null) continue;
                string title = S(Get(d, "document_name"));
                if (title.Length == 0) title = S(Get(d, "filename"));
                string mfg = S(Get(d, "manufacturer"));
                string ctl = S(Get(d, "controller"));
                string local = S(Get(d, "local_path"));
                Add(new Hit
                {
                    Kind = "MANUAL",
                    Title = title,
                    Subtitle = mfg + (ctl.Length > 0 ? "  ·  " + ctl : ""),
                    Body = local,
                    Path = local,
                    Hay = (title + " " + mfg + " " + ctl + " " + S(Get(d, "filename")) + " " + S(Get(d, "category"))).ToLowerInvariant()
                }, title);
                n++;
            }
            return n;
        }

        int LoadFilters(JavaScriptSerializer ser, string path)
        {
            var rootObj = ReadRoot(ser, path);
            if (rootObj == null) return 0;
            int n = 0;
            foreach (var item in Arr(Get(rootObj, "rows")))
            {
                var d = Dict(item);
                if (d == null) continue;
                string type = S(Get(d, "comp_type"));
                string code = S(Get(d, "code"));
                var body = new StringBuilder();
                AddLine(body, "Engine oil filter", d, "eng_oil_filter");
                AddLine(body, "Fuel/water", d, "eng_fuel_water");
                AddLine(body, "Fuel filter", d, "fuel_filter");
                AddLine(body, "Separator", d, "separator");
                AddLine(body, "Air primary", d, "air_filter_primary");
                AddLine(body, "Air filter", d, "air_filter");
                AddLine(body, "Comp oil filter", d, "comp_oil_filter");
                AddLine(body, "Other", d, "other_filters");
                AddLine(body, "Engine oil qty", d, "engine_oil_qty");
                AddLine(body, "Comp oil qty", d, "comp_oil_qty");
                body.AppendLine("Verify ENG vs A/E piping before ordering.");
                Add(new Hit
                {
                    Kind = "FILTER",
                    Title = code + "  ·  " + type,
                    Subtitle = "Rental filter / oil chart",
                    Body = body.ToString(),
                    Hay = (type + " " + code + " " + body).ToLowerInvariant()
                }, code);
                n++;
            }
            return n;
        }

        int LoadEquipment(JavaScriptSerializer ser, string path)
        {
            var rootObj = ReadRoot(ser, path);
            if (rootObj == null) return 0;
            int n = 0;
            foreach (var item in Arr(Get(rootObj, "rows")))
            {
                var d = Dict(item);
                if (d == null) continue;
                string eq = S(Get(d, "equipment"));
                string body =
                    "W " + S(Get(d, "width")) + "  L " + S(Get(d, "length")) + "  H " + S(Get(d, "height")) + "\r\n" +
                    "Weight " + S(Get(d, "weight")) + "\r\n" +
                    "Rim " + S(Get(d, "rim_size")) + "  Tire " + S(Get(d, "tire_size")) + "\r\n" +
                    "ACS " + S(Get(d, "acs_part_no"));
                Add(new Hit
                {
                    Kind = "EQUIP",
                    Title = eq,
                    Subtitle = "Dims / tires / ACS",
                    Body = body,
                    Hay = (eq + " " + body + " " + S(Get(d, "acs_part_no"))).ToLowerInvariant()
                }, eq);
                n++;
            }
            return n;
        }

        int LoadCodes(JavaScriptSerializer ser, string path)
        {
            var rootObj = ReadRoot(ser, path);
            if (rootObj == null) return 0;
            int n = 0;
            foreach (var item in Arr(Get(rootObj, "codes")))
            {
                var d = Dict(item);
                if (d == null) continue;
                string brand = S(Get(d, "brand_id"));
                string code = S(Get(d, "code"));
                string title = S(Get(d, "title"));
                string desc = S(Get(d, "description"));
                var body = new StringBuilder();
                if (desc.Length > 0) body.AppendLine(desc);
                body.AppendLine();
                body.AppendLine("Likely causes:");
                foreach (var x in Arr(Get(d, "likely_causes"))) body.AppendLine("  • " + S(x));
                body.AppendLine();
                body.AppendLine("Checks:");
                foreach (var x in Arr(Get(d, "checks"))) body.AppendLine("  • " + S(x));
                string reset = S(Get(d, "reset_notes"));
                if (reset.Length > 0) { body.AppendLine(); body.AppendLine("Reset: " + reset); }
                string safety = S(Get(d, "safety"));
                if (safety.Length > 0) { body.AppendLine(); body.AppendLine("Safety: " + safety); }
                string sev = S(Get(d, "severity"));
                Add(new Hit
                {
                    Kind = "CODE",
                    Title = brand + "  " + code + "  —  " + title,
                    Subtitle = sev + "  ·  " + S(Get(d, "controller_id")) + "  ·  " + S(Get(d, "confidence")),
                    Body = body.ToString(),
                    Hay = (brand + " " + code + " " + title + " " + desc + " " + S(Get(d, "controller_id")) + " " + JoinArr(Get(d, "tags"))).ToLowerInvariant()
                }, code);
                n++;
            }
            return n;
        }

        void Add(Hit h, string key)
        {
            h.Key = (key ?? "").Trim().ToLowerInvariant();
            h.TitleLow = (h.Title ?? "").ToLowerInvariant();
            All.Add(h);
        }

        public List<Hit> Search(string query, string kind)
        {
            int total;
            return Search(query, kind, MaxResults, out total);
        }

        /// <param name="total">Every match, not just the ones returned, so the UI can say when it capped.</param>
        public List<Hit> Search(string query, string kind, int max, out int total)
        {
            var hits = new List<Hit>();
            total = 0;
            if (string.IsNullOrWhiteSpace(query))
            {
                foreach (Hit h in All)
                {
                    if (kind != "ALL" && h.Kind != kind) continue;
                    total++;
                    if (hits.Count < max) hits.Add(h);
                }
                return hits;
            }

            string q = query.Trim().ToLowerInvariant();
            string[] toks = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var matches = new List<Hit>();
            foreach (Hit h in All)
            {
                if (kind != "ALL" && h.Kind != kind) continue;
                bool ok = true;
                foreach (string t in toks)
                    if (h.Hay.IndexOf(t, StringComparison.Ordinal) < 0) { ok = false; break; }
                if (!ok) continue;
                h.Score = Score(h, q, toks);
                matches.Add(h);
            }
            total = matches.Count;
            // An exact fault-code match has to come first. Unranked, "F68" buried the F68 entry behind
            // every row that merely mentioned it.
            matches.Sort(delegate(Hit a, Hit b)
            {
                int c = b.Score.CompareTo(a.Score);
                return c != 0 ? c : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            });
            for (int i = 0; i < matches.Count && i < max; i++) hits.Add(matches[i]);
            return hits;
        }

        static int Score(Hit h, string q, string[] toks)
        {
            int s = 0;
            if (h.Key.Length > 0)
            {
                if (h.Key == q) s += 1000;
                else if (h.Key.StartsWith(q, StringComparison.Ordinal)) s += 400;
                else if (h.Key.IndexOf(q, StringComparison.Ordinal) >= 0) s += 150;
            }
            if (h.TitleLow.IndexOf(q, StringComparison.Ordinal) >= 0) s += 120;
            foreach (string t in toks)
            {
                if (h.TitleLow.IndexOf(t, StringComparison.Ordinal) >= 0) s += 30;
                if (h.Key.Length > 0 && h.Key.IndexOf(t, StringComparison.Ordinal) >= 0) s += 20;
            }
            if (h.Kind == "CODE") s += 10;
            return s;
        }

        static object Get(Dictionary<string, object> d, string k)
        {
            if (d == null) return null;
            object v;
            return d.TryGetValue(k, out v) ? v : null;
        }
        static Dictionary<string, object> Dict(object o) { return o as Dictionary<string, object>; }
        static IEnumerable<object> Arr(object o)
        {
            var a = o as ArrayList;
            if (a == null) yield break;
            foreach (object x in a) yield return x;
        }
        static string S(object o) { return o == null ? "" : Convert.ToString(o); }
        static void AddLine(StringBuilder sb, string label, Dictionary<string, object> d, string key)
        {
            string v = S(Get(d, key));
            if (v.Length > 0) sb.AppendLine(label + ": " + v);
        }
        static string JoinArr(object o)
        {
            var sb = new StringBuilder();
            foreach (var x in Arr(o)) { if (sb.Length > 0) sb.Append(' '); sb.Append(S(x)); }
            return sb.ToString();
        }
    }
}
