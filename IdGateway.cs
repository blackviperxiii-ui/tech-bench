using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
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

        public bool Ok { get { return Status >= 200 && Status < 300; } }
    }

    public sealed class IdCallResult
    {
        public bool Posted;
        public string Message = "";
        public int Status;
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
                return p;
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

        static string Token(IdSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.TokenUrl)
                || string.IsNullOrWhiteSpace(settings.ClientId)
                || string.IsNullOrWhiteSpace(settings.ClientSecret))
                return "";
            var req = new IdHttpRequest
            {
                Method = "POST",
                Url = settings.TokenUrl,
                ContentType = "application/x-www-form-urlencoded",
                Body = "grant_type=client_credentials&client_id=" + Uri.EscapeDataString(settings.ClientId)
                    + "&client_secret=" + Uri.EscapeDataString(settings.ClientSecret)
            };
            IdHttpResponse resp = HttpOverride != null ? HttpOverride(req) : RealSend(req);
            if (resp == null || !resp.Ok || string.IsNullOrEmpty(resp.Body)) return "";
            try
            {
                var ser = new JavaScriptSerializer();
                var map = ser.Deserialize<Dictionary<string, object>>(resp.Body);
                if (map != null && map.ContainsKey("access_token") && map["access_token"] != null)
                    return map["access_token"].ToString();
            }
            catch { }
            return "";
        }

        static IdHttpResponse RealSend(IdHttpRequest req)
        {
            var result = new IdHttpResponse();
            try
            {
                try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; }
                catch { }
                var http = (HttpWebRequest)WebRequest.Create(req.Url);
                http.Method = req.Method;
                http.Timeout = 20000;
                foreach (KeyValuePair<string, string> h in req.Headers)
                    http.Headers[h.Key] = h.Value;
                if (!string.IsNullOrEmpty(req.FilePath) && File.Exists(req.FilePath))
                    WriteMultipart(http, req);
                else if (!string.Equals(req.Method, "GET", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(req.Body))
                {
                    http.ContentType = string.IsNullOrEmpty(req.ContentType) ? "application/json" : req.ContentType;
                    byte[] bytes = Encoding.UTF8.GetBytes(req.Body);
                    http.ContentLength = bytes.Length;
                    using (Stream s = http.GetRequestStream())
                        s.Write(bytes, 0, bytes.Length);
                }
                using (var resp = (HttpWebResponse)http.GetResponse())
                {
                    result.Status = (int)resp.StatusCode;
                    using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                        result.Body = r.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                result.Error = ex.Message;
                HttpWebResponse resp = ex.Response as HttpWebResponse;
                if (resp != null)
                {
                    result.Status = (int)resp.StatusCode;
                    try
                    {
                        using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                            result.Body = r.ReadToEnd();
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            return result;
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

        static string Fail(string what, IdHttpResponse resp)
        {
            string extra = "";
            if (resp == null) extra = "no response";
            else if (!string.IsNullOrEmpty(resp.Error)) extra = resp.Error;
            else extra = "HTTP " + resp.Status;
            if (resp != null && !string.IsNullOrEmpty(resp.Body) && resp.Body.Length < 240)
                extra += "  " + resp.Body.Trim();
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
