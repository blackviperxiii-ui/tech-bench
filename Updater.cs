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
        /// <summary>Absolute manifest URL this document was fetched from.</summary>
        public string SourceUrl = "";
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
    /// Anonymous latest.json (built-in URL or a shop override) plus SHA-256 of TechBench.exe,
    /// then a tiny .cmd swap after the process exits. Never sends credentials (no GitHub PAT).
    /// Never replaces a running session.
    /// </summary>
    internal static class Updater
    {
        public const string DefaultManifestUrl =
            "https://github.com/blackviperxiii-ui/tech-bench-dist/releases/latest/download/latest.json";

        public const long MaxManifestBytes = 64 * 1024;
        public const long MaxPayloadBytes = 32L * 1024 * 1024;
        public const int MaxRedirects = 5;

        public const string StagedName = "TechBench.exe.new";
        public const string PartName = "TechBench.exe.new.part";
        public const string HashSidecar = "TechBench.exe.new.sha256";
        public const string ApplyCmd = "apply-update.cmd";

        public const string PayloadFailureSuffix =
            "This build is private. Use the shop Setup from whoever has repo access, "
            + "or your shop's configured URL if one is set. "
            + "This app never uses a GitHub token.";

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

        public static string PayloadFailureText(string reason)
        {
            string detail = reason == null ? "" : reason.Trim();
            if (detail.Length == 0) detail = "The update could not be downloaded.";
            return "Update download failed. " + detail + " " + PayloadFailureSuffix;
        }

        /// <summary>
        /// Same host and port. http may upgrade to https when both URIs use their default ports.
        /// https to http is refused. Any other scheme change is refused.
        /// </summary>
        public static bool RedirectAllowed(Uri from, Uri to, out string reason)
        {
            reason = "";
            if (from == null || to == null)
            {
                reason = "Redirect target was missing.";
                return false;
            }
            if (!string.Equals(from.Scheme, to.Scheme, StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(from.Scheme, "https", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(to.Scheme, "http", StringComparison.OrdinalIgnoreCase))
                {
                    reason = "Redirect from https to http was refused.";
                    return false;
                }
                bool upgrade = string.Equals(from.Scheme, "http", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(to.Scheme, "https", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(from.Host ?? "", to.Host ?? "", StringComparison.OrdinalIgnoreCase)
                    && from.IsDefaultPort
                    && to.IsDefaultPort;
                if (upgrade) return true;
                reason = "Redirect to another scheme was refused ("
                    + DescribeEndpoint(from) + " \u2192 " + DescribeEndpoint(to) + ").";
                return false;
            }
            if (!string.Equals(from.Host ?? "", to.Host ?? "", StringComparison.OrdinalIgnoreCase))
            {
                reason = "Redirect to another host was refused ("
                    + DescribeEndpoint(from) + " \u2192 " + DescribeEndpoint(to) + ").";
                return false;
            }
            if (from.Port != to.Port)
            {
                reason = "Redirect to another port was refused ("
                    + DescribeEndpoint(from) + " \u2192 " + DescribeEndpoint(to) + ").";
                return false;
            }
            return true;
        }

        /// <summary>
        /// A file manifest may name a file payload on any host, or an http(s) payload.
        /// An http(s) manifest shares host (case-insensitive) and port. http may upgrade
        /// to https on the default ports. https to http, a file payload, and other schemes are refused.
        /// </summary>
        public static bool PayloadOriginAllowed(string manifestUrl, string payloadUrl, out string reason)
        {
            reason = "";
            Uri manifest;
            if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out manifest))
            {
                reason = "Update manifest URL could not be read.";
                return false;
            }
            Uri payload;
            if (!Uri.TryCreate(payloadUrl, UriKind.Absolute, out payload))
            {
                reason = "Update payload URL is relative or unreadable.";
                return false;
            }

            bool manifestFile = IsFileScheme(manifest.Scheme);
            bool payloadFile = IsFileScheme(payload.Scheme);
            bool manifestHttp = IsHttpScheme(manifest.Scheme);
            bool payloadHttp = IsHttpScheme(payload.Scheme);

            if (manifestFile)
            {
                if (payloadFile || payloadHttp) return true;
                reason = "A file update manifest cannot name an " + (payload.Scheme ?? "") + " payload.";
                return false;
            }
            if (!manifestHttp)
            {
                reason = "Update manifest scheme is not allowed.";
                return false;
            }

            if (payloadFile)
            {
                reason = "An http update manifest cannot name a file payload.";
                return false;
            }
            if (!payloadHttp)
            {
                reason = "Update payload scheme does not match the manifest.";
                return false;
            }
            if (!string.Equals(manifest.Host ?? "", payload.Host ?? "", StringComparison.OrdinalIgnoreCase))
            {
                reason = "Update payload host does not match the manifest ("
                    + DescribeEndpoint(manifest) + " \u2192 " + DescribeEndpoint(payload) + ").";
                return false;
            }

            bool sameScheme = string.Equals(manifest.Scheme, payload.Scheme, StringComparison.OrdinalIgnoreCase);
            if (!sameScheme)
            {
                bool upgrade = string.Equals(manifest.Scheme, "http", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(payload.Scheme, "https", StringComparison.OrdinalIgnoreCase)
                    && manifest.IsDefaultPort
                    && payload.IsDefaultPort;
                if (upgrade) return true;
                if (string.Equals(manifest.Scheme, "https", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(payload.Scheme, "http", StringComparison.OrdinalIgnoreCase))
                    reason = "Update payload from https to http was refused.";
                else
                    reason = "Update payload scheme does not match the manifest.";
                return false;
            }

            if (manifest.Port != payload.Port)
            {
                reason = "Update payload port does not match the manifest.";
                return false;
            }
            return true;
        }

        public static void DownloadAndStage(string exeDir, UpdateManifest manifest)
        {
            if (manifest == null)
                throw new InvalidOperationException(PayloadFailureText("No update manifest."));
            DownloadAndStage(exeDir, manifest.SourceUrl, manifest.Url, manifest.Sha256, MaxPayloadBytes);
        }

        public static void DownloadAndStage(string exeDir, string manifestUrl, string payloadUrl, string sha256)
        {
            DownloadAndStage(exeDir, manifestUrl, payloadUrl, sha256, MaxPayloadBytes);
        }

        public static void DownloadAndStage(string exeDir, string manifestUrl, string payloadUrl, string sha256, long maxBytes)
        {
            if (string.IsNullOrEmpty(exeDir))
                throw new InvalidOperationException(PayloadFailureText("Update folder is empty."));
            string part = Path.Combine(exeDir, PartName);
            string staged = StagedPath(exeDir);
            bool stagedThisAttempt = false;
            try
            {
                string originReason;
                if (!PayloadOriginAllowed(manifestUrl, payloadUrl, out originReason))
                    throw new InvalidOperationException(originReason);
                string got = DownloadPayloadToPart(payloadUrl, part, maxBytes);
                if (!string.Equals(got, NormalizeHash(sha256), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Update hash did not match latest.json (got " + got + ").");
                // Leave an older TechBench.exe.new in place until the new bytes replace it.
                // File.Replace requires the destination to exist.
                if (File.Exists(staged))
                    File.Replace(part, staged, null);
                else
                    File.Move(part, staged);
                stagedThisAttempt = true;
                File.WriteAllText(Path.Combine(exeDir, HashSidecar), got);
            }
            catch (Exception ex)
            {
                if (stagedThisAttempt)
                {
                    try { if (File.Exists(staged)) File.Delete(staged); } catch { }
                    try
                    {
                        string side = Path.Combine(exeDir, HashSidecar);
                        if (File.Exists(side)) File.Delete(side);
                    }
                    catch { }
                }
                throw AsPayloadFailure(ex);
            }
            finally
            {
                try { if (File.Exists(part)) File.Delete(part); } catch { }
            }
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
                result.Manifest.SourceUrl = manifestUrl;
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
                string originReason;
                if (!PayloadOriginAllowed(manifestUrl, result.Manifest.Url, out originReason))
                    throw new UpdateFeedRefusedException(originReason);
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
            return DownloadBytes(url, MaxPayloadBytes, false);
        }

        static string FriendlyNetError(Exception ex)
        {
            string msg = ex == null || ex.Message == null ? "network error" : ex.Message;
            if (ex is UpdateFeedRefusedException
                || msg.IndexOf("401", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("403", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("404", StringComparison.OrdinalIgnoreCase) >= 0)
                return PrivateFeedMessage(msg);
            return msg;
        }

        static string PrivateFeedMessage(string detail)
        {
            return "Update feed unavailable. Could not read update info. "
                + "This build is private. Use the shop Setup from whoever has repo access, "
                + "or your shop's configured URL if one is set. "
                + "This app never uses a GitHub token.\n\n" + (detail ?? "");
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
            // Followed by hand. A hop cannot leave the host.
            // http may upgrade to https when both URIs use the default port.
            req.AllowAutoRedirect = false;
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
            byte[] bytes = DownloadBytes(url, MaxManifestBytes, true);
            return Encoding.UTF8.GetString(bytes);
        }

        static byte[] DownloadBytes(string url, long maxBytes, bool manifest)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("Update URL is empty.");
            if (IsFileUrl(url))
            {
                string path = new Uri(url).LocalPath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    throw new FileNotFoundException("latest.json was not found.", path ?? "");
                var info = new FileInfo(path);
                if (info.Length > maxBytes)
                    throw new UpdateFeedRefusedException(TooLargeMessage(manifest, maxBytes));
                using (FileStream input = File.OpenRead(path))
                {
                    byte[] bytes = ReadStreamCapped(input, maxBytes, manifest);
                    // An empty file used to come back as zero bytes so ParseManifest could reject it.
                    if (manifest) return bytes;
                    if (bytes.Length == 0)
                        throw new InvalidOperationException("Update download was empty.");
                    return bytes;
                }
            }
            EnsureTls();
            HttpWebRequest req;
            HttpWebResponse resp = GetManual(url, maxBytes, manifest, out req);
            Stream input = null;
            try
            {
                input = resp.GetResponseStream();
                if (input == null) throw new InvalidOperationException("Empty response.");
                byte[] bytes = ReadStreamCapped(input, maxBytes, manifest);
                if (bytes.Length == 0)
                    throw new InvalidOperationException("Update feed was empty.");
                return bytes;
            }
            catch
            {
                // Abort before Close so a cap or network failure cannot sit in Close draining the body.
                // Swallow close errors so they cannot replace that failure.
                try { req.Abort(); } catch { }
                throw;
            }
            finally
            {
                if (input != null) try { input.Close(); } catch { }
                try { resp.Close(); } catch { }
            }
        }

        static string DownloadPayloadToPart(string url, string partPath, long maxBytes)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("Update URL is empty.");
            if (IsFileUrl(url))
            {
                string path = new Uri(url).LocalPath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    throw new FileNotFoundException("Update download was not found.", path ?? "");
                var info = new FileInfo(path);
                if (info.Length > maxBytes)
                    throw new UpdateFeedRefusedException(TooLargeMessage(false, maxBytes));
                using (FileStream input = File.OpenRead(path))
                using (FileStream output = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    return StreamTo(input, output, maxBytes, false);
            }
            EnsureTls();
            HttpWebRequest req;
            HttpWebResponse resp = GetManual(url, maxBytes, false, out req);
            Stream input = null;
            FileStream output = null;
            try
            {
                input = resp.GetResponseStream();
                output = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None);
                if (input == null) throw new InvalidOperationException("Empty response.");
                string hash = StreamTo(input, output, maxBytes, false);
                output.Close();
                output = null;
                return hash;
            }
            catch
            {
                // Abort before Close so a cap or network failure cannot sit in Close draining the body.
                // Swallow close errors so they cannot replace that failure.
                try { req.Abort(); } catch { }
                throw;
            }
            finally
            {
                if (output != null) try { output.Close(); } catch { }
                if (input != null) try { input.Close(); } catch { }
                try { resp.Close(); } catch { }
            }
        }

        static HttpWebResponse GetManual(string url, long maxBytes, bool manifest, out HttpWebRequest request)
        {
            request = null;
            Uri current;
            if (!Uri.TryCreate(url, UriKind.Absolute, out current))
                throw new InvalidOperationException("Update URL is empty.");
            int followed = 0;
            while (true)
            {
                request = MakeRequest(current.AbsoluteUri);
                HttpWebResponse resp = GetHttp(request);
                int status = (int)resp.StatusCode;
                if (IsRedirectStatus(status))
                {
                    Uri next;
                    string redirReason = ConsumeRedirect(resp, current, followed, out next);
                    CloseQuiet(resp);
                    if (redirReason != null)
                        throw new UpdateFeedRefusedException(redirReason);
                    current = next;
                    followed++;
                    continue;
                }
                if (status < 200 || status >= 300)
                {
                    string desc = resp.StatusDescription ?? "";
                    CloseQuiet(resp);
                    throw new InvalidOperationException("HTTP " + status + " " + desc);
                }
                if (resp.ContentLength >= 0 && resp.ContentLength > maxBytes)
                {
                    try { request.Abort(); } catch { }
                    CloseQuiet(resp);
                    throw new UpdateFeedRefusedException(TooLargeMessage(manifest, maxBytes));
                }
                return resp;
            }
        }

        static HttpWebResponse GetHttp(HttpWebRequest req)
        {
            try
            {
                return (HttpWebResponse)req.GetResponse();
            }
            catch (WebException ex)
            {
                HttpWebResponse http = ex.Response as HttpWebResponse;
                if (http == null) throw;
                int code = (int)http.StatusCode;
                if (IsRedirectStatus(code)) return http;
                string desc = http.StatusDescription ?? "";
                CloseQuiet(http);
                throw new InvalidOperationException("HTTP " + code + " " + desc);
            }
        }

        static string ConsumeRedirect(HttpWebResponse resp, Uri current, int followed, out Uri next)
        {
            next = current;
            if (followed >= MaxRedirects)
                return "Redirected more than " + MaxRedirects.ToString() + " times.";
            string loc = resp.Headers["Location"];
            if (string.IsNullOrEmpty(loc))
                return "Redirect was missing a Location.";
            Uri resolved;
            if (!Uri.TryCreate(current, loc, out resolved) || resolved == null)
                return "Redirect Location could not be read.";
            string why;
            if (!RedirectAllowed(current, resolved, out why))
                return why;
            next = resolved;
            return null;
        }

        static bool IsRedirectStatus(int status)
        {
            return status == 301 || status == 302 || status == 303 || status == 307 || status == 308;
        }

        static byte[] ReadStreamCapped(Stream input, long maxBytes, bool manifest)
        {
            using (var ms = new MemoryStream())
            {
                CopyCapped(input, ms, null, maxBytes, manifest);
                return ms.ToArray();
            }
        }

        static string StreamTo(Stream input, Stream output, long maxBytes, bool manifest)
        {
            using (SHA256 sha = SHA256.Create())
            {
                long total = CopyCapped(input, output, sha, maxBytes, manifest);
                if (total <= 0)
                    throw new InvalidOperationException("Update download was empty.");
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return Hex(sha.Hash);
            }
        }

        static long CopyCapped(Stream input, Stream output, SHA256 sha, long maxBytes, bool manifest)
        {
            byte[] buf = new byte[8192];
            long total = 0;
            while (true)
            {
                int n = input.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                if (total > maxBytes - n)
                    throw new UpdateFeedRefusedException(TooLargeMessage(manifest, maxBytes));
                total += n;
                if (output != null) output.Write(buf, 0, n);
                if (sha != null) sha.TransformBlock(buf, 0, n, buf, 0);
            }
            return total;
        }

        static string TooLargeMessage(bool manifest, long maxBytes)
        {
            if (manifest)
                return "Update feed response is larger than the 64 KB limit.";
            if (maxBytes == MaxPayloadBytes)
                return "Update download is larger than the 32 MB limit.";
            return "Update download is larger than the " + maxBytes.ToString() + " byte limit.";
        }

        static Exception AsPayloadFailure(Exception ex)
        {
            string msg = ex == null || ex.Message == null ? "" : ex.Message;
            if (msg.IndexOf("Update download failed.", StringComparison.Ordinal) >= 0
                && msg.IndexOf("shop Setup", StringComparison.Ordinal) >= 0
                && msg.IndexOf("never uses a GitHub token", StringComparison.Ordinal) >= 0)
                return ex;
            return new InvalidOperationException(PayloadFailureText(msg));
        }

        static string DescribeEndpoint(Uri uri)
        {
            if (uri == null) return "";
            string host = uri.Host ?? "";
            if (uri.IsDefaultPort)
                return (uri.Scheme ?? "") + "://" + host;
            return (uri.Scheme ?? "") + "://" + host + ":" + uri.Port.ToString();
        }

        static bool IsFileUrl(string url)
        {
            return url.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsFileScheme(string scheme)
        {
            return string.Equals(scheme, "file", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsHttpScheme(string scheme)
        {
            return string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase);
        }

        static void CloseQuiet(HttpWebResponse resp)
        {
            if (resp == null) return;
            try { resp.Close(); } catch { }
        }

        public sealed class UpdateFeedRefusedException : InvalidOperationException
        {
            public UpdateFeedRefusedException(string message) : base(message ?? "") { }
        }
    }
}
