using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace TechBench
{
    /// <summary>
    /// File-backed WO packets. LocalAppData plus the KB shop tree so two-way shop sync
    /// (data\shop\{tech}\…) can carry them without this branch owning that engine.
    /// </summary>
    public static class WorkOrderStore
    {
        public const string ShopRel = "data\\shop";
        public const string SharedTech = "_shared";
        public const string WoRel = "work-orders";

        public static string FolderOverride;
        public static string ExeDirOverride;
        public static readonly List<ShopConflict> Conflicts = new List<ShopConflict>();

        /// <summary>SelfTest: skip File.Replace and use the copy/delete/move fallback.</summary>
        internal static bool ForceCopyFallbackForTest;

        public static string LocalRoot()
        {
            if (!string.IsNullOrEmpty(FolderOverride))
            {
                Directory.CreateDirectory(FolderOverride);
                return FolderOverride;
            }
            return Path.Combine(IdSettings.Folder(), WoRel);
        }

        public static string SafeKey(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var sb = new StringBuilder();
            foreach (char c in raw.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else sb.Append('-');
            }
            string s = sb.ToString().Trim('-');
            if (s.Length > 60) s = s.Substring(0, 60);
            return s;
        }

        public static string PacketDir(string root, string key)
        {
            return Path.Combine(root, SafeKey(key));
        }

        public static string ShopTechDir(string kbRoot, string techId)
        {
            return Path.Combine(kbRoot ?? "", ShopRel, IdSettings.Sanitize(techId), WoRel);
        }

        public static string ShopSharedDir(string kbRoot)
        {
            return Path.Combine(kbRoot ?? "", ShopRel, SharedTech, WoRel);
        }

        public static void Save(WorkOrder wo, IdSettings settings, string kbRoot)
        {
            if (wo == null) return;
            if (string.IsNullOrWhiteSpace(wo.Number)) throw new InvalidOperationException("Work order number is required.");
            wo.Touch();
            string key = SafeKey(wo.Key());
            string local = PacketDir(LocalRoot(), key);
            WritePacket(local, wo);
            wo.Folder = local;

            if (settings == null) settings = new IdSettings();
            string tech = IdSettings.Sanitize(settings.TechId);
            if (!string.IsNullOrEmpty(kbRoot) && Directory.Exists(kbRoot))
                CopyPacket(local, PacketDir(ShopTechDir(kbRoot, tech), key));
        }

        public static WorkOrder Load(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !File.Exists(Path.Combine(folder, "wo.json"))) return null;
            try
            {
                var ser = new JavaScriptSerializer();
                var wo = ser.Deserialize<WorkOrder>(File.ReadAllText(Path.Combine(folder, "wo.json")));
                if (wo == null) return null;
                wo.Folder = folder;
                if (wo.Media == null) wo.Media = new List<string>();
                if (wo.Clock == null) wo.Clock = new List<WorkOrderClock>();
                string notes = Path.Combine(folder, "notes.txt");
                if (File.Exists(notes)) wo.Notes = File.ReadAllText(notes);
                string report = Path.Combine(folder, "report.txt");
                if (File.Exists(report)) wo.ReportText = File.ReadAllText(report);
                string media = Path.Combine(folder, "media");
                if (Directory.Exists(media))
                {
                    foreach (string f in Directory.GetFiles(media))
                    {
                        if (IsIgnoredPacketFile(f)) continue;
                        string name = Path.GetFileName(f);
                        if (!ContainsName(wo.Media, name)) wo.Media.Add(name);
                    }
                }
                return wo;
            }
            catch
            {
                return null;
            }
        }

        public static List<WorkOrder> ListAll(IdSettings settings, string kbRoot)
        {
            Conflicts.Clear();
            var byKey = new Dictionary<string, WorkOrder>(StringComparer.OrdinalIgnoreCase);
            if (settings == null) settings = new IdSettings();
            string tech = IdSettings.Sanitize(settings.TechId);

            Absorb(byKey, LocalRoot(), "file", kbRoot);
            if (!string.IsNullOrEmpty(kbRoot) && Directory.Exists(kbRoot))
            {
                Absorb(byKey, ShopTechDir(kbRoot, tech), "file", kbRoot);
                AbsorbShared(byKey, ShopSharedDir(kbRoot), kbRoot);
            }
            if (!string.IsNullOrWhiteSpace(settings.ShareFolder) && Directory.Exists(settings.ShareFolder))
            {
                AbsorbShared(byKey, Path.Combine(settings.ShareFolder, WoRel), kbRoot);
                AbsorbShared(byKey, Path.Combine(settings.ShareFolder, ShopRel, SharedTech, WoRel), kbRoot);
            }
            ImportAssignedFile(byKey, settings.AssignedFile);
            AbsorbAssignedSidecar(byKey, kbRoot);

            var list = new List<WorkOrder>();
            foreach (WorkOrder wo in byKey.Values) list.Add(wo);
            list.Sort(CompareWo);
            return list;
        }

        public static void CollectConflicts(IdSettings settings, string kbRoot, ShopSyncResult result)
        {
            ListAll(settings, kbRoot);
            if (result == null) return;
            foreach (ShopConflict c in Conflicts) result.AddConflict(c);
        }

        public static void Resolve(ShopConflict c, string choice, string kbRoot)
        {
            if (c == null) return;
            choice = (choice ?? "").Trim().ToLowerInvariant();
            if (choice == "local")
                CopyPacket(c.LocalPath, c.RemotePath);
            else if (choice == "remote")
                CopyPacket(c.RemotePath, c.LocalPath);
            else if (choice == "both")
                MarkKeepBoth(kbRoot, c.Key);
        }

        public static string AttachFile(WorkOrder wo, string sourcePath, IdSettings settings, string kbRoot)
        {
            if (wo == null || string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)) return null;
            Save(wo, settings, kbRoot);
            string destDir = Path.Combine(wo.Folder, "media");
            Directory.CreateDirectory(destDir);
            string name = UniqueName(destDir, Path.GetFileName(sourcePath));
            string dest = Path.Combine(destDir, name);
            File.Copy(sourcePath, dest, true);
            if (wo.Media == null) wo.Media = new List<string>();
            if (!ContainsName(wo.Media, name)) wo.Media.Add(name);
            Save(wo, settings, kbRoot);
            return dest;
        }

        public static string AttachText(WorkOrder wo, string fileName, string text, IdSettings settings, string kbRoot)
        {
            if (wo == null) return null;
            Save(wo, settings, kbRoot);
            string destDir = Path.Combine(wo.Folder, "media");
            Directory.CreateDirectory(destDir);
            string name = UniqueName(destDir, fileName);
            string dest = Path.Combine(destDir, name);
            AtomicWriteText(dest, text ?? "", Encoding.UTF8);
            if (wo.Media == null) wo.Media = new List<string>();
            if (!ContainsName(wo.Media, name)) wo.Media.Add(name);
            Save(wo, settings, kbRoot);
            return dest;
        }

        public static string Share(WorkOrder wo, IdSettings settings, string kbRoot)
        {
            if (wo == null) return null;
            Save(wo, settings, kbRoot);
            string key = SafeKey(wo.Key());
            var dests = new List<string>();
            if (!string.IsNullOrEmpty(kbRoot) && Directory.Exists(kbRoot))
                dests.Add(PacketDir(ShopSharedDir(kbRoot), key));
            if (settings != null && !string.IsNullOrWhiteSpace(settings.ShareFolder))
            {
                Directory.CreateDirectory(settings.ShareFolder);
                dests.Add(PacketDir(Path.Combine(settings.ShareFolder, WoRel), key));
                dests.Add(PacketDir(Path.Combine(settings.ShareFolder, ShopRel, SharedTech, WoRel), key));
            }
            if (dests.Count == 0)
                throw new InvalidOperationException(
                    "No shop share folder. Set one in Shop → IntelliDealer, or keep the knowledge base on a shared OneDrive tree.");
            string last = null;
            foreach (string d in dests)
            {
                CopyPacket(wo.Folder, d);
                last = d;
            }
            wo.Source = "file";
            wo.AddClock("share", "Copied packet for other techs", false);
            Save(wo, settings, kbRoot);
            return last;
        }

        public static WorkOrder ImportRow(Dictionary<string, object> row)
        {
            if (row == null) return null;
            var wo = new WorkOrder { Source = "file" };
            wo.Number = Pick(row, "Number", "WorkOrder", "WorkOrderNumber", "WO", "OrderNumber", "orderNumber");
            wo.Segment = Pick(row, "Segment", "Seg", "SegmentNumber");
            wo.Customer = Pick(row, "Customer", "CustomerName", "Name");
            wo.CustomerNo = Pick(row, "CustomerNo", "CustomerNumber", "CustomerId");
            wo.Model = Pick(row, "Model", "Machine", "MakeModel");
            wo.Serial = Pick(row, "Serial", "SerialNumber", "MachineSerialNumber");
            wo.Stock = Pick(row, "Stock", "StockNumber");
            wo.Description = Pick(row, "Description", "History", "Complaint");
            wo.AssignedTech = Pick(row, "AssignedTech", "Technician", "Tech");
            if (string.IsNullOrWhiteSpace(wo.Number)) return null;
            return wo;
        }

        public static List<WorkOrder> ParseAssignedJson(string json)
        {
            var list = new List<WorkOrder>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            var ser = new JavaScriptSerializer();
            object root = ser.DeserializeObject(json);
            foreach (Dictionary<string, object> row in EnumerateRows(root))
            {
                WorkOrder wo = ImportRow(row);
                if (wo != null) list.Add(wo);
            }
            return list;
        }

        public static List<WorkOrder> ParseAssignedCsv(string text)
        {
            var list = new List<WorkOrder>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            if (lines.Length == 0) return list;
            string[] head = SplitCsv(lines[0]);
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                string[] cells = SplitCsv(lines[i]);
                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                for (int c = 0; c < head.Length && c < cells.Length; c++)
                    row[head[c].Trim()] = cells[c];
                WorkOrder wo = ImportRow(row);
                if (wo != null) list.Add(wo);
            }
            return list;
        }

        static void WritePacket(string dir, WorkOrder wo)
        {
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "media"));
            string notes = wo.Notes ?? "";
            AtomicWriteText(Path.Combine(dir, "notes.txt"), notes, Encoding.UTF8);
            if (!string.IsNullOrEmpty(wo.ReportText))
                AtomicWriteText(Path.Combine(dir, "report.txt"), wo.ReportText, Encoding.UTF8);
            var snap = Shallow(wo);
            var ser = new JavaScriptSerializer();
            // File.WriteAllText(path, text) is UTF-8 without a BOM. Encoding.UTF8 would add one.
            AtomicWriteText(Path.Combine(dir, "wo.json"), ser.Serialize(snap), new UTF8Encoding(false));
        }

        /// <summary>
        /// Write text by flushing a temp file in the same directory, then replacing the target.
        /// File.Replace keeps path.bak. Some network shares and OneDrive placeholders throw on
        /// Replace; those fall back to copying the target over path.bak, deleting the target,
        /// and moving the temp into place. On failure the temp is deleted and the exception
        /// is rethrown. If that fallback already deleted the target and the move then fails,
        /// the previous version is copied back from path.bak when the live file is missing.
        /// </summary>
        internal static void AtomicWriteText(string path, string text, Encoding enc)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path is required.", "path");
            if (enc == null) enc = new UTF8Encoding(false);
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                WriteAtomicBytes(temp, text ?? "", enc);
                if (File.Exists(path))
                    CommitReplacing(temp, path);
                else
                    File.Move(temp, path);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch { }
                throw;
            }
        }

        /// <summary>Flush a sibling temp file and return its path without replacing the target. SelfTest crash seam.</summary>
        internal static string WriteAtomicTemp(string path, string text, Encoding enc)
        {
            if (enc == null) enc = new UTF8Encoding(false);
            string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            WriteAtomicBytes(temp, text ?? "", enc);
            return temp;
        }

        static void WriteAtomicBytes(string temp, string text, Encoding enc)
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                byte[] preamble = enc.GetPreamble();
                if (preamble != null && preamble.Length > 0)
                    fs.Write(preamble, 0, preamble.Length);
                byte[] bytes = enc.GetBytes(text ?? "");
                if (bytes.Length > 0)
                    fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
        }

        static void CommitReplacing(string temp, string path)
        {
            string bak = path + ".bak";
            if (!ForceCopyFallbackForTest)
            {
                try
                {
                    File.Replace(temp, path, bak, true);
                    return;
                }
                catch (Exception)
                {
                    // Network shares and OneDrive placeholders can reject File.Replace.
                }
            }
            File.Copy(path, bak, true);
            File.Delete(path);
            try
            {
                File.Move(temp, path);
            }
            catch
            {
                try
                {
                    if (!File.Exists(path) && File.Exists(bak))
                        File.Copy(bak, path, false);
                }
                catch { }
                throw;
            }
        }

        internal static bool IsIgnoredPacketFile(string path)
        {
            string name = Path.GetFileName(path ?? "");
            if (name.Length == 0) return false;
            if (name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.IndexOf(".tmp-", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static WorkOrder Shallow(WorkOrder wo)
        {
            var copy = new WorkOrder();
            copy.Number = wo.Number;
            copy.Segment = wo.Segment;
            copy.Customer = wo.Customer;
            copy.CustomerNo = wo.CustomerNo;
            copy.Model = wo.Model;
            copy.Serial = wo.Serial;
            copy.Stock = wo.Stock;
            copy.Description = wo.Description;
            copy.AssignedTech = wo.AssignedTech;
            copy.Notes = wo.Notes;
            copy.ReportText = wo.ReportText;
            copy.Source = wo.Source;
            copy.ClockState = wo.ClockState;
            copy.ApiMessage = wo.ApiMessage;
            copy.ApiLogOn = wo.ApiLogOn;
            copy.ApiSignOff = wo.ApiSignOff;
            copy.UpdatedUtc = wo.UpdatedUtc;
            copy.Media = wo.Media == null ? new List<string>() : new List<string>(wo.Media);
            copy.Clock = wo.Clock == null ? new List<WorkOrderClock>() : new List<WorkOrderClock>(wo.Clock);
            return copy;
        }

        static void CopyPacket(string src, string dest)
        {
            if (string.IsNullOrEmpty(src) || !Directory.Exists(src)) return;
            Directory.CreateDirectory(dest);
            Directory.CreateDirectory(Path.Combine(dest, "media"));
            foreach (string f in Directory.GetFiles(src))
            {
                if (IsIgnoredPacketFile(f)) continue;
                AtomicCopyFile(f, Path.Combine(dest, Path.GetFileName(f)));
            }
            string media = Path.Combine(src, "media");
            if (Directory.Exists(media))
            {
                string dm = Path.Combine(dest, "media");
                Directory.CreateDirectory(dm);
                foreach (string f in Directory.GetFiles(media))
                {
                    if (IsIgnoredPacketFile(f)) continue;
                    AtomicCopyFile(f, Path.Combine(dm, Path.GetFileName(f)));
                }
            }
        }

        /// <summary>
        /// Copy one file onto dest with no .bak. Bytes land in a sibling dest.tmp-guid first,
        /// then replace (or move) into place. The temp is removed if the copy fails.
        /// </summary>
        static void AtomicCopyFile(string src, string dest)
        {
            string folder = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            string temp = dest + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(src, temp, false);
                if (File.Exists(dest))
                {
                    try
                    {
                        File.Replace(temp, dest, null, true);
                    }
                    catch (Exception)
                    {
                        File.Copy(temp, dest, true);
                        try { if (File.Exists(temp)) File.Delete(temp); }
                        catch { }
                    }
                }
                else
                    File.Move(temp, dest);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch { }
                throw;
            }
        }

        static void Absorb(Dictionary<string, WorkOrder> byKey, string root, string source, string kbRoot)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            string[] dirs;
            try { dirs = Directory.GetDirectories(root); }
            catch { return; }
            foreach (string d in dirs)
            {
                WorkOrder wo = Load(d);
                if (wo == null || string.IsNullOrWhiteSpace(wo.Key())) continue;
                if (string.IsNullOrWhiteSpace(wo.Source)) wo.Source = source;
                Merge(byKey, wo, kbRoot);
            }
        }

        static void AbsorbShared(Dictionary<string, WorkOrder> byKey, string root, string kbRoot)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            string[] dirs;
            try { dirs = Directory.GetDirectories(root); }
            catch { return; }
            foreach (string d in dirs)
            {
                WorkOrder wo = Load(d);
                if (wo == null || string.IsNullOrWhiteSpace(wo.Key())) continue;
                wo.Source = "shared";
                Merge(byKey, wo, kbRoot);
            }
        }

        static void Merge(Dictionary<string, WorkOrder> byKey, WorkOrder incoming, string kbRoot)
        {
            if (incoming == null) return;
            string k = incoming.Key();
            if (string.IsNullOrWhiteSpace(k)) return;
            if (!byKey.ContainsKey(k))
            {
                byKey[k] = incoming;
                return;
            }
            WorkOrder have = byKey[k];
            if (PacketHash(have) == PacketHash(incoming))
            {
                if (CompareUtc(incoming, have) > 0) byKey[k] = incoming;
                return;
            }
            WorkOrder winner = CompareUtc(incoming, have) > 0 ? incoming : have;
            if (AlreadyConflict(k)) return;
            byKey[k] = winner;
            if (KeepBoth(kbRoot, k)) return;
            Conflicts.Add(new ShopConflict
            {
                Kind = "work-order",
                Key = k,
                RelativePath = Path.Combine(WoRel, SafeKey(k)),
                LocalPath = have.Folder,
                RemotePath = incoming.Folder,
                LocalSummary = NotePreview(have),
                RemoteSummary = NotePreview(incoming),
                Prefix = "wo"
            });
        }

        static bool AlreadyConflict(string key)
        {
            foreach (ShopConflict c in Conflicts)
            {
                if (c.Kind == "work-order"
                    && string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        static string PacketHash(WorkOrder wo)
        {
            if (wo == null) return "";
            var sb = new StringBuilder();
            sb.Append(wo.Number ?? "").Append('\n');
            sb.Append(wo.Segment ?? "").Append('\n');
            sb.Append(wo.Customer ?? "").Append('\n');
            sb.Append(wo.CustomerNo ?? "").Append('\n');
            sb.Append(wo.Model ?? "").Append('\n');
            sb.Append(wo.Serial ?? "").Append('\n');
            sb.Append(wo.Stock ?? "").Append('\n');
            sb.Append(wo.Description ?? "").Append('\n');
            sb.Append(wo.AssignedTech ?? "").Append('\n');
            sb.Append(wo.Notes ?? "").Append('\n');
            sb.Append(wo.ReportText ?? "").Append('\n');
            sb.Append(wo.ClockState ?? "").Append('\n');
            if (wo.Media != null)
            {
                var names = new List<string>(wo.Media);
                names.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string n in names) sb.Append(n).Append('\n');
            }
            return ShopSync.HashText(sb.ToString());
        }

        static int CompareUtc(WorkOrder a, WorkOrder b)
        {
            return ParseUtc(a == null ? "" : a.UpdatedUtc).CompareTo(ParseUtc(b == null ? "" : b.UpdatedUtc));
        }

        static DateTime ParseUtc(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return DateTime.MinValue;
            DateTime dt;
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out dt))
                return dt;
            return DateTime.MinValue;
        }

        static string NotePreview(WorkOrder wo)
        {
            string n = wo == null ? "" : (wo.Notes ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (n.Length == 0) n = "(no notes)";
            if (n.Length > 60) n = n.Substring(0, 60);
            return n;
        }

        static string KeepBothPath(string kbRoot)
        {
            if (!string.IsNullOrEmpty(kbRoot))
                return Path.Combine(kbRoot, ShopRel, "_resolved", "keep-both-wo.txt");
            return Path.Combine(IdSettings.Folder(), "keep-both-wo.txt");
        }

        static bool KeepBoth(string kbRoot, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            try
            {
                string path = KeepBothPath(kbRoot);
                if (!File.Exists(path)) return false;
                foreach (string line in File.ReadAllLines(path))
                {
                    if (string.Equals(line.Trim(), key, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        static void MarkKeepBoth(string kbRoot, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            string path = KeepBothPath(kbRoot);
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var lines = new List<string>();
            if (File.Exists(path)) lines.AddRange(File.ReadAllLines(path));
            foreach (string line in lines)
                if (string.Equals(line.Trim(), key, StringComparison.OrdinalIgnoreCase)) return;
            lines.Add(key);
            File.WriteAllLines(path, lines.ToArray());
        }

        static void AbsorbAssignedSidecar(Dictionary<string, WorkOrder> byKey, string kbRoot)
        {
            var files = new List<string>();
            string exe = string.IsNullOrEmpty(ExeDirOverride) ? IdSettings.ExeDir() : ExeDirOverride;
            files.Add(Path.Combine(exe, "id-work-orders.json"));
            files.Add(Path.Combine(exe, "id-work-orders.csv"));
            if (!string.IsNullOrEmpty(kbRoot))
            {
                files.Add(Path.Combine(kbRoot, "data", "id-work-orders.json"));
                files.Add(Path.Combine(kbRoot, "data", "id-work-orders.csv"));
            }
            foreach (string f in files) ImportAssignedFile(byKey, f);
        }

        static void ImportAssignedFile(Dictionary<string, WorkOrder> byKey, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            try
            {
                string text = File.ReadAllText(path);
                List<WorkOrder> rows;
                if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    rows = ParseAssignedCsv(text);
                else
                    rows = ParseAssignedJson(text);
                foreach (WorkOrder wo in rows)
                {
                    string k = wo.Key();
                    if (k.Length == 0 || byKey.ContainsKey(k)) continue;
                    wo.Source = "file";
                    byKey[k] = wo;
                }
            }
            catch { }
        }

        static IEnumerable<Dictionary<string, object>> EnumerateRows(object root)
        {
            if (root == null) yield break;
            object[] arr = AsArray(root);
            if (arr != null)
            {
                foreach (object item in arr)
                {
                    Dictionary<string, object> row = AsMap(item);
                    if (row != null) yield return row;
                }
                yield break;
            }
            Dictionary<string, object> map = AsMap(root);
            if (map == null) yield break;
            foreach (string key in new[] { "value", "workOrders", "WorkOrders", "data", "items", "Assigned" })
            {
                if (!map.ContainsKey(key)) continue;
                foreach (Dictionary<string, object> row in EnumerateRows(map[key]))
                    yield return row;
                yield break;
            }
            yield return map;
        }

        static object[] AsArray(object o)
        {
            object[] arr = o as object[];
            if (arr != null) return arr;
            System.Collections.ArrayList list = o as System.Collections.ArrayList;
            if (list != null)
            {
                var a = new object[list.Count];
                list.CopyTo(a);
                return a;
            }
            return null;
        }

        static Dictionary<string, object> AsMap(object o)
        {
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d != null) return d;
            var raw = o as Dictionary<string, object>;
            return raw;
        }

        static string Pick(Dictionary<string, object> row, params string[] names)
        {
            if (row == null) return "";
            foreach (string n in names)
            {
                foreach (KeyValuePair<string, object> kv in row)
                {
                    if (string.Equals(kv.Key, n, StringComparison.OrdinalIgnoreCase) && kv.Value != null)
                        return kv.Value.ToString().Trim();
                }
            }
            return "";
        }

        static string[] SplitCsv(string line)
        {
            var cells = new List<string>();
            var sb = new StringBuilder();
            bool q = false;
            foreach (char c in line)
            {
                if (c == '"') { q = !q; continue; }
                if (c == ',' && !q) { cells.Add(sb.ToString()); sb.Length = 0; }
                else sb.Append(c);
            }
            cells.Add(sb.ToString());
            return cells.ToArray();
        }

        static string UniqueName(string dir, string fileName)
        {
            string name = Path.GetFileName(fileName);
            if (string.IsNullOrEmpty(name)) name = "file.bin";
            string dest = Path.Combine(dir, name);
            if (!File.Exists(dest)) return name;
            string stem = Path.GetFileNameWithoutExtension(name);
            string ext = Path.GetExtension(name);
            for (int i = 2; i < 1000; i++)
            {
                string n = stem + "-" + i + ext;
                if (!File.Exists(Path.Combine(dir, n))) return n;
            }
            return stem + "-" + DateTime.Now.Ticks + ext;
        }

        static bool ContainsName(List<string> names, string name)
        {
            if (names == null) return false;
            foreach (string n in names)
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static int CompareWo(WorkOrder a, WorkOrder b)
        {
            int c = string.Compare(a.Number, b.Number, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            return string.Compare(a.Segment, b.Segment, StringComparison.OrdinalIgnoreCase);
        }
    }
}
