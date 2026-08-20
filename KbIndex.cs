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
        public override string ToString()
        {
            return "[" + Kind + "]  " + Title;
        }
    }

    public sealed class KbIndex
    {
        public readonly List<Hit> All = new List<Hit>();
        public string Root;
        public string Status = "";

        public static string FindRoot()
        {
            string[] candidates = {
                @"C:\Users\jerem\Documents\air-compressor-kb",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "air-compressor-kb"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "air-compressor-kb")
            };
            foreach (string c in candidates)
                if (File.Exists(Path.Combine(c, "data", "kb.json"))) return c;
            return candidates[0];
        }

        public void Load()
        {
            Load(FindRoot());
        }

        public void Load(string root)
        {
            All.Clear();
            Root = root;
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            int nCode = 0, nPwd = 0, nMan = 0, nFil = 0, nEq = 0;

            string kbPath = Path.Combine(root, "data", "kb.json");
            nCode += LoadCodes(ser, kbPath);
            nCode += LoadCodes(ser, Path.Combine(root, "data", "user-codes.json"));

            string pwdPath = Path.Combine(root, "data", "passwords", "ifix-passwords.json");
            if (File.Exists(pwdPath))
            {
                var rootObj = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(pwdPath));
                foreach (var item in Arr(Get(rootObj, "passwords")))
                {
                    var d = Dict(item);
                    if (d == null) continue;
                    string mfg = S(Get(d, "manufacturer"));
                    string ctl = S(Get(d, "controller"));
                    string lvl = S(Get(d, "access_level"));
                    string code = S(Get(d, "code_or_procedure"));
                    var h = new Hit
                    {
                        Kind = "PASSWORD",
                        Title = mfg + "  ·  " + ctl + "  ·  " + lvl,
                        Subtitle = "Service access — trained use only",
                        Body = code,
                        Hay = (mfg + " " + ctl + " " + lvl + " " + code).ToLowerInvariant()
                    };
                    All.Add(h);
                    nPwd++;
                }
            }

            string manPath = Path.Combine(root, "data", "usb-manuals.json");
            if (File.Exists(manPath))
            {
                var rootObj = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(manPath));
                foreach (var item in Arr(Get(rootObj, "documents")))
                {
                    var d = Dict(item);
                    if (d == null) continue;
                    string title = S(Get(d, "document_name"));
                    if (title.Length == 0) title = S(Get(d, "filename"));
                    string mfg = S(Get(d, "manufacturer"));
                    string ctl = S(Get(d, "controller"));
                    string local = S(Get(d, "local_path"));
                    var h = new Hit
                    {
                        Kind = "MANUAL",
                        Title = title,
                        Subtitle = mfg + (ctl.Length > 0 ? "  ·  " + ctl : ""),
                        Body = local,
                        Path = local,
                        Hay = (title + " " + mfg + " " + ctl + " " + S(Get(d, "filename")) + " " + S(Get(d, "category"))).ToLowerInvariant()
                    };
                    All.Add(h);
                    nMan++;
                }
            }

            string filPath = Path.Combine(root, "data", "rental-portable-filters-oil.json");
            if (File.Exists(filPath))
            {
                var rootObj = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(filPath));
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
                    var h = new Hit
                    {
                        Kind = "FILTER",
                        Title = code + "  ·  " + type,
                        Subtitle = "Rental filter / oil chart",
                        Body = body.ToString(),
                        Hay = (type + " " + code + " " + body).ToLowerInvariant()
                    };
                    All.Add(h);
                    nFil++;
                }
            }

            string eqPath = Path.Combine(root, "data", "rental-equipment-info.json");
            if (File.Exists(eqPath))
            {
                var rootObj = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(eqPath));
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
                    var h = new Hit
                    {
                        Kind = "EQUIP",
                        Title = eq,
                        Subtitle = "Dims / tires / ACS",
                        Body = body,
                        Hay = (eq + " " + body + " " + S(Get(d, "acs_part_no"))).ToLowerInvariant()
                    };
                    All.Add(h);
                    nEq++;
                }
            }

            Status = string.Format("Codes {0}  ·  Passwords {1}  ·  Manuals {2}  ·  Filters {3}  ·  Equipment {4}",
                nCode, nPwd, nMan, nFil, nEq);
        }

        int LoadCodes(JavaScriptSerializer ser, string path)
        {
            if (!File.Exists(path)) return 0;
            var rootObj = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
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
                var h = new Hit
                {
                    Kind = "CODE",
                    Title = brand + "  " + code + "  —  " + title,
                    Subtitle = sev + "  ·  " + S(Get(d, "controller_id")) + "  ·  " + S(Get(d, "confidence")),
                    Body = body.ToString(),
                    Hay = (brand + " " + code + " " + title + " " + desc + " " + S(Get(d, "controller_id")) + " " + JoinArr(Get(d, "tags"))).ToLowerInvariant()
                };
                All.Add(h);
                n++;
            }
            return n;
        }

        public List<Hit> Search(string query, string kind)
        {
            var hits = new List<Hit>();
            if (string.IsNullOrWhiteSpace(query))
            {
                foreach (Hit h in All)
                {
                    if (kind != "ALL" && h.Kind != kind) continue;
                    hits.Add(h);
                    if (hits.Count >= 80) break;
                }
                return hits;
            }
            string[] toks = query.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (Hit h in All)
            {
                if (kind != "ALL" && h.Kind != kind) continue;
                bool ok = true;
                foreach (string t in toks)
                    if (h.Hay.IndexOf(t) < 0) { ok = false; break; }
                if (!ok) continue;
                hits.Add(h);
                if (hits.Count >= 80) break;
            }
            return hits;
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
