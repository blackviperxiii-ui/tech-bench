using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace TechBench
{
    /// <summary>
    /// Dealer Azure API Gateway credentials and shop-share paths. Secrets live only in
    /// LocalAppData, DPAPI-protected at rest. Beside-exe files may overlay non-secrets.
    /// Never a GitHub token.
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
        public string SubscriptionKeyProtected = "";
        public string ClientSecretProtected = "";
        public string ShareFolder = "";
        public string AssignedFile = "";

        public string AssignedPath = "/service/technicians/{tech}/workorders";
        public string LogOnPath = "/service/workorders/{wo}/logon";
        public string LogOffPath = "/service/workorders/{wo}/logoff";
        public string SignOffPath = "/service/workorders/{wo}/signoff";
        public string MultimediaPath = "/service/workorders/{wo}/attachments";

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
            return Path.Combine(Folder(), "id-settings.json");
        }

        public static IdSettings Load()
        {
            IdSettings s = LoadFrom(PathName());
            string exe = ExeDir();
            IdSettings beside = LoadFrom(Path.Combine(exe, "id-settings.json"));
            if (s.IsBlank()) CopyNonSecrets(s, beside);
            else FillBlankNonSecrets(s, beside);
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
                if (s == null) return new IdSettings();
                FinishLoad(s, StoresSecrets(path));
                return s;
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
            bool secrets = StoresSecrets(path);
            string secret = ClientSecret ?? "";
            string key = SubscriptionKey ?? "";
            string pSecret = ClientSecretProtected ?? "";
            string pKey = SubscriptionKeyProtected ?? "";
            try
            {
                ClientSecret = "";
                SubscriptionKey = "";
                if (secrets)
                {
                    string nextSecret = ProtectSecret(secret);
                    string nextKey = ProtectSecret(key);
                    bool secretFailed = string.IsNullOrEmpty(nextSecret) && !string.IsNullOrEmpty(secret);
                    bool keyFailed = string.IsNullOrEmpty(nextKey) && !string.IsNullOrEmpty(key);
                    if (secretFailed || keyFailed)
                    {
                        string keepSecret = pSecret;
                        string keepKey = pKey;
                        if (string.IsNullOrEmpty(keepSecret) || string.IsNullOrEmpty(keepKey))
                            ReadExistingProtected(path, ref keepSecret, ref keepKey);
                        if (secretFailed) nextSecret = keepSecret;
                        if (keyFailed) nextKey = keepKey;
                    }
                    ClientSecretProtected = nextSecret;
                    SubscriptionKeyProtected = nextKey;
                }
                else
                {
                    ClientSecretProtected = "";
                    SubscriptionKeyProtected = "";
                }
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var ser = new JavaScriptSerializer();
                File.WriteAllText(path, ser.Serialize(this));
            }
            finally
            {
                ClientSecret = secret;
                SubscriptionKey = key;
                ClientSecretProtected = pSecret;
                SubscriptionKeyProtected = pKey;
            }
        }

        const string PortablePrefix = "tb1.";
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TechBench.IdSettings.v1");
        static int _dpapi; // 0 unknown, 1 yes, -1 no

        public static string ProtectSecret(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            if (DpapiAvailable())
            {
                try
                {
                    byte[] data = Encoding.UTF8.GetBytes(plain);
                    byte[] prot = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
                    return Convert.ToBase64String(prot);
                }
                catch { return ""; }
            }
            if (!string.IsNullOrEmpty(FolderOverride)) return PortableProtect(plain);
            return "";
        }

        public static string UnprotectSecret(string cipher)
        {
            if (string.IsNullOrEmpty(cipher)) return "";
            if (cipher.StartsWith(PortablePrefix, StringComparison.Ordinal))
            {
                if (string.IsNullOrEmpty(FolderOverride)) return "";
                try { return PortableUnprotect(cipher); }
                catch { return ""; }
            }
            try
            {
                byte[] raw = Convert.FromBase64String(cipher);
                byte[] plain = ProtectedData.Unprotect(raw, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return "";
            }
        }

        static bool DpapiAvailable()
        {
            if (_dpapi != 0) return _dpapi > 0;
            try
            {
                byte[] p = ProtectedData.Protect(new byte[] { 7 }, Entropy, DataProtectionScope.CurrentUser);
                byte[] u = ProtectedData.Unprotect(p, Entropy, DataProtectionScope.CurrentUser);
                _dpapi = (u != null && u.Length == 1 && u[0] == 7) ? 1 : -1;
            }
            catch { _dpapi = -1; }
            return _dpapi > 0;
        }

        static string PortableProtect(string plain)
        {
            byte[] data = Encoding.UTF8.GetBytes(plain);
            for (int i = 0; i < data.Length; i++) data[i] ^= Entropy[i % Entropy.Length];
            return PortablePrefix + Convert.ToBase64String(data);
        }

        static string PortableUnprotect(string cipher)
        {
            byte[] data = Convert.FromBase64String(cipher.Substring(PortablePrefix.Length));
            for (int i = 0; i < data.Length; i++) data[i] ^= Entropy[i % Entropy.Length];
            return Encoding.UTF8.GetString(data);
        }

        static void ReadExistingProtected(string path, ref string secretProt, ref string keyProt)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                var ser = new JavaScriptSerializer();
                var prev = ser.Deserialize<IdSettings>(File.ReadAllText(path));
                if (prev == null) return;
                if (string.IsNullOrEmpty(secretProt)) secretProt = prev.ClientSecretProtected ?? "";
                if (string.IsNullOrEmpty(keyProt)) keyProt = prev.SubscriptionKeyProtected ?? "";
            }
            catch { }
        }

        static bool StoresSecrets(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return false;
                string full = Path.GetFullPath(path);
                string folder = Path.GetFullPath(Folder());
                string root = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string dir = Path.GetDirectoryName(full);
                if (string.IsNullOrEmpty(dir)) return false;
                if (string.Equals(Path.GetFullPath(dir), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
                    return true;
                string prefix = root + Path.DirectorySeparatorChar;
                return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        static void FinishLoad(IdSettings s, bool allowSecrets)
        {
            if (s == null) return;
            if (!allowSecrets)
            {
                s.ClientSecret = "";
                s.SubscriptionKey = "";
                s.ClientSecretProtected = "";
                s.SubscriptionKeyProtected = "";
                return;
            }
            string decSecret = UnprotectSecret(s.ClientSecretProtected);
            string decKey = UnprotectSecret(s.SubscriptionKeyProtected);
            if (!string.IsNullOrEmpty(decSecret)) s.ClientSecret = decSecret;
            if (!string.IsNullOrEmpty(decKey)) s.SubscriptionKey = decKey;
        }

        static void CopyNonSecrets(IdSettings dest, IdSettings src)
        {
            if (dest == null || src == null) return;
            dest.TechId = src.TechId;
            dest.TechNumber = src.TechNumber;
            dest.Company = src.Company;
            dest.Location = src.Location;
            dest.GatewayUrl = src.GatewayUrl;
            dest.ClientId = src.ClientId;
            dest.TokenUrl = src.TokenUrl;
            dest.ShareFolder = src.ShareFolder;
            dest.AssignedFile = src.AssignedFile;
            dest.AssignedPath = src.AssignedPath;
            dest.LogOnPath = src.LogOnPath;
            dest.LogOffPath = src.LogOffPath;
            dest.SignOffPath = src.SignOffPath;
            dest.MultimediaPath = src.MultimediaPath;
        }

        static void FillBlankNonSecrets(IdSettings dest, IdSettings src)
        {
            if (dest == null || src == null) return;
            if (string.IsNullOrWhiteSpace(dest.TechId)) dest.TechId = src.TechId;
            if (string.IsNullOrWhiteSpace(dest.TechNumber)) dest.TechNumber = src.TechNumber;
            if (string.IsNullOrWhiteSpace(dest.Company)) dest.Company = src.Company;
            if (string.IsNullOrWhiteSpace(dest.Location)) dest.Location = src.Location;
            if (string.IsNullOrWhiteSpace(dest.GatewayUrl)) dest.GatewayUrl = src.GatewayUrl;
            if (string.IsNullOrWhiteSpace(dest.ClientId)) dest.ClientId = src.ClientId;
            if (string.IsNullOrWhiteSpace(dest.TokenUrl)) dest.TokenUrl = src.TokenUrl;
            if (string.IsNullOrWhiteSpace(dest.ShareFolder)) dest.ShareFolder = src.ShareFolder;
            if (string.IsNullOrWhiteSpace(dest.AssignedFile)) dest.AssignedFile = src.AssignedFile;
            if (string.IsNullOrWhiteSpace(dest.AssignedPath)) dest.AssignedPath = src.AssignedPath;
            if (string.IsNullOrWhiteSpace(dest.LogOnPath)) dest.LogOnPath = src.LogOnPath;
            if (string.IsNullOrWhiteSpace(dest.LogOffPath)) dest.LogOffPath = src.LogOffPath;
            if (string.IsNullOrWhiteSpace(dest.SignOffPath)) dest.SignOffPath = src.SignOffPath;
            if (string.IsNullOrWhiteSpace(dest.MultimediaPath)) dest.MultimediaPath = src.MultimediaPath;
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
