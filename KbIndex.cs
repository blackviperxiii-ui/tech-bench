using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
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

        /// <summary>
        /// JSON shipped inside TechBench.exe and copied beside the exe by the installer.
        /// Paths are relative to the knowledge-base root. Resource names are
        /// TechBench.BundledKb. plus these paths with slashes turned into dots.
        /// </summary>
        public static readonly string[] BakedRelPaths = new string[]
        {
            "data/kb.json",
            "data/passwords/ifix-passwords.json",
            "data/usb-manuals.json",
            "data/rental-portable-filters-oil.json",
            "data/rental-equipment-info.json"
        };

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
        /// Shipped database next to the exe, then a TECHBENCH_KB variable or kb-path.txt, then the old
        /// Documents/OneDrive folder. If none of those have data\kb.json, the copy embedded in the exe
        /// is written under %LocalAppData%\TechBench\air-compressor-kb.
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

            // The installer drops air-compressor-kb next to TechBench.exe. Prefer that over a
            // leftover Documents/OneDrive folder so a normal install does not need Drive.
            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                candidates.Add(Path.Combine(exeDir, "air-compressor-kb"));
                DirectoryInfo up = Directory.GetParent(exeDir.TrimEnd(Path.DirectorySeparatorChar));
                if (up != null) candidates.Add(Path.Combine(up.FullName, "air-compressor-kb"));
            }
            catch { }

            AddSpecial(candidates, Environment.SpecialFolder.MyDocuments, "air-compressor-kb");
            AddSpecial(candidates, Environment.SpecialFolder.UserProfile, "Documents", "air-compressor-kb");
            // README says the KB may live under OneDrive Documents, which MyDocuments only returns when
            // Known Folder Move is on.
            AddSpecial(candidates, Environment.SpecialFolder.UserProfile, "OneDrive", "Documents", "air-compressor-kb");
            AddSpecial(candidates, Environment.SpecialFolder.UserProfile, "air-compressor-kb");

            foreach (string c in candidates)
            {
                try { if (File.Exists(Path.Combine(c, "data", "kb.json"))) { found = true; return c; } }
                catch { }
            }

            // Standalone exe (updater swap, or a copy with no folder beside it): write the
            // embedded JSON under LocalAppData. Shop notes and user-codes in that folder are
            // left alone; only the shipped files are rewritten.
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(local))
                {
                    string dest = Path.Combine(local, "TechBench", "air-compressor-kb");
                    if (TryMaterializeBaked(dest)) { found = true; return dest; }
                }
            }
            catch { }

            found = false;
            // Nothing found: hand back the most likely location so the error message names a path on
            // this machine rather than whoever's PC the default was written on.
            return candidates.Count > 0 ? candidates[0] : "air-compressor-kb";
        }

        public static string BakedResourceName(string rel)
        {
            return "TechBench.BundledKb." + (rel ?? "").Replace('\\', '.').Replace('/', '.');
        }

        /// <summary>
        /// Writes the embedded field database into destRoot. Returns false when this assembly
        /// was built without those resources (the offline self-test exe, unless it embeds them).
        /// </summary>
        public static bool TryMaterializeBaked(string destRoot)
        {
            if (string.IsNullOrWhiteSpace(destRoot)) return false;
            Assembly asm = Assembly.GetExecutingAssembly();
            int wrote = 0;
            foreach (string rel in BakedRelPaths)
            {
                Stream src = asm.GetManifestResourceStream(BakedResourceName(rel));
                if (src == null) return false;
                using (src)
                {
                    string outPath = Path.Combine(destRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                    string dir = Path.GetDirectoryName(outPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    using (FileStream fs = File.Create(outPath))
                        src.CopyTo(fs);
                }
                wrote++;
            }
            return wrote == BakedRelPaths.Length
                && File.Exists(Path.Combine(destRoot, "data", "kb.json"));
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
            J1939Reader.Names.Reset();
            var spn = new Dictionary<int, string>();
            var pgn = new Dictionary<int, string>();
            var sa = new Dictionary<int, string>();

            // Each file is loaded on its own so one bad JSON file costs that section only, instead of
            // leaving the tech with a completely empty index and no idea why.
            int nCode = Try("kb.json", delegate { return LoadCodes(ser, Path.Combine(root, "data", "kb.json"), "", null, null, null); });
            int nPwd = Try("ifix-passwords.json", delegate { return LoadPasswords(ser, Path.Combine(root, "data", "passwords", "ifix-passwords.json")); });
            int nMan = Try("usb-manuals.json", delegate { return LoadManuals(ser, Path.Combine(root, "data", "usb-manuals.json")); });
            int nFil = Try("rental-portable-filters-oil.json", delegate { return LoadFilters(ser, Path.Combine(root, "data", "rental-portable-filters-oil.json")); });
            int nEq = Try("rental-equipment-info.json", delegate { return LoadEquipment(ser, Path.Combine(root, "data", "rental-equipment-info.json")); });
            int nNames = Try("j1939-names.json", delegate { return MergeNames(ser, Path.Combine(root, "data", "j1939-names.json"), spn, pgn, sa, false); });
            int nNotes = Try("notes", delegate { return LoadNoteDir(Path.Combine(root, "data", "notes"), ""); });
            int nFiles = Try("files", delegate { return LoadFileDir(Path.Combine(root, "data", "files"), ""); });

            Dictionary<string, bool> skipCodes = ShopSync.ResolvedKeys(root);
            Dictionary<string, bool> keepBoth = ShopSync.KeepBothKeys(root);
            var seenUser = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            nCode += Try("_resolved\\user-codes.json", delegate
            {
                return LoadCodes(ser, Path.Combine(ShopSync.ShopRoot(root), "_resolved", "user-codes.json"), "resolved", null, null, seenUser);
            });
            foreach (string tech in ShopSync.TechIds(root))
            {
                string shard = ShopSync.ShardDir(root, tech);
                nCode += Try(tech + "\\user-codes.json", delegate
                {
                    return LoadCodes(ser, Path.Combine(shard, "user-codes.json"), tech, skipCodes, keepBoth, seenUser);
                });
                nNames += Try(tech + "\\j1939-names.json", delegate
                {
                    return MergeNames(ser, Path.Combine(shard, "j1939-names.json"), spn, pgn, sa, false);
                });
                nNotes += Try(tech + "\\notes", delegate { return LoadNoteDir(Path.Combine(shard, "notes"), tech); });
                nFiles += Try(tech + "\\files", delegate { return LoadFileDir(Path.Combine(shard, "files"), tech); });
            }
            nCode += Try("user-codes.json", delegate
            {
                return LoadCodes(ser, Path.Combine(root, "data", "user-codes.json"), "", null, null, seenUser);
            });
            nNames += Try("_resolved\\j1939-names.json", delegate
            {
                return MergeNames(ser, Path.Combine(ShopSync.ShopRoot(root), "_resolved", "j1939-names.json"), spn, pgn, sa, true);
            });
            J1939Reader.Names.Load(spn, pgn, sa);

            Status = string.Format("Codes {0}  ·  Passwords {1}  ·  Manuals {2}  ·  Filters {3}  ·  Equipment {4}",
                nCode, nPwd, nMan, nFil, nEq);
            if (nNames > 0) Status += "  ·  J1939 names " + nNames;
            if (nNotes > 0) Status += "  ·  Notes " + nNotes;
            if (nFiles > 0) Status += "  ·  Files " + nFiles;
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

        int LoadCodes(JavaScriptSerializer ser, string path, string author, Dictionary<string, bool> skip, Dictionary<string, bool> keepBoth, Dictionary<string, bool> seen)
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
                string key = brand.Trim().ToLowerInvariant() + "\u0001" + code.Trim().ToLowerInvariant();
                if (skip != null && skip.ContainsKey(key) && (keepBoth == null || !keepBoth.ContainsKey(key)))
                    continue;
                if (seen != null && seen.ContainsKey(key) && (keepBoth == null || !keepBoth.ContainsKey(key)))
                    continue;
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
                string sub = sev + "  ·  " + S(Get(d, "controller_id")) + "  ·  " + S(Get(d, "confidence"));
                if (!string.IsNullOrEmpty(author)) sub += "  ·  " + author;
                Add(new Hit
                {
                    Kind = "CODE",
                    Title = brand + "  " + code + "  —  " + title,
                    Subtitle = sub,
                    Body = body.ToString(),
                    Hay = (brand + " " + code + " " + title + " " + desc + " " + S(Get(d, "controller_id")) + " " + author + " " + JoinArr(Get(d, "tags"))).ToLowerInvariant()
                }, code);
                n++;
                if (seen != null) seen[key] = true;
            }
            return n;
        }

        int LoadNoteDir(string dir, string author)
        {
            if (!Directory.Exists(dir)) return 0;
            int n = 0;
            foreach (string f in Directory.GetFiles(dir))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".txt" && ext != ".md") continue;
                string body;
                try { body = File.ReadAllText(f); }
                catch { continue; }
                string title = Path.GetFileNameWithoutExtension(f);
                string first = FirstLine(body);
                if (first.Length > 0) title = first;
                string sub = "Tech note";
                if (!string.IsNullOrEmpty(author)) sub += "  ·  " + author;
                Add(new Hit
                {
                    Kind = "NOTE",
                    Title = title,
                    Subtitle = sub,
                    Body = body,
                    Path = f,
                    Hay = (title + " " + author + " " + Path.GetFileName(f) + " " + body).ToLowerInvariant()
                }, title);
                n++;
            }
            return n;
        }

        int LoadFileDir(string dir, string author)
        {
            if (!Directory.Exists(dir)) return 0;
            int n = 0;
            foreach (string f in Directory.GetFiles(dir))
            {
                string name = Path.GetFileName(f);
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".tmp" || ext == ".bak" || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                    continue;
                string sub = "Shop file";
                if (!string.IsNullOrEmpty(author)) sub += "  ·  " + author;
                Add(new Hit
                {
                    Kind = "FILE",
                    Title = name,
                    Subtitle = sub,
                    Body = f,
                    Path = f,
                    Hay = (name + " " + author).ToLowerInvariant()
                }, name);
                n++;
            }
            return n;
        }

        static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string t = raw.Trim();
                if (t.Length > 0) return t;
            }
            return "";
        }

        /// <summary>
        /// Optional data\j1939-names.json extends the built-in SPN/PGN/SA labels, so a new SPN gets a
        /// name without a rebuild. Keys may be decimal or 0x-prefixed hex.
        /// </summary>
        int MergeNames(JavaScriptSerializer ser, string path,
            Dictionary<int, string> spn, Dictionary<int, string> pgn, Dictionary<int, string> sa, bool overwrite)
        {
            var rootObj = ReadRoot(ser, path);
            if (rootObj == null) return 0;
            int n = 0;
            n += MergeNameMap(NameMap(Get(rootObj, "spn")), spn, overwrite);
            n += MergeNameMap(NameMap(Get(rootObj, "pgn")), pgn, overwrite);
            n += MergeNameMap(NameMap(Get(rootObj, "sa")), sa, overwrite);
            return n;
        }

        static int MergeNameMap(Dictionary<int, string> src, Dictionary<int, string> dest, bool overwrite)
        {
            int n = 0;
            foreach (KeyValuePair<int, string> kv in src)
            {
                string existing;
                if (dest.TryGetValue(kv.Key, out existing))
                {
                    if (!overwrite) continue;
                    if (existing == kv.Value) continue;
                    dest[kv.Key] = kv.Value;
                    n++;
                    continue;
                }
                dest[kv.Key] = kv.Value;
                n++;
            }
            return n;
        }

        static Dictionary<int, string> NameMap(object o)
        {
            var map = new Dictionary<int, string>();
            var d = o as Dictionary<string, object>;
            if (d == null) return map;
            foreach (KeyValuePair<string, object> kv in d)
            {
                int key;
                if (!ParseKey(kv.Key, out key)) continue;
                string v = kv.Value == null ? "" : Convert.ToString(kv.Value).Trim();
                if (v.Length > 0) map[key] = v;
            }
            return map;
        }

        static bool ParseKey(string s, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// KB entries that look relevant to a live J1939 fault, so the INLINE 7 tab can show shop
        /// knowledge next to the built-in CodeBook text.
        /// </summary>
        public List<Hit> SpnHits(int spn, int fmi, int max)
        {
            var seen = new Dictionary<string, bool>();
            var hits = new List<Hit>();
            string spnName = J1939Reader.Names.Spn(spn);
            string[] queries = spnName.Length > 0
                ? new[] { "spn " + spn, spn.ToString(CultureInfo.InvariantCulture), spnName }
                : new[] { "spn " + spn, spn.ToString(CultureInfo.InvariantCulture) };
            foreach (string q in queries)
            {
                int total;
                foreach (Hit h in Search(q, "ALL", max, out total))
                {
                    string key = h.Kind + "\u0001" + h.Title;
                    if (seen.ContainsKey(key)) continue;
                    seen[key] = true;
                    hits.Add(h);
                    if (hits.Count >= max) return hits;
                }
            }
            return hits;
        }

        public string SpnText(int spn, int fmi)
        {
            List<Hit> hits = SpnHits(spn, fmi, 3);
            if (hits.Count == 0) return null;
            var sb = new StringBuilder();
            foreach (Hit h in hits)
            {
                sb.AppendLine("[" + h.Kind + "] " + h.Title);
                if (h.Subtitle.Trim().Length > 0) sb.AppendLine(h.Subtitle);
                string body = (h.Body ?? "").Trim();
                if (body.Length > 900) body = body.Substring(0, 900) + "…";
                if (body.Length > 0) sb.AppendLine(body);
                sb.AppendLine();
            }
            return sb.ToString();
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
