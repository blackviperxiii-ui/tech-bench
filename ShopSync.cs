using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace TechBench
{
    /// <summary>LocalAppData shop-sync prefs: who this PC is, and the optional shared folder.</summary>
    public sealed class ShopSyncSettings
    {
        public string TechId = "";
        public string SyncFolder = "";
    }

    /// <summary>
    /// Two copies of the same shop file (or the same user-code key) disagree. The engine never
    /// picks a winner: the tech does.
    /// </summary>
    public sealed class ShopConflict
    {
        public string Kind = "file";
        public string RelativePath = "";
        public string LocalPath = "";
        public string RemotePath = "";
        public string Key = "";
        public string LocalSummary = "";
        public string RemoteSummary = "";
        public string Prefix = "";

        public override string ToString()
        {
            if (Kind == "code")
                return "CODE  " + Key + "  —  " + LocalSummary + "  vs  " + RemoteSummary;
            return Kind.ToUpperInvariant() + "  " + RelativePath;
        }
    }

    public sealed class ShopSyncResult
    {
        public int Pulled;
        public int Pushed;
        public int Conflicts;
        public int Skipped;
        public int Techs;
        public bool SharedTree;
        public string Status = "";
        public readonly List<ShopConflict> ConflictList = new List<ShopConflict>();
        public readonly List<string> Notes = new List<string>();

        public void AddConflict(ShopConflict c)
        {
            if (c == null) return;
            ConflictList.Add(c);
            Conflicts = ConflictList.Count;
        }
    }

    /// <summary>
    /// Two-way shop sync. Fits a copied 32-bit exe and a knowledge-base folder that may already
    /// live on OneDrive: no GitHub token, no extra installer.
    ///
    /// Shared KB tree (default): each tech writes only data\shop\{techId}\ so OneDrive is not
    /// last-write-wins on a single user-codes.json. The index merges every shard.
    ///
    /// Configured sync folder (USB / network / a second OneDrive folder): the same shop tree is
    /// copied both ways using content hashes. A file both sides changed is a conflict, never a
    /// silent overwrite of notes.
    /// </summary>
    public static class ShopSync
    {
        public const long MaxBytes = 50L * 1024 * 1024;
        public const string ShopRel = "data\\shop";
        public const string NotesRel = "data\\notes";
        public const string FilesRel = "data\\files";

        /// <summary>Tests point this at a temp folder so we do not touch LocalAppData.</summary>
        public static string SettingsFolderOverride;

        public static string Folder()
        {
            if (!string.IsNullOrEmpty(SettingsFolderOverride))
            {
                Directory.CreateDirectory(SettingsFolderOverride);
                return SettingsFolderOverride;
            }
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root)) root = Path.GetTempPath();
            string d = Path.Combine(root, "TechBench");
            Directory.CreateDirectory(d);
            return d;
        }

        public static string SettingsPath()
        {
            return Path.Combine(Folder(), "sync.json");
        }

        public static string DefaultTechId()
        {
            try
            {
                string u = Environment.UserName;
                if (!string.IsNullOrWhiteSpace(u)) return SanitizeTechId(u);
            }
            catch { }
            try
            {
                string m = Environment.MachineName;
                if (!string.IsNullOrWhiteSpace(m)) return SanitizeTechId(m);
            }
            catch { }
            return "tech";
        }

        public static string SanitizeTechId(string raw)
        {
            var sb = new StringBuilder();
            foreach (char c in raw ?? "")
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else if (c == ' ' || c == '.') sb.Append('-');
                if (sb.Length >= 40) break;
            }
            string s = sb.ToString().Trim('-', '_');
            return s.Length > 0 ? s : "tech";
        }

        public static ShopSyncSettings LoadSettings()
        {
            var s = new ShopSyncSettings();
            try
            {
                string path = SettingsPath();
                if (File.Exists(path))
                {
                    var ser = new JavaScriptSerializer();
                    var loaded = ser.Deserialize<ShopSyncSettings>(File.ReadAllText(path));
                    if (loaded != null) s = loaded;
                }
            }
            catch { s = new ShopSyncSettings(); }
            if (string.IsNullOrWhiteSpace(s.TechId)) s.TechId = DefaultTechId();
            else s.TechId = SanitizeTechId(s.TechId);
            if (string.IsNullOrWhiteSpace(s.SyncFolder))
                s.SyncFolder = ReadPathFile("sync-path.txt");
            return s;
        }

        public static void SaveSettings(ShopSyncSettings s)
        {
            if (s == null) s = new ShopSyncSettings();
            s.TechId = SanitizeTechId(s.TechId);
            var ser = new JavaScriptSerializer();
            File.WriteAllText(SettingsPath(), ser.Serialize(s), new UTF8Encoding(false));
        }

        static string ReadPathFile(string name)
        {
            try
            {
                string cfg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
                if (!File.Exists(cfg)) return "";
                foreach (string line in File.ReadAllLines(cfg))
                {
                    string t = line.Trim();
                    if (t.Length > 0 && !t.StartsWith("#")) return t;
                }
            }
            catch { }
            return "";
        }

        public static string ShopRoot(string kbRoot)
        {
            return Path.Combine(kbRoot ?? "", ShopRel);
        }

        public static string ShardDir(string kbRoot, string techId)
        {
            return Path.Combine(ShopRoot(kbRoot), SanitizeTechId(techId));
        }

        public static string ShardCodesPath(string kbRoot, string techId)
        {
            return Path.Combine(ShardDir(kbRoot, techId), "user-codes.json");
        }

        public static bool SamePath(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(a).TrimEnd('\\'),
                    Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static bool IsSharedTree(string kbRoot, ShopSyncSettings s)
        {
            if (s == null || string.IsNullOrWhiteSpace(s.SyncFolder)) return true;
            return SamePath(s.SyncFolder, kbRoot) || SamePath(s.SyncFolder, ShopRoot(kbRoot));
        }

        public static ShopSyncResult Run(string kbRoot)
        {
            return Run(kbRoot, LoadSettings());
        }

        public static ShopSyncResult Run(string kbRoot, ShopSyncSettings settings)
        {
            var result = new ShopSyncResult();
            if (string.IsNullOrWhiteSpace(kbRoot))
            {
                result.Status = "No knowledge base folder — nowhere to sync.";
                return result;
            }
            if (settings == null) settings = LoadSettings();
            settings.TechId = SanitizeTechId(settings.TechId);
            Directory.CreateDirectory(ShardDir(kbRoot, settings.TechId));
            ImportLegacyCodes(kbRoot, settings.TechId, result);

            result.SharedTree = IsSharedTree(kbRoot, settings);
            if (!result.SharedTree)
            {
                try
                {
                    string remote = settings.SyncFolder.Trim();
                    Directory.CreateDirectory(remote);
                    SyncState state = LoadState(kbRoot, remote);
                    TwoWay(Path.Combine(kbRoot, ShopRel), Path.Combine(remote, ShopRel), "shop", state, result);
                    TwoWay(Path.Combine(kbRoot, NotesRel), Path.Combine(remote, NotesRel), "notes", state, result);
                    TwoWay(Path.Combine(kbRoot, FilesRel), Path.Combine(remote, FilesRel), "files", state, result);
                    state.LastSyncUtc = DateTime.UtcNow.ToString("o");
                    SaveState(kbRoot, remote, state);
                }
                catch (Exception ex)
                {
                    result.Notes.Add("Sync folder: " + ex.Message);
                }
            }

            CollectCodeConflicts(kbRoot, result);
            result.Techs = CountTechs(kbRoot);
            result.Status = Describe(kbRoot, settings, result);
            return result;
        }

        public static string Describe(string kbRoot, ShopSyncSettings s, ShopSyncResult r)
        {
            if (s == null) s = LoadSettings();
            if (r == null) r = new ShopSyncResult();
            string where = r.SharedTree
                ? "sharing KB folder"
                : ("folder " + (s.SyncFolder ?? ""));
            string body = "Sync: " + where + "  ·  tech " + SanitizeTechId(s.TechId);
            if (r.Techs > 0) body += "  ·  " + r.Techs + " tech folder" + (r.Techs == 1 ? "" : "s");
            if (r.Pulled > 0) body += "  ·  pulled " + r.Pulled;
            if (r.Pushed > 0) body += "  ·  pushed " + r.Pushed;
            if (r.Conflicts > 0) body += "  ·  " + r.Conflicts + " conflict" + (r.Conflicts == 1 ? "" : "s") + " — Shop → Sync";
            else if (r.SharedTree && r.Pulled == 0 && r.Pushed == 0)
                body += "  ·  notes and files in data\\shop stay in this tree";
            if (r.Skipped > 0) body += "  ·  skipped " + r.Skipped;
            if (r.Notes.Count > 0) body += "  ·  " + r.Notes[0];
            return body;
        }

        static int CountTechs(string kbRoot)
        {
            int n = 0;
            try
            {
                string shop = ShopRoot(kbRoot);
                if (!Directory.Exists(shop)) return 0;
                foreach (string d in Directory.GetDirectories(shop))
                {
                    string name = Path.GetFileName(d);
                    if (IsMetaFolder(name)) continue;
                    n++;
                }
            }
            catch { }
            return n;
        }

        static bool IsMetaFolder(string name)
        {
            return string.Equals(name, "_conflicts", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "_resolved", StringComparison.OrdinalIgnoreCase);
        }

        static void ImportLegacyCodes(string kbRoot, string techId, ShopSyncResult result)
        {
            string shard = ShardCodesPath(kbRoot, techId);
            string legacy = UserCodes.PathFor(kbRoot);
            try
            {
                if (!File.Exists(legacy)) return;
                if (File.Exists(shard)) return;
                List<UserCode> codes = UserCodes.LoadFrom(legacy);
                if (codes.Count == 0) return;
                UserCodes.SaveTo(shard, codes);
                if (result != null) result.Notes.Add("Copied data\\user-codes.json into your shop folder.");
            }
            catch (Exception ex)
            {
                if (result != null) result.Notes.Add("Legacy codes: " + ex.Message);
            }
        }

        public static List<string> TechIds(string kbRoot)
        {
            var list = new List<string>();
            try
            {
                string shop = ShopRoot(kbRoot);
                if (!Directory.Exists(shop)) return list;
                foreach (string d in Directory.GetDirectories(shop))
                {
                    string name = Path.GetFileName(d);
                    if (IsMetaFolder(name)) continue;
                    list.Add(name);
                }
                list.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            return list;
        }

        public static List<UserCode> LoadAllCodes(string kbRoot)
        {
            var all = new List<UserCode>();
            var seenFiles = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            AddCodes(all, UserCodes.PathFor(kbRoot), "", seenFiles);
            foreach (string tech in TechIds(kbRoot))
                AddCodes(all, ShardCodesPath(kbRoot, tech), tech, seenFiles);
            AddCodes(all, Path.Combine(ShopRoot(kbRoot), "_resolved", "user-codes.json"), "resolved", seenFiles);
            return all;
        }

        static void AddCodes(List<UserCode> into, string path, string author, Dictionary<string, bool> seen)
        {
            try
            {
                string full = Path.GetFullPath(path);
                if (seen.ContainsKey(full)) return;
                seen[full] = true;
            }
            catch { }
            foreach (UserCode c in UserCodes.LoadFrom(path))
            {
                if (author.Length > 0 && string.IsNullOrEmpty(c.Author)) c.Author = author;
                into.Add(c);
            }
        }

        public static void SaveUserCode(string kbRoot, string techId, UserCode entry)
        {
            techId = SanitizeTechId(techId);
            Directory.CreateDirectory(ShardDir(kbRoot, techId));
            string path = ShardCodesPath(kbRoot, techId);
            List<UserCode> mine = UserCodes.LoadFrom(path);
            UserCodes.Upsert(mine, entry);
            UserCodes.SaveTo(path, mine);
        }

        public static string SaveNote(string kbRoot, string techId, string title, string body)
        {
            techId = SanitizeTechId(techId);
            string dir = Path.Combine(ShardDir(kbRoot, techId), "notes");
            Directory.CreateDirectory(dir);
            string slug = SanitizeTechId(title);
            if (string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(body))
            {
                string first = "";
                foreach (string raw in body.Replace("\r\n", "\n").Split('\n'))
                {
                    if (raw.Trim().Length > 0) { first = raw.Trim(); break; }
                }
                if (first.Length > 0) slug = SanitizeTechId(first);
            }
            if (slug == "tech") slug = "note";
            string name = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "-" + slug + ".txt";
            string path = Path.Combine(dir, name);
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(title)) sb.AppendLine(title.Trim());
            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(body)) sb.AppendLine();
            sb.Append(body ?? "");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }

        public static string AddFile(string kbRoot, string techId, string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("No file to add.", sourcePath);
            techId = SanitizeTechId(techId);
            string dir = Path.Combine(ShardDir(kbRoot, techId), "files");
            Directory.CreateDirectory(dir);
            string name = Path.GetFileName(sourcePath);
            string dest = Path.Combine(dir, name);
            if (File.Exists(dest) && HashFile(dest) != HashFile(sourcePath))
            {
                string stem = Path.GetFileNameWithoutExtension(name);
                string ext = Path.GetExtension(name);
                dest = Path.Combine(dir, stem + "-" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ext);
            }
            File.Copy(sourcePath, dest, false);
            return dest;
        }

        public static void PublishHistory(string kbRoot, string techId, string dtcCsvPath)
        {
            if (string.IsNullOrWhiteSpace(kbRoot) || string.IsNullOrWhiteSpace(dtcCsvPath)) return;
            if (!File.Exists(dtcCsvPath)) return;
            techId = SanitizeTechId(techId);
            string dir = Path.Combine(ShardDir(kbRoot, techId), "history");
            Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, Path.GetFileName(dtcCsvPath));
            File.Copy(dtcCsvPath, dest, true);
        }

        public static int ImportHistory(string kbRoot, string sessionFolder)
        {
            if (string.IsNullOrWhiteSpace(kbRoot) || string.IsNullOrWhiteSpace(sessionFolder)) return 0;
            Directory.CreateDirectory(sessionFolder);
            int n = 0;
            foreach (string tech in TechIds(kbRoot))
            {
                string dir = Path.Combine(ShardDir(kbRoot, tech), "history");
                if (!Directory.Exists(dir)) continue;
                foreach (string src in Directory.GetFiles(dir, "*_dtcs.csv"))
                {
                    string dest = Path.Combine(sessionFolder, Path.GetFileName(src));
                    try
                    {
                        if (File.Exists(dest))
                        {
                            if (HashFile(dest) == HashFile(src)) continue;
                            continue; // same name, different bytes: leave the local session file alone
                        }
                        File.Copy(src, dest, false);
                        n++;
                    }
                    catch { }
                }
            }
            return n;
        }

        public static Dictionary<string, bool> KeepBothKeys(string kbRoot)
        {
            var set = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = Path.Combine(ShopRoot(kbRoot), "_resolved", "keep-both.txt");
                if (!File.Exists(path)) return set;
                foreach (string line in File.ReadAllLines(path))
                {
                    string t = line.Trim();
                    if (t.Length > 0 && !t.StartsWith("#")) set[t] = true;
                }
            }
            catch { }
            return set;
        }

        public static Dictionary<string, bool> ResolvedKeys(string kbRoot)
        {
            var set = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (UserCode c in UserCodes.LoadFrom(Path.Combine(ShopRoot(kbRoot), "_resolved", "user-codes.json")))
                set[UserCodes.KeyOf(c)] = true;
            return set;
        }

        public static void CollectCodeConflicts(string kbRoot, ShopSyncResult result)
        {
            if (result == null) return;
            Dictionary<string, bool> keepBoth = KeepBothKeys(kbRoot);
            Dictionary<string, bool> resolved = ResolvedKeys(kbRoot);
            var byKey = new Dictionary<string, List<UserCode>>(StringComparer.OrdinalIgnoreCase);
            foreach (string tech in TechIds(kbRoot))
            {
                foreach (UserCode c in UserCodes.LoadFrom(ShardCodesPath(kbRoot, tech)))
                {
                    c.Author = tech;
                    string k = UserCodes.KeyOf(c);
                    if (!byKey.ContainsKey(k)) byKey[k] = new List<UserCode>();
                    byKey[k].Add(c);
                }
            }
            foreach (KeyValuePair<string, List<UserCode>> kv in byKey)
            {
                if (keepBoth.ContainsKey(kv.Key) || resolved.ContainsKey(kv.Key)) continue;
                if (kv.Value.Count < 2) continue;
                string fp0 = UserCodes.Fingerprint(kv.Value[0]);
                UserCode other = null;
                for (int i = 1; i < kv.Value.Count; i++)
                {
                    if (UserCodes.Fingerprint(kv.Value[i]) != fp0) { other = kv.Value[i]; break; }
                }
                if (other == null) continue;
                UserCode a = kv.Value[0];
                result.AddConflict(new ShopConflict
                {
                    Kind = "code",
                    Key = kv.Key,
                    RelativePath = "user-codes.json",
                    LocalPath = ShardCodesPath(kbRoot, a.Author),
                    RemotePath = ShardCodesPath(kbRoot, other.Author),
                    LocalSummary = a.Author + ": " + a.Title,
                    RemoteSummary = other.Author + ": " + other.Title,
                    Prefix = "shop"
                });
            }
        }

        public static void Resolve(string kbRoot, ShopConflict c, string choice)
        {
            if (c == null) return;
            choice = (choice ?? "").Trim().ToLowerInvariant();
            if (c.Kind == "code")
            {
                ResolveCode(kbRoot, c, choice);
                return;
            }
            if (choice == "local")
            {
                if (File.Exists(c.LocalPath)) CopyFile(c.LocalPath, c.RemotePath);
            }
            else if (choice == "remote")
            {
                if (File.Exists(c.RemotePath)) CopyFile(c.RemotePath, c.LocalPath);
            }
            else if (choice == "both")
            {
                KeepBothFiles(c);
            }
            else return;

            ShopSyncSettings s = LoadSettings();
            if (!IsSharedTree(kbRoot, s) && !string.IsNullOrWhiteSpace(s.SyncFolder))
            {
                SyncState state = LoadState(kbRoot, s.SyncFolder);
                string key = (c.Prefix ?? "shop") + "/" + RelKey(c.RelativePath);
                string h = File.Exists(c.LocalPath) ? HashFile(c.LocalPath) : "";
                string rh = File.Exists(c.RemotePath) ? HashFile(c.RemotePath) : h;
                if (choice == "local") state.Set(key, h, h);
                else if (choice == "remote") state.Set(key, rh, rh);
                SaveState(kbRoot, s.SyncFolder, state);
            }
        }

        static void ResolveCode(string kbRoot, ShopConflict c, string choice)
        {
            string dir = Path.Combine(ShopRoot(kbRoot), "_resolved");
            Directory.CreateDirectory(dir);
            if (choice == "both")
            {
                string path = Path.Combine(dir, "keep-both.txt");
                var lines = new List<string>();
                if (File.Exists(path)) lines.AddRange(File.ReadAllLines(path));
                if (!ContainsLine(lines, c.Key)) lines.Add(c.Key);
                File.WriteAllLines(path, lines.ToArray());
                return;
            }
            UserCode pick = null;
            if (choice == "local")
                pick = FindCode(c.LocalPath, c.Key);
            else if (choice == "remote")
                pick = FindCode(c.RemotePath, c.Key);
            if (pick == null) return;
            string resolved = Path.Combine(dir, "user-codes.json");
            List<UserCode> list = UserCodes.LoadFrom(resolved);
            UserCodes.Upsert(list, pick);
            UserCodes.SaveTo(resolved, list);
        }

        static bool ContainsLine(List<string> lines, string key)
        {
            foreach (string l in lines)
                if (string.Equals(l.Trim(), key, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static UserCode FindCode(string path, string key)
        {
            foreach (UserCode c in UserCodes.LoadFrom(path))
                if (UserCodes.KeyOf(c) == key) return c;
            return null;
        }

        static void KeepBothFiles(ShopConflict c)
        {
            if (!File.Exists(c.RemotePath) || string.IsNullOrEmpty(c.LocalPath)) return;
            string dir = Path.GetDirectoryName(c.LocalPath);
            Directory.CreateDirectory(dir);
            string stem = Path.GetFileNameWithoutExtension(c.LocalPath);
            string ext = Path.GetExtension(c.LocalPath);
            string extra = Path.Combine(dir, stem + " (from other tech)" + ext);
            int n = 2;
            while (File.Exists(extra))
            {
                extra = Path.Combine(dir, stem + " (from other tech " + n + ")" + ext);
                n++;
            }
            CopyFile(c.RemotePath, extra);
        }

        static void TwoWay(string localRoot, string remoteRoot, string prefix, SyncState state, ShopSyncResult result)
        {
            Directory.CreateDirectory(localRoot);
            Directory.CreateDirectory(remoteRoot);
            var names = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (string rel in ListRel(localRoot)) names[rel] = true;
            foreach (string rel in ListRel(remoteRoot)) names[rel] = true;
            foreach (string rel in names.Keys)
            {
                string lp = Path.Combine(localRoot, rel);
                string rp = Path.Combine(remoteRoot, rel);
                bool lExists = File.Exists(lp);
                bool rExists = File.Exists(rp);
                string key = prefix + "/" + RelKey(rel);
                string lastL, lastR;
                state.TryGet(key, out lastL, out lastR);

                if (lExists && rExists)
                {
                    string lh, rh;
                    if (!TryHash(lp, result, out lh) || !TryHash(rp, result, out rh)) continue;
                    if (lh == rh)
                    {
                        state.Set(key, lh, rh);
                        continue;
                    }
                    bool localChanged = lastL == null || lh != lastL;
                    bool remoteChanged = lastR == null || rh != lastR;
                    if (localChanged && remoteChanged)
                    {
                        result.AddConflict(new ShopConflict
                        {
                            Kind = KindOf(rel),
                            RelativePath = rel,
                            LocalPath = lp,
                            RemotePath = rp,
                            LocalSummary = "this PC",
                            RemoteSummary = "sync folder",
                            Prefix = prefix
                        });
                        continue;
                    }
                    if (localChanged && !remoteChanged)
                    {
                        CopyFile(lp, rp);
                        state.Set(key, lh, lh);
                        result.Pushed++;
                    }
                    else if (!localChanged && remoteChanged)
                    {
                        CopyFile(rp, lp);
                        state.Set(key, rh, rh);
                        result.Pulled++;
                    }
                    else
                    {
                        result.AddConflict(new ShopConflict
                        {
                            Kind = KindOf(rel),
                            RelativePath = rel,
                            LocalPath = lp,
                            RemotePath = rp,
                            LocalSummary = "this PC",
                            RemoteSummary = "sync folder",
                            Prefix = prefix
                        });
                    }
                }
                else if (lExists)
                {
                    string lh;
                    if (!TryHash(lp, result, out lh)) continue;
                    CopyFile(lp, rp);
                    state.Set(key, lh, lh);
                    result.Pushed++;
                }
                else if (rExists)
                {
                    string rh;
                    if (!TryHash(rp, result, out rh)) continue;
                    CopyFile(rp, lp);
                    state.Set(key, rh, rh);
                    result.Pulled++;
                }
            }
        }

        static string KindOf(string rel)
        {
            string low = (rel ?? "").ToLowerInvariant();
            if (low.IndexOf("\\notes\\", StringComparison.Ordinal) >= 0 || low.StartsWith("notes\\", StringComparison.Ordinal))
                return "note";
            if (low.EndsWith("user-codes.json", StringComparison.OrdinalIgnoreCase)) return "code-file";
            if (low.EndsWith("j1939-names.json", StringComparison.OrdinalIgnoreCase)) return "names";
            return "file";
        }

        static bool TryHash(string path, ShopSyncResult result, out string hash)
        {
            hash = "";
            try
            {
                var info = new FileInfo(path);
                if (info.Length > MaxBytes)
                {
                    result.Skipped++;
                    result.Notes.Add("Skipped (over 50 MB): " + Path.GetFileName(path));
                    return false;
                }
                hash = HashFile(path);
                return true;
            }
            catch (Exception ex)
            {
                result.Skipped++;
                result.Notes.Add(Path.GetFileName(path) + ": " + ex.Message);
                return false;
            }
        }

        static IEnumerable<string> ListRel(string root)
        {
            var list = new List<string>();
            if (!Directory.Exists(root)) return list;
            foreach (string full in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                if (Ignore(root, full)) continue;
                string rel = full.Substring(root.Length).TrimStart('\\', '/');
                if (rel.Length > 0) list.Add(rel);
            }
            return list;
        }

        static bool Ignore(string root, string full)
        {
            string name = Path.GetFileName(full);
            if (name.StartsWith("~$") || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)
                || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                return true;
            string rel = full.Substring(root.Length);
            if (rel.IndexOf("\\_conflicts\\", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static string RelKey(string rel)
        {
            return (rel ?? "").Replace('/', '\\');
        }

        static void CopyFile(string src, string dest)
        {
            string dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = dest + ".tmp";
            File.Copy(src, tmp, true);
            if (File.Exists(dest)) File.Copy(tmp, dest, true);
            else File.Move(tmp, dest);
            try { if (File.Exists(tmp)) File.Delete(tmp); }
            catch { }
        }

        public static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(fs);
                return Hex(hash);
            }
        }

        public static string HashText(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                return Hex(hash);
            }
        }

        static string Hex(byte[] hash)
        {
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        static string StatePath(string kbRoot, string syncFolder)
        {
            string id = HashText((kbRoot ?? "") + "\n" + (syncFolder ?? "")).Substring(0, 12);
            return Path.Combine(Folder(), "sync-state-" + id + ".json");
        }

        static SyncState LoadState(string kbRoot, string syncFolder)
        {
            var st = new SyncState();
            try
            {
                string path = StatePath(kbRoot, syncFolder);
                if (!File.Exists(path)) return st;
                var ser = new JavaScriptSerializer();
                var d = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                if (d == null) return st;
                object v;
                if (d.TryGetValue("lastSyncUtc", out v) && v != null) st.LastSyncUtc = Convert.ToString(v);
                ReadMap(d, "local", st.Local);
                ReadMap(d, "remote", st.Remote);
            }
            catch { }
            return st;
        }

        static void ReadMap(Dictionary<string, object> d, string key, Dictionary<string, string> into)
        {
            object v;
            if (!d.TryGetValue(key, out v)) return;
            var map = v as Dictionary<string, object>;
            if (map == null) return;
            foreach (KeyValuePair<string, object> kv in map)
                into[kv.Key] = kv.Value == null ? "" : Convert.ToString(kv.Value);
        }

        static void SaveState(string kbRoot, string syncFolder, SyncState st)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"lastSyncUtc\": " + UserCodes.Quote(st.LastSyncUtc) + ",");
            WriteMap(sb, "local", st.Local, true);
            WriteMap(sb, "remote", st.Remote, false);
            sb.AppendLine("}");
            File.WriteAllText(StatePath(kbRoot, syncFolder), sb.ToString(), new UTF8Encoding(false));
        }

        static void WriteMap(StringBuilder sb, string name, Dictionary<string, string> map, bool comma)
        {
            sb.AppendLine("  \"" + name + "\": {");
            var keys = new List<string>(map.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < keys.Count; i++)
            {
                sb.Append("    " + UserCodes.Quote(keys[i]) + ": " + UserCodes.Quote(map[keys[i]]));
                sb.AppendLine(i < keys.Count - 1 ? "," : "");
            }
            sb.AppendLine("  }" + (comma ? "," : ""));
        }

        sealed class SyncState
        {
            public string LastSyncUtc = "";
            public readonly Dictionary<string, string> Local = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, string> Remote = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public void TryGet(string key, out string local, out string remote)
            {
                if (!Local.TryGetValue(key, out local)) local = null;
                if (!Remote.TryGetValue(key, out remote)) remote = null;
            }

            public void Set(string key, string local, string remote)
            {
                Local[key] = local ?? "";
                Remote[key] = remote ?? "";
            }
        }
    }
}
