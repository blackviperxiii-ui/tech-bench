using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace TechBench
{
    internal sealed class UpdateManifest
    {
        public string Version = "";
        public string Sha256 = "";
        public string Url = "";
        public string Notes = "";
    }

    internal sealed class UpdateCheck
    {
        public string Error;
        public UpdateManifest Manifest;
        /// <summary>
        /// True only after a manifest was read and its version parsed against this build.
        /// Newer defaults to false; that default is not "up to date".
        /// </summary>
        public bool Checked;
        public bool Newer;
        public bool Ok { get { return Manifest != null && string.IsNullOrEmpty(Error); } }
    }

    /// <summary>
    /// Public HTTPS latest.json + SHA-256 of TechBench.exe, then a tiny .cmd swap after the
    /// process exits. Never sends credentials (no GitHub PAT). Never replaces a running session.
    /// </summary>
    internal static class Updater
    {
        public const string DefaultManifestUrl =
            "https://github.com/blackviperxiii-ui/tech-bench/releases/latest/download/latest.json";

        public const string StagedName = "TechBench.exe.new";
        public const string HashSidecar = "TechBench.exe.new.sha256";
        public const string ApplyCmd = "apply-update.cmd";

        public static string ExeDir()
        {
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        public static string ManifestUrl(AppSettings settings)
        {
            if (settings != null && !string.IsNullOrWhiteSpace(settings.ManifestUrl))
                return settings.ManifestUrl.Trim();
            try
            {
                string cfg = Path.Combine(ExeDir(), "update-url.txt");
                if (File.Exists(cfg))
                {
                    foreach (string line in File.ReadAllLines(cfg))
                    {
                        string t = line.Trim();
                        if (t.Length > 0 && !t.StartsWith("#")) return t;
                    }
                }
            }
            catch { }
            return DefaultManifestUrl;
        }

        public static bool TryParseVersion(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = text.Trim();
            if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s.Substring(1);
            return Version.TryParse(s, out version);
        }

        public static bool IsNewer(string remote, string local)
        {
            Version a, b;
            if (!TryParseVersion(remote, out a) || !TryParseVersion(local, out b))
                return false;
            return a > b;
        }

        /// <summary>
        /// "Up to date" only after a readable manifest compared as not newer.
        /// A missing file, HTTP error, 404, or unreadable latest.json leaves Checked false.
        /// </summary>
        public static bool IsCurrent(UpdateCheck result)
        {
            return result != null && result.Checked && result.Ok && !result.Newer;
        }

        public static bool IsAvailable(UpdateCheck result)
        {
            return result != null && result.Checked && result.Ok && result.Newer;
        }

        public static string StatusText(UpdateCheck result)
        {
            if (IsAvailable(result))
                return "Version " + result.Manifest.Version + " is available (you have " + AppVersion.Number + ").";
            if (IsCurrent(result))
                return "Tech Bench " + AppVersion.Number + " is current.";
            string detail = result == null || result.Error == null ? "" : result.Error.Trim();
            if (detail.Length == 0)
                return "Update check failed.";
            if (detail.StartsWith("Update check failed", StringComparison.OrdinalIgnoreCase))
                return detail;
            return "Update check failed. " + detail;
        }

        public static UpdateManifest ParseManifest(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            Dictionary<string, object> d;
            try
            {
                var ser = new JavaScriptSerializer();
                d = ser.Deserialize<Dictionary<string, object>>(json);
            }
            catch
            {
                return null;
            }
            if (d == null) return null;
            var m = new UpdateManifest();
            m.Version = Str(d, "version");
            m.Sha256 = Str(d, "sha256");
            if (m.Sha256.Length == 0) m.Sha256 = Str(d, "hash");
            m.Url = Str(d, "url");
            m.Notes = Str(d, "notes");
            if (m.Version.Length == 0 || m.Sha256.Length == 0 || m.Url.Length == 0) return null;
            m.Sha256 = NormalizeHash(m.Sha256);
            if (m.Sha256.Length != 64) return null;
            return m;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return "";
            return Convert.ToString(v).Trim();
        }

        public static string NormalizeHash(string hash)
        {
            if (string.IsNullOrEmpty(hash)) return "";
            var sb = new StringBuilder(hash.Length);
            foreach (char c in hash)
            {
                if (c != ' ' && c != '\t' && c != '-') sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        public static string Sha256File(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(fs);
                return Hex(hash);
            }
        }

        public static string Sha256Bytes(byte[] data)
        {
            using (var sha = SHA256.Create())
                return Hex(sha.ComputeHash(data ?? new byte[0]));
        }

        static string Hex(byte[] hash)
        {
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }

        public static bool HashMatches(string path, string expected)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            return string.Equals(Sha256File(path), NormalizeHash(expected), StringComparison.OrdinalIgnoreCase);
        }

        public static string ApplyScript()
        {
            // Wait until this process is gone, then swap the hashed exe. ping is the sleep that
            // exists on shop PCs that predate `timeout`.
            return "@echo off\r\n"
                + "cd /d \"%~dp0\"\r\n"
                + ":wait\r\n"
                + "ping -n 2 127.0.0.1 >nul\r\n"
                + "tasklist /FI \"IMAGENAME eq TechBench.exe\" | find /I \"TechBench.exe\" >nul\r\n"
                + "if not errorlevel 1 goto wait\r\n"
                + "if not exist \"TechBench.exe.new\" goto done\r\n"
                + "if exist \"TechBench.exe\" move /Y \"TechBench.exe\" \"TechBench.exe.bak\" >nul\r\n"
                + "move /Y \"TechBench.exe.new\" \"TechBench.exe\"\r\n"
                + "if errorlevel 1 (\r\n"
                + "  if exist \"TechBench.exe.bak\" move /Y \"TechBench.exe.bak\" \"TechBench.exe\" >nul\r\n"
                + "  goto done\r\n"
                + ")\r\n"
                + "if exist \"TechBench.exe.new.sha256\" del \"TechBench.exe.new.sha256\"\r\n"
                + "start \"\" \"TechBench.exe\"\r\n"
                + ":done\r\n"
                + "del \"%~f0\"\r\n";
        }

        public static string StagedPath(string exeDir)
        {
            return Path.Combine(exeDir, StagedName);
        }

        public static bool HasVerifiedPending(string exeDir)
        {
            string staged = StagedPath(exeDir);
            string sidecar = Path.Combine(exeDir, HashSidecar);
            if (!File.Exists(staged) || !File.Exists(sidecar)) return false;
            try
            {
                string want = NormalizeHash(File.ReadAllText(sidecar));
                return HashMatches(staged, want);
            }
            catch
            {
                return false;
            }
        }

        public static void Stage(string exeDir, byte[] exeBytes, string sha256)
        {
            if (exeBytes == null || exeBytes.Length == 0)
                throw new InvalidOperationException("Update download was empty.");
            string got = Sha256Bytes(exeBytes);
            if (!string.Equals(got, NormalizeHash(sha256), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Update hash did not match latest.json (got " + got + ").");
            string staged = StagedPath(exeDir);
            File.WriteAllBytes(staged, exeBytes);
            File.WriteAllText(Path.Combine(exeDir, HashSidecar), got);
        }

        public static bool CanApplyNow(bool sessionLive)
        {
            return !sessionLive;
        }

        /// <summary>
        /// Cold-start path: if a verified TechBench.exe.new is waiting, spawn the .cmd and
        /// return true so Main should exit (not mid-session — nothing is connected yet).
        /// </summary>
        public static bool TryBeginPendingSwap(string exeDir)
        {
            if (!HasVerifiedPending(exeDir)) return false;
            LaunchSwap(exeDir);
            return true;
        }

        public static void LaunchSwap(string exeDir)
        {
            string cmd = Path.Combine(exeDir, ApplyCmd);
            File.WriteAllText(cmd, ApplyScript());
            var psi = new ProcessStartInfo
            {
                FileName = cmd,
                WorkingDirectory = exeDir,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
        }

        public static UpdateCheck Check(string manifestUrl)
        {
            var result = new UpdateCheck();
            try
            {
                if (string.IsNullOrWhiteSpace(manifestUrl))
                    throw new InvalidOperationException("Update URL is empty.");
                EnsureTls();
                string json = DownloadString(manifestUrl);
                result.Manifest = ParseManifest(json);
                if (result.Manifest == null)
                {
                    result.Error = "latest.json was missing or unreadable (version, url, or sha256).";
                    return result;
                }
                Version parsed;
                if (!TryParseVersion(result.Manifest.Version, out parsed)
                    || !TryParseVersion(AppVersion.Number, out parsed)
                    || parsed == null)
                {
                    result.Manifest = null;
                    result.Newer = false;
                    result.Checked = false;
                    result.Error = "latest.json version could not be read.";
                    return result;
                }
                result.Newer = IsNewer(result.Manifest.Version, AppVersion.Number);
                result.Checked = true;
            }
            catch (Exception ex)
            {
                result.Manifest = null;
                result.Newer = false;
                result.Checked = false;
                result.Error = FriendlyNetError(ex);
            }
            return result;
        }

        public static byte[] DownloadExe(string url)
        {
            EnsureTls();
            return DownloadBytes(url);
        }

        static string FriendlyNetError(Exception ex)
        {
            string msg = ex.Message ?? "network error";
            if (msg.IndexOf("401", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("403", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("404", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Could not read the update feed. Host latest.json on a public HTTPS URL "
                    + "(this app never uses a GitHub token).\n\n" + msg;
            }
            return msg;
        }

        static void EnsureTls()
        {
            try
            {
                // TLS 1.2 (3072) on .NET 4.0, where the enum value may not exist.
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            }
            catch { }
        }

        static HttpWebRequest MakeRequest(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "TechBench/" + AppVersion.Number;
            if (req.RequestUri != null && req.RequestUri.IsLoopback)
                req.Proxy = null;
            req.Timeout = 8000;
            req.ReadWriteTimeout = 60000;
            req.AllowAutoRedirect = true;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            // Never attach a GitHub PAT, GCM creds, or Windows login.
            req.UseDefaultCredentials = false;
            req.Credentials = null;
            req.PreAuthenticate = false;
            req.KeepAlive = false;
            return req;
        }

        static string DownloadString(string url)
        {
            byte[] bytes = DownloadBytes(url);
            return Encoding.UTF8.GetString(bytes);
        }

        static byte[] DownloadBytes(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("Update URL is empty.");
            if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                string path = new Uri(url).LocalPath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    throw new FileNotFoundException("latest.json was not found.", path ?? "");
                return File.ReadAllBytes(path);
            }
            var req = MakeRequest(url);
            HttpWebResponse resp = null;
            try
            {
                resp = (HttpWebResponse)req.GetResponse();
            }
            catch (WebException ex)
            {
                var http = ex.Response as HttpWebResponse;
                if (http != null)
                {
                    int code = (int)http.StatusCode;
                    string desc = http.StatusDescription ?? "";
                    try { http.Close(); } catch { }
                    throw new InvalidOperationException("HTTP " + code + " " + desc);
                }
                throw;
            }
            using (resp)
            using (var s = resp.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                int code = (int)resp.StatusCode;
                if (code < 200 || code >= 300)
                    throw new InvalidOperationException("HTTP " + code + " " + (resp.StatusDescription ?? ""));
                if (s == null) throw new InvalidOperationException("Empty response.");
                s.CopyTo(ms);
                if (ms.Length == 0)
                    throw new InvalidOperationException("Update feed was empty.");
                return ms.ToArray();
            }
        }
    }
}
