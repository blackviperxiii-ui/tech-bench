using System;
using System.IO;
using System.Web.Script.Serialization;

namespace TechBench
{
    /// <summary>
    /// Dealer Azure API Gateway credentials and shop-share paths. Lives in LocalAppData (and
    /// optionally next to the exe). Never a GitHub token.
    /// </summary>
    public sealed class IdSettings
    {
        public string TechId = "";
        public string TechNumber = "";
        public string Company = "";
        public string Location = "";
        public string GatewayUrl = "";
        public string SubscriptionKey = "";
        public string ClientId = "";
        public string ClientSecret = "";
        public string TokenUrl = "";
        public string ShareFolder = "";
        public string AssignedFile = "";

        public string AssignedPath = "/service/technicians/{tech}/workorders";
        public string LogOnPath = "/service/workorders/{wo}/logon";
        public string LogOffPath = "/service/workorders/{wo}/logoff";
        public string SignOffPath = "/service/workorders/{wo}/signoff";
        public string MultimediaPath = "/service/workorders/{wo}/attachments";

        /// <summary>Tests point this at a temp folder so we do not touch LocalAppData.</summary>
        public static string FolderOverride;

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
            return Path.Combine(Folder(), "id-settings.json");
        }

        public static IdSettings Load()
        {
            IdSettings s = LoadFrom(PathName());
            string exe = ExeDir();
            if (s.IsBlank())
            {
                IdSettings beside = LoadFrom(Path.Combine(exe, "id-settings.json"));
                if (!beside.IsBlank()) s = beside;
            }
            OverlayOperations(s, Path.Combine(exe, "id-api.json"));
            if (string.IsNullOrWhiteSpace(s.TechId)) s.TechId = DefaultTechId();
            if (string.IsNullOrWhiteSpace(s.ShareFolder))
                s.ShareFolder = ReadSiblingShare(exe);
            return s;
        }

        public static IdSettings LoadFrom(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new IdSettings();
                var ser = new JavaScriptSerializer();
                var s = ser.Deserialize<IdSettings>(File.ReadAllText(path));
                return s ?? new IdSettings();
            }
            catch
            {
                return new IdSettings();
            }
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
            var ser = new JavaScriptSerializer();
            File.WriteAllText(path, ser.Serialize(this));
        }

        public bool IsBlank()
        {
            return string.IsNullOrWhiteSpace(GatewayUrl)
                && string.IsNullOrWhiteSpace(SubscriptionKey)
                && string.IsNullOrWhiteSpace(ShareFolder)
                && string.IsNullOrWhiteSpace(AssignedFile)
                && string.IsNullOrWhiteSpace(TechNumber);
        }

        public bool GatewayConfigured()
        {
            return !string.IsNullOrWhiteSpace(GatewayUrl)
                && (!string.IsNullOrWhiteSpace(SubscriptionKey)
                    || (!string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret)));
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
                var ser = new JavaScriptSerializer();
                var map = ser.Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(path));
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

        static void OverlayOperations(IdSettings s, string apiPath)
        {
            if (s == null || string.IsNullOrEmpty(apiPath) || !File.Exists(apiPath)) return;
            try
            {
                var extra = LoadFrom(apiPath);
                if (!string.IsNullOrWhiteSpace(extra.AssignedPath)) s.AssignedPath = extra.AssignedPath;
                if (!string.IsNullOrWhiteSpace(extra.LogOnPath)) s.LogOnPath = extra.LogOnPath;
                if (!string.IsNullOrWhiteSpace(extra.LogOffPath)) s.LogOffPath = extra.LogOffPath;
                if (!string.IsNullOrWhiteSpace(extra.SignOffPath)) s.SignOffPath = extra.SignOffPath;
                if (!string.IsNullOrWhiteSpace(extra.MultimediaPath)) s.MultimediaPath = extra.MultimediaPath;
                if (!string.IsNullOrWhiteSpace(extra.GatewayUrl) && string.IsNullOrWhiteSpace(s.GatewayUrl))
                    s.GatewayUrl = extra.GatewayUrl;
                if (!string.IsNullOrWhiteSpace(extra.TokenUrl) && string.IsNullOrWhiteSpace(s.TokenUrl))
                    s.TokenUrl = extra.TokenUrl;
            }
            catch { }
        }
    }
}
