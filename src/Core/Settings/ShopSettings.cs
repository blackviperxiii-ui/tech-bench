using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace TechBench
{
    /// <summary>
    /// Shop identity and share paths for work orders: tech name, extra share folder, assigned-WO file.
    /// Stored in LocalAppData\TechBench\shop-settings.json; a beside-exe copy fills blanks.
    /// </summary>
    public sealed class ShopSettings
    {
        public string TechId = "";
        public string ShareFolder = "";
        public string AssignedFile = "";

        public const string FileName = "shop-settings.json";
        /// <summary>Older settings file. Read once for TechId / ShareFolder / AssignedFile only, never written.</summary>
        public const string LegacyFileName = "id-settings.json";

        /// <summary>Tests point this at a temp folder so we do not touch LocalAppData.</summary>
        public static string FolderOverride;
        /// <summary>Tests point this at a temp folder so beside-exe overlays stay off the real exe.</summary>
        public static string ExeDirOverride;

        public static string Folder()
        {
            if (!string.IsNullOrEmpty(FolderOverride))
            {
                Directory.CreateDirectory(FolderOverride);
                return FolderOverride;
            }
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root)) root = Path.GetTempPath();
            string d = Path.Combine(root, "TechBench");
            Directory.CreateDirectory(d);
            return d;
        }

        public static string PathName()
        {
            return Path.Combine(Folder(), FileName);
        }

        public static ShopSettings Load()
        {
            string path = PathName();
            ShopSettings s;
            if (File.Exists(path))
                s = LoadFrom(path);
            else
            {
                s = ReadLegacy(Path.Combine(Folder(), LegacyFileName));
                if (!s.IsBlank()) s.SaveTo(path);
            }
            string exe = ExeDir();
            FillBlanks(s, LoadFrom(Path.Combine(exe, FileName)));
            FillBlanks(s, ReadLegacy(Path.Combine(exe, LegacyFileName)));
            if (string.IsNullOrWhiteSpace(s.TechId)) s.TechId = DefaultTechId();
            if (string.IsNullOrWhiteSpace(s.ShareFolder))
                s.ShareFolder = ReadSiblingShare(exe);
            return s;
        }

        public static ShopSettings LoadFrom(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new ShopSettings();
                var s = new JavaScriptSerializer().Deserialize<ShopSettings>(File.ReadAllText(path));
                return s ?? new ShopSettings();
            }
            catch
            {
                return new ShopSettings();
            }
        }

        /// <summary>Pick only the three shop fields out of an old settings file; every other key is ignored.</summary>
        public static ShopSettings ReadLegacy(string path)
        {
            var s = new ShopSettings();
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return s;
                var map = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                if (map == null) return s;
                s.TechId = Field(map, "TechId");
                s.ShareFolder = Field(map, "ShareFolder");
                s.AssignedFile = Field(map, "AssignedFile");
            }
            catch { }
            return s;
        }

        static string Field(Dictionary<string, object> map, string key)
        {
            object v;
            if (!map.TryGetValue(key, out v) || v == null) return "";
            return v as string ?? "";
        }

        public void Save()
        {
            SaveTo(PathName());
        }

        public void SaveTo(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, new JavaScriptSerializer().Serialize(this));
        }

        static void FillBlanks(ShopSettings dest, ShopSettings src)
        {
            if (dest == null || src == null) return;
            if (string.IsNullOrWhiteSpace(dest.TechId)) dest.TechId = src.TechId ?? "";
            if (string.IsNullOrWhiteSpace(dest.ShareFolder)) dest.ShareFolder = src.ShareFolder ?? "";
            if (string.IsNullOrWhiteSpace(dest.AssignedFile)) dest.AssignedFile = src.AssignedFile ?? "";
        }

        public bool IsBlank()
        {
            return string.IsNullOrWhiteSpace(TechId)
                && string.IsNullOrWhiteSpace(ShareFolder)
                && string.IsNullOrWhiteSpace(AssignedFile);
        }

        public static string DefaultTechId()
        {
            try
            {
                string u = Environment.UserName;
                if (!string.IsNullOrWhiteSpace(u)) return Sanitize(u);
            }
            catch { }
            return "tech";
        }

        public static string Sanitize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "tech";
            var sb = new System.Text.StringBuilder();
            foreach (char c in raw.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else if (c == ' ' || c == '.') sb.Append('-');
            }
            string s = sb.ToString().Trim('-');
            if (s.Length == 0) return "tech";
            if (s.Length > 40) s = s.Substring(0, 40);
            return s;
        }

        public static string ExeDir()
        {
            if (!string.IsNullOrEmpty(ExeDirOverride)) return ExeDirOverride;
            try
            {
                string d = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(d)) return d;
            }
            catch { }
            return Environment.CurrentDirectory;
        }

        /// <summary>
        /// Read the other branch's sync.json / sync-path.txt if present. We never write those files.
        /// </summary>
        public static string ReadSiblingShare(string exeDir)
        {
            string fromSync = ReadJsonField(Path.Combine(Folder(), "sync.json"), "SyncFolder");
            if (!string.IsNullOrWhiteSpace(fromSync)) return fromSync;
            string line = ReadFirstPath(Path.Combine(exeDir ?? "", "sync-path.txt"));
            if (!string.IsNullOrWhiteSpace(line)) return line;
            return "";
        }

        static string ReadJsonField(string path, string field)
        {
            try
            {
                if (!File.Exists(path)) return "";
                var map = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                if (map == null || !map.ContainsKey(field) || map[field] == null) return "";
                return map[field].ToString();
            }
            catch { return ""; }
        }

        static string ReadFirstPath(string path)
        {
            try
            {
                if (!File.Exists(path)) return "";
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    return line;
                }
            }
            catch { }
            return "";
        }
    }
}
