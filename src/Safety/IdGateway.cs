using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace TechBench
{
    public sealed class IdHttpRequest
    {
        public string Method = "GET";
        public string Url = "";
        public string Body = "";
        public string ContentType = "application/json";
        public string FilePath = "";
        public readonly Dictionary<string, string> Headers = new Dictionary<string, string>();
    }

    public sealed class IdHttpResponse
    {
        public int Status;
        public string Body = "";
        public string Error = "";
        /// <summary>Seconds from a 429 Retry-After header (tests may set directly).</summary>
        public int RetryAfterSeconds;

        public bool Ok
        {
            get { return Status == 200 || Status == 201 || Status == 204; }
        }
    }

    public sealed class IdCallResult
    {
        public bool Posted;
        public string Message = "";
        public int Status;
        public bool NeedsReSignIn;
        public readonly List<WorkOrder> WorkOrders = new List<WorkOrder>();
    }

    /// <summary>
    /// IntelliDealer Azure API Gateway client. Calls only when URL + credentials exist.
    /// Sign-off / payroll never reports success unless the gateway returns HTTP 2xx.
    /// </summary>
    public static class IdGateway
    {
        /// <summary>Tests replace the real HTTP stack. Production leaves this null.</summary>
        public static Func<IdHttpRequest, IdHttpResponse> HttpOverride;

        static string _token;
        static DateTime _tokenExpiryUtc;
        static string _tokenStamp;
        static string _lastTokenError = "";

        /// <summary>Last OAuth token failure reason (empty when the last Token() call succeeded or was skipped).</summary>
        public static string LastTokenError
        {
            get { return _lastTokenError ?? ""; }
        }

        public static void ClearTokenCache()
        {
            _token = null;
            _tokenExpiryUtc = DateTime.MinValue;
            _tokenStamp = null;
            _lastTokenError = "";
        }

        public static string Expand(string template, IdSettings s, WorkOrder wo)
        {
            string path = template ?? "";
            string tech = s == null ? "" : (string.IsNullOrWhiteSpace(s.TechNumber) ? s.TechId : s.TechNumber);
            string woNo = wo == null ? "" : wo.Number;
            string seg = wo == null ? "" : wo.Segment;
            path = Replace(path, "{tech}", tech);
            path = Replace(path, "{wo}", woNo);
            path = Replace(path, "{segment}", seg);
            path = Replace(path, "{company}", s == null ? "" : s.Company);
            path = Replace(path, "{location}", s == null ? "" : s.Location);
            return path;
        }

        public static string CombineUrl(string gateway, string path)
        {
            string g = (gateway ?? "").Trim().TrimEnd('/');
            string p = (path ?? "").Trim();
            if (g.Length == 0) return "";
            if (p.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || p.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Absolute op paths may only stay on the configured gateway host.
                // Off-host absolute URLs used to send the subscription key / Bearer elsewhere.
                Uri gateUri, pathUri;
                if (!Uri.TryCreate(g, UriKind.Absolute, out gateUri)
                    || !Uri.TryCreate(p, UriKind.Absolute, out pathUri))
                    return "";
                if (!string.Equals(gateUri.Host ?? "", pathUri.Host ?? "", StringComparison.OrdinalIgnoreCase))
                    return "";
                if (gateUri.Port != pathUri.Port)
                    return "";
                if (string.Equals(gateUri.Scheme, "https", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(pathUri.Scheme, "http", StringComparison.OrdinalIgnoreCase))
                    return "";
                return p;
            }
            if (!p.StartsWith("/")) p = "/" + p;
            return g + p;
        }

        public static string MissingGatewayMessage()
        {
            return "IntelliDealer Azure API Gateway is not configured. Paste the credentials from "
                + "IntelliDealer → Configuration → API Gateway into Shop → IntelliDealer. "
                + "Assigned work orders stay file-backed until then. Sign-off is not posted to the DMS.";
        }

        public static IdCallResult FetchAssigned(IdSettings settings)
        {
            var result = new IdCallResult();
            if (settings == null || !settings.GatewayConfigured())
            {
                result.Message = MissingGatewayMessage();
                return result;
            }
            string url = CombineUrl(settings.GatewayUrl, Expand(settings.AssignedPath, settings, null));
            IdHttpResponse resp = Send(settings, "GET", url, null, null);
            result.Status = resp.Status;
            if (resp != null && resp.Status == 401) result.NeedsReSignIn = true;
            if (!resp.Ok)
            {
                result.Message = Fail("assigned work orders", resp);
                return result;
            }
            foreach (WorkOrder wo in WorkOrderStore.ParseAssignedJson(resp.Body))
            {
                wo.Source = "gateway";
                result.WorkOrders.Add(wo);
            }
            result.Posted = true;
            result.Message = "Loaded " + result.WorkOrders.Count + " assigned work order(s) from IntelliDealer.";
            return result;
        }

        public static IdCallResult LogOn(IdSettings settings, WorkOrder wo)
        {
            return Clock(settings, wo, settings == null ? "" : settings.LogOnPath, "logon",
                "IntelliDealer log on");
        }

        public static IdCallResult LogOff(IdSettings settings, WorkOrder wo)
        {
            return Clock(settings, wo, settings == null ? "" : settings.LogOffPath, "logoff",
                "IntelliDealer log off");
        }

        public static IdCallResult SignOff(IdSettings settings, WorkOrder wo)
        {
            var result = new IdCallResult();
            if (settings == null || !settings.GatewayConfigured())
            {
                result.Message = "Sign-off is an IntelliDealer payroll/billing action. "
                    + MissingGatewayMessage();
                return result;
            }
            if (wo == null || string.IsNullOrWhiteSpace(wo.Number))
            {
                result.Message = "Pick a work order before signing off.";
                return result;
            }
            return Clock(settings, wo, settings.SignOffPath, "signoff", "IntelliDealer sign off");
        }

        public static IdCallResult PostMultimedia(IdSettings settings, WorkOrder wo, string filePath)
        {
            var result = new IdCallResult();
            if (settings == null || !settings.GatewayConfigured())
            {
                result.Message = "Multimedia stays in the Tech Bench packet until the API Gateway is configured.";
                return result;
            }
            if (wo == null || string.IsNullOrWhiteSpace(wo.Number))
            {
                result.Message = "Pick a work order before posting multimedia.";
                return result;
            }
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                result.Message = "No file to post.";
                return result;
            }
            string url = CombineUrl(settings.GatewayUrl, Expand(settings.MultimediaPath, settings, wo));
            IdHttpResponse resp = Send(settings, "POST", url, null, filePath);
            result.Status = resp.Status;
            if (resp != null && resp.Status == 401) result.NeedsReSignIn = true;
            if (!resp.Ok)
            {
                result.Message = Fail("multimedia post-back", resp);
                return result;
            }
            result.Posted = true;
            result.Message = "Posted " + Path.GetFileName(filePath) + " to IntelliDealer.";
            return result;
        }

        static IdCallResult Clock(IdSettings settings, WorkOrder wo, string path, string kind, string label)
        {
            var result = new IdCallResult();
            if (settings == null || !settings.GatewayConfigured())
            {
                result.Message = label + " was not sent to IntelliDealer. " + MissingGatewayMessage();
                return result;
            }
            if (wo == null || string.IsNullOrWhiteSpace(wo.Number))
            {
                result.Message = "Pick a work order first.";
                return result;
            }
            string url = CombineUrl(settings.GatewayUrl, Expand(path, settings, wo));
            string body = ClockBody(settings, wo, kind);
            IdHttpResponse resp = Send(settings, "POST", url, body, null);
            result.Status = resp.Status;
            if (resp != null && resp.Status == 401) result.NeedsReSignIn = true;
            if (!resp.Ok)
            {
                result.Message = Fail(label, resp);
                return result;
            }
            result.Posted = true;
            result.Message = label + " posted to IntelliDealer.";
            return result;
        }

        static string ClockBody(IdSettings settings, WorkOrder wo, string kind)
        {
            var ser = new JavaScriptSerializer();
            var map = new Dictionary<string, object>();
            map["workOrder"] = wo.Number;
            map["segment"] = wo.Segment ?? "";
            map["technician"] = string.IsNullOrWhiteSpace(settings.TechNumber) ? settings.TechId : settings.TechNumber;
            map["company"] = settings.Company ?? "";
            map["location"] = settings.Location ?? "";
            map["action"] = kind;
            map["whenUtc"] = DateTime.UtcNow.ToString("o");
            return ser.Serialize(map);
        }

        static IdHttpResponse Send(IdSettings settings, string method, string url, string body, string filePath)
        {
            const int maxRateRetries = 5;
            for (int attempt = 0; attempt <= maxRateRetries; attempt++)
            {
                IdHttpResponse resp = SendOnce(settings, method, url, body, filePath);
                if (resp == null) return new IdHttpResponse();
                if (resp.Ok || resp.Status == 401 || resp.Status == 403) return resp;
                if (resp.Status == 429 && attempt < maxRateRetries)
                {
                    Thread.Sleep(RetryAfterMs(resp));
                    continue;
                }
                return resp;
            }
            return new IdHttpResponse { Status = 429, Error = "rate limited" };
        }

        static IdHttpResponse SendOnce(IdSettings settings, string method, string url, string body, string filePath)
        {
            var req = new IdHttpRequest
            {
                Method = method,
                Url = url,
                Body = body ?? "",
                FilePath = filePath ?? ""
            };
            if (!string.IsNullOrWhiteSpace(settings.SubscriptionKey))
                req.Headers["Ocp-Apim-Subscription-Key"] = settings.SubscriptionKey;
            string token = Token(settings);
            if (!string.IsNullOrEmpty(token))
                req.Headers["Authorization"] = "Bearer " + token;

            if (HttpOverride != null) return HttpOverride(req);
            return RealSend(req);
        }

        /// <summary>
        /// Backoff for 429. Honor Retry-After when present; otherwise use a floor so
        /// missing/zero headers cannot tight-loop the gateway.
        /// </summary>
        static int RetryAfterMs(IdHttpResponse resp)
        {
            const int minMs = 250;
            if (resp != null && resp.RetryAfterSeconds > 0)
            {
                int fromHeader = resp.RetryAfterSeconds * 1000;
                return fromHeader < minMs ? minMs : fromHeader;
            }
            return minMs;
        }

        public static string Token(IdSettings settings)
        {
            if (settings == null
                || string.IsNullOrWhiteSpace(settings.TokenUrl)
                || string.IsNullOrWhiteSpace(settings.ClientId)
                || string.IsNullOrWhiteSpace(settings.ClientSecret))
            {
                _lastTokenError = "";
                return "";
            }
            string stamp = settings.TokenUrl + "\n" + settings.ClientId + "\n" + settings.ClientSecret;
            if (!string.IsNullOrEmpty(_token)
                && stamp == _tokenStamp
                && DateTime.UtcNow.AddSeconds(60) < _tokenExpiryUtc)
            {
                _lastTokenError = "";
                return _token;
            }

            var req = new IdHttpRequest
            {
                Method = "POST",
                Url = settings.TokenUrl,
                ContentType = "application/x-www-form-urlencoded",
                Body = "grant_type=client_credentials&client_id=" + Uri.EscapeDataString(settings.ClientId)
                    + "&client_secret=" + Uri.EscapeDataString(settings.ClientSecret)
            };
            IdHttpResponse resp = HttpOverride != null ? HttpOverride(req) : RealSend(req);
            if (resp == null || !resp.Ok || string.IsNullOrEmpty(resp.Body))
            {
                if (resp == null) _lastTokenError = "OAuth token request returned no response.";
                else if (!string.IsNullOrEmpty(resp.Error))
                    _lastTokenError = "OAuth token request failed: " + resp.Error;
                else
                    _lastTokenError = "OAuth token request failed (HTTP " + resp.Status + ").";
                return "";
            }
            try
            {
                var ser = new JavaScriptSerializer();
                var map = ser.Deserialize<Dictionary<string, object>>(resp.Body);
                if (map == null || !map.ContainsKey("access_token") || map["access_token"] == null)
                {
                    _lastTokenError = "OAuth token response had no access_token.";
                    return "";
                }
                string access = map["access_token"].ToString();
                if (string.IsNullOrEmpty(access))
                {
                    _lastTokenError = "OAuth token response had an empty access_token.";
                    return "";
                }
                int seconds = 3600;
                if (map.ContainsKey("expires_in") && map["expires_in"] != null)
                {
                    try { seconds = Convert.ToInt32(map["expires_in"]); }
                    catch { seconds = 3600; }
                }
                if (seconds < 1) seconds = 1;
                _token = access;
                _tokenStamp = stamp;
                _tokenExpiryUtc = DateTime.UtcNow.AddSeconds(seconds);
                _lastTokenError = "";
                return _token;
            }
            catch (Exception ex)
            {
                _lastTokenError = "OAuth token response was not usable: " + ex.Message;
                return "";
            }
        }

        const int MaxGatewayRedirects = 5;
        /// <summary>Cap gateway response text so a runaway body cannot fill memory.</summary>
        const int MaxGatewayBodyChars = 4 * 1024 * 1024;

        static string ReadBodyCapped(Stream stream)
        {
            if (stream == null) return "";
            var sb = new StringBuilder();
            var buf = new char[8192];
            using (var r = new StreamReader(stream, Encoding.UTF8))
            {
                int n;
                while ((n = r.Read(buf, 0, buf.Length)) > 0)
                {
                    int room = MaxGatewayBodyChars - sb.Length;
                    if (room <= 0) break;
                    if (n > room) n = room;
                    sb.Append(buf, 0, n);
                    if (sb.Length >= MaxGatewayBodyChars) break;
                }
            }
            return sb.ToString();
        }

        static IdHttpResponse RealSend(IdHttpRequest req)
        {
            var result = new IdHttpResponse();
            try
            {
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; }
                catch { }
                Uri current;
                if (req == null || !Uri.TryCreate(req.Url, UriKind.Absolute, out current))
                {
                    result.Error = "Gateway URL is empty or relative.";
                    return result;
                }
                var headers = new Dictionary<string, string>(req.Headers);
                string method = req.Method ?? "GET";
                string body = req.Body ?? "";
                string contentType = req.ContentType;
                string filePath = req.FilePath ?? "";
                int followed = 0;
                while (true)
                {
                    var http = (HttpWebRequest)WebRequest.Create(current);
                    http.Method = method;
                    http.Timeout = 20000;
                    http.AllowAutoRedirect = false;
                    http.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    http.KeepAlive = false;
                    if (current.IsLoopback) http.Proxy = null;
                    http.UseDefaultCredentials = false;
                    http.Credentials = null;
                    http.PreAuthenticate = false;
                    foreach (KeyValuePair<string, string> h in headers)
                        http.Headers[h.Key] = h.Value;
                    if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    {
                        var multi = new IdHttpRequest { FilePath = filePath, Method = method };
                        WriteMultipart(http, multi);
                    }
                    else if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(body))
                    {
                        http.ContentType = string.IsNullOrEmpty(contentType) ? "application/json" : contentType;
                        byte[] bytes = Encoding.UTF8.GetBytes(body);
                        http.ContentLength = bytes.Length;
                        using (Stream s = http.GetRequestStream())
                            s.Write(bytes, 0, bytes.Length);
                    }
                    HttpWebResponse resp = null;
                    try
                    {
                        resp = (HttpWebResponse)http.GetResponse();
                        result.Error = "";
                    }
                    catch (WebException ex)
                    {
                        result.Error = ex.Message;
                        resp = ex.Response as HttpWebResponse;
                        if (resp == null) return result;
                    }
                    int status = (int)resp.StatusCode;
                    if (IsRedirectStatus(status))
                    {
                        Uri next;
                        string why = ConsumeRedirect(resp, current, followed, out next);
                        try { resp.Close(); } catch { }
                        if (why != null)
                        {
                            result.Error = why;
                            result.Status = status;
                            return result;
                        }
                        // 307/308 must keep method+body; wiping to GET dropped POST payloads
                        // (log-on / sign-off / multimedia) while still returning a later 200.
                        string followWhy = RedirectFollowError(status, method);
                        if (followWhy != null)
                        {
                            result.Error = followWhy;
                            result.Status = status;
                            return result;
                        }
                        current = next;
                        followed++;
                        // 301/302/303: browsers switch to GET without a body. 307/308 keep method
                        // (only GET/HEAD reach here after RedirectFollowError).
                        if (status == 301 || status == 302 || status == 303)
                        {
                            method = "GET";
                            body = "";
                            filePath = "";
                        }
                        continue;
                    }
                    result.Status = status;
                    result.RetryAfterSeconds = ParseRetryAfterHeader(resp.Headers["Retry-After"]);
                    try
                    {
                        result.Body = ReadBodyCapped(resp.GetResponseStream());
                    }
                    catch { }
                    try { resp.Close(); } catch { }
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            return result;
        }

        static bool IsRedirectStatus(int status)
        {
            return status == 301 || status == 302 || status == 303 || status == 307 || status == 308;
        }

        /// <summary>
        /// Whether RealSend may follow a redirect for this method.
        /// 307/308 on non-GET/HEAD are refused — replaying POST bodies needs an explicit
        /// same-host policy, and silently converting to GET drops the payload.
        /// Returns null when follow is allowed; otherwise a clear error string.
        /// </summary>
        public static string RedirectFollowError(int status, string method)
        {
            if (status != 307 && status != 308) return null;
            string m = string.IsNullOrEmpty(method) ? "GET" : method.Trim().ToUpperInvariant();
            if (m == "GET" || m == "HEAD") return null;
            return "Gateway " + status + " redirect after " + m
                + " was refused (payload would not be resent).";
        }

        /// <summary>
        /// SelfTest hook: real HTTP stack (does not use <see cref="HttpOverride"/>).
        /// </summary>
        public static IdHttpResponse SendRaw(IdHttpRequest req)
        {
            return RealSend(req);
        }

        static string ConsumeRedirect(HttpWebResponse resp, Uri from, int followed, out Uri next)
        {
            next = null;
            if (followed >= MaxGatewayRedirects)
                return "Gateway redirect limit (" + MaxGatewayRedirects + ") was exceeded.";
            string location = resp == null ? null : resp.Headers["Location"];
            if (string.IsNullOrWhiteSpace(location))
                return "Gateway redirect had no Location header.";
            if (!Uri.TryCreate(from, location, out next))
                return "Gateway redirect Location could not be read.";
            string reason;
            if (!RedirectAllowed(from, next, out reason))
                return reason;
            return null;
        }

        /// <summary>
        /// Same host and port. http may upgrade to https on default ports.
        /// https to http is refused.
        /// </summary>
        public static bool RedirectAllowed(Uri from, Uri to, out string reason)
        {
            reason = "";
            if (from == null || to == null)
            {
                reason = "Gateway redirect target was missing.";
                return false;
            }
            if (!string.Equals(from.Scheme, to.Scheme, StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(from.Scheme, "https", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(to.Scheme, "http", StringComparison.OrdinalIgnoreCase))
                {
                    reason = "Gateway redirect from https to http was refused.";
                    return false;
                }
                bool upgrade = string.Equals(from.Scheme, "http", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(to.Scheme, "https", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(from.Host ?? "", to.Host ?? "", StringComparison.OrdinalIgnoreCase)
                    && from.IsDefaultPort
                    && to.IsDefaultPort;
                if (upgrade) return true;
                reason = "Gateway redirect to another scheme was refused.";
                return false;
            }
            if (!string.Equals(from.Host ?? "", to.Host ?? "", StringComparison.OrdinalIgnoreCase))
            {
                reason = "Gateway redirect to another host was refused.";
                return false;
            }
            if (from.Port != to.Port)
            {
                reason = "Gateway redirect to another port was refused.";
                return false;
            }
            return true;
        }

        static void WriteMultipart(HttpWebRequest http, IdHttpRequest req)
        {
            string boundary = "----TechBench" + DateTime.UtcNow.Ticks;
            http.ContentType = "multipart/form-data; boundary=" + boundary;
            http.Method = "POST";
            string name = Path.GetFileName(req.FilePath);
            byte[] file = File.ReadAllBytes(req.FilePath);
            var sb = new StringBuilder();
            sb.Append("--").Append(boundary).Append("\r\n");
            sb.Append("Content-Disposition: form-data; name=\"file\"; filename=\"").Append(name).Append("\"\r\n");
            sb.Append("Content-Type: application/octet-stream\r\n\r\n");
            byte[] head = Encoding.UTF8.GetBytes(sb.ToString());
            byte[] tail = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--\r\n");
            http.ContentLength = head.Length + file.Length + tail.Length;
            using (Stream s = http.GetRequestStream())
            {
                s.Write(head, 0, head.Length);
                s.Write(file, 0, file.Length);
                s.Write(tail, 0, tail.Length);
            }
        }

        static int ParseRetryAfterHeader(string header)
        {
            if (string.IsNullOrWhiteSpace(header)) return 0;
            header = header.Trim();
            int seconds;
            if (int.TryParse(header, out seconds) && seconds >= 0) return seconds;
            DateTime when;
            if (DateTime.TryParse(header, out when))
            {
                double delta = (when.ToUniversalTime() - DateTime.UtcNow).TotalSeconds;
                if (delta < 0) return 0;
                if (delta > int.MaxValue) return int.MaxValue;
                return (int)Math.Ceiling(delta);
            }
            return 0;
        }

        static string Fail(string what, IdHttpResponse resp)
        {
            if (resp != null && resp.Status == 401)
                return "IntelliDealer rejected the API credentials (HTTP 401). "
                    + "Open Shop → IntelliDealer and sign in again with a valid subscription key "
                    + "and OAuth secret if your dealer uses client credentials.";
            string extra = "";
            if (resp == null) extra = "no response";
            else if (!string.IsNullOrEmpty(resp.Error)) extra = resp.Error;
            else extra = "HTTP " + resp.Status;
            if (resp != null && !string.IsNullOrEmpty(resp.Body) && resp.Body.Length < 240)
                extra += "  " + resp.Body.Trim();
            if (!string.IsNullOrEmpty(_lastTokenError))
                extra += "  " + _lastTokenError;
            return "IntelliDealer did not accept " + what + " (" + extra
                + "). Tech Bench did not record this as a DMS success.";
        }

        static string Replace(string s, string token, string value)
        {
            if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(token)) return s ?? "";
            return s.Replace(token, value ?? "");
        }
    }
}
