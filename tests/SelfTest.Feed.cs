// Offline checks for the parts of Tech Bench that can be verified without an adapter.
// Includes the shipped field database under kb\. Run with test.bat.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using J1939Reader;
using TechBench;

static partial class SelfTest
{
    [SelfTestCase("updater", Order = 230)]
    static void UpdaterTests()
    {
        Eq("stamped version", AppVersion.Number, "1.2.19");
        Check("1.2.5 is newer than 1.2.4", Updater.IsNewer("1.2.5", "1.2.4"), "strict greater");
        Check("same 1.2.5 is not newer", !Updater.IsNewer("1.2.5", "1.2.5"), "strict same");
        Version parsed;
        Check("current version parses", Updater.TryParseVersion(AppVersion.Number, out parsed), "parse failed");
        Check("1.3.0 is newer", Updater.IsNewer("1.3.0", "1.2.0"), "1.3.0 vs 1.2.0");
        Check("v1.2.1 is newer", Updater.IsNewer("v1.2.1", "1.2.0"), "v prefix");
        Check("same version is not newer", !Updater.IsNewer("1.2.0", "1.2.0"), "same");
        Check("older is not newer", !Updater.IsNewer("1.1.9", "1.2.0"), "older");
        Check("garbage version is not newer", !Updater.IsNewer("nope", "1.2.0"), "garbage");

        var m = Updater.ParseManifest(
            @"{""version"":""1.2.0"",""sha256"":""0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"",""url"":""https://example.com/TechBench.exe"",""notes"":""fix""}");
        Check("manifest parses", m != null, "null");
        if (m != null)
        {
            Eq("manifest version", m.Version, "1.2.0");
            Eq("manifest url", m.Url, "https://example.com/TechBench.exe");
            Eq("manifest notes", m.Notes, "fix");
            Eq("manifest hash length", m.Sha256.Length, 64);
        }
        Check("spaced hash normalizes",
            Updater.NormalizeHash("AB CD") == "abcd", Updater.NormalizeHash("AB CD"));
        Check("short hash rejected",
            Updater.ParseManifest(@"{""version"":""1"",""sha256"":""abc"",""url"":""http://x""}") == null, "accepted short hash");
        Check("missing url rejected",
            Updater.ParseManifest(@"{""version"":""1.2.0"",""sha256"":""0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef""}") == null,
            "accepted");

        byte[] payload = Encoding.UTF8.GetBytes("tech-bench-update-bytes");
        string hash = Updater.Sha256Bytes(payload);
        Eq("sha256 hex length", hash.Length, 64);
        string dir = Path.Combine(Path.GetTempPath(), "tb-upd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "blob.bin");
        File.WriteAllBytes(file, payload);
        Check("file hash matches bytes", Updater.HashMatches(file, hash), Updater.Sha256File(file));
        Check("wrong hash fails", !Updater.HashMatches(file, "ff" + hash.Substring(2)), "matched");

        Updater.Stage(dir, payload, hash);
        Check("staged pending verifies", Updater.HasVerifiedPending(dir), "missing sidecar or mismatch");
        File.WriteAllText(Path.Combine(dir, Updater.HashSidecar), "0");
        Check("tampered sidecar fails", !Updater.HasVerifiedPending(dir), "still verified");
        Check("cannot apply mid-session", !Updater.CanApplyNow(true), "allowed");
        Check("can apply when idle", Updater.CanApplyNow(false), "blocked");
        string script = Updater.ApplyScript();
        Check("cmd waits for process", script.Contains("TechBench.exe") && script.Contains(":wait"), "no wait");
        Check("cmd swaps hashed exe", script.Contains("TechBench.exe.new") && script.Contains("move /Y"), "no swap");
        Check("swap stays in the exe folder", script.Contains("cd /d \"%~dp0\""), "no %~dp0");
        Check("cmd never mentions a token",
            script.IndexOf("ghp_", StringComparison.OrdinalIgnoreCase) < 0
            && script.IndexOf("Authorization", StringComparison.OrdinalIgnoreCase) < 0, "token");
        Check("default feed is public HTTPS",
            Updater.DefaultManifestUrl.StartsWith("https://"), Updater.DefaultManifestUrl);
        Check("default feed is public dist latest.json",
            Updater.DefaultManifestUrl == "https://github.com/blackviperxiii-ui/tech-bench-dist/releases/latest/download/latest.json",
            Updater.DefaultManifestUrl);
        Check("default feed is not the private source repo",
            Updater.DefaultManifestUrl.IndexOf("tech-bench/releases", StringComparison.OrdinalIgnoreCase) < 0,
            Updater.DefaultManifestUrl);

        // Newer defaults to false. That used to be the "is current" branch.
        var unset = new UpdateCheck();
        Check("unset check is not current", !Updater.IsCurrent(unset), Updater.StatusText(unset));
        Check("unset check reads as failed",
            Updater.StatusText(unset).IndexOf("Update check failed", StringComparison.Ordinal) >= 0
            && Updater.StatusText(unset).IndexOf("is current", StringComparison.Ordinal) < 0,
            Updater.StatusText(unset));
        var http404 = new UpdateCheck { Error = "HTTP 404 Not Found" };
        Check("404 is not current", !Updater.IsCurrent(http404) && !Updater.IsAvailable(http404), Updater.StatusText(http404));
        Check("404 reads as failed",
            Updater.StatusText(http404).IndexOf("Update check failed", StringComparison.Ordinal) >= 0
            && Updater.StatusText(http404).IndexOf("404", StringComparison.Ordinal) >= 0
            && Updater.StatusText(http404).IndexOf("is current", StringComparison.Ordinal) < 0,
            Updater.StatusText(http404));

        string missingUrl = new Uri(Path.Combine(dir, "missing-latest.json")).AbsoluteUri;
        Check("file url stays allowed", missingUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase), missingUrl);
        UpdateCheck missing = Updater.Check(missingUrl);
        Check("missing manifest is not current", !Updater.IsCurrent(missing), Updater.StatusText(missing));
        Check("missing manifest reads as failed",
            Updater.StatusText(missing).IndexOf("Update check failed", StringComparison.Ordinal) >= 0
            && Updater.StatusText(missing).IndexOf("is current", StringComparison.Ordinal) < 0,
            Updater.StatusText(missing));

        string badPath = Path.Combine(dir, "bad-latest.json");
        File.WriteAllText(badPath, "{ this is not json");
        UpdateCheck bad = Updater.Check(new Uri(badPath).AbsoluteUri);
        Check("unreadable latest.json is not current", !Updater.IsCurrent(bad), Updater.StatusText(bad));
        Check("unreadable latest.json reads as failed",
            Updater.StatusText(bad).IndexOf("Update check failed", StringComparison.Ordinal) >= 0
            && Updater.StatusText(bad).IndexOf("is current", StringComparison.Ordinal) < 0,
            Updater.StatusText(bad));

        string junkVer = Path.Combine(dir, "junk-ver.json");
        File.WriteAllText(junkVer, ManifestJson("nope", "http://127.0.0.1/TechBench.exe"));
        UpdateCheck junk = Updater.Check(new Uri(junkVer).AbsoluteUri);
        Check("unreadable version is not current", !Updater.IsCurrent(junk), Updater.StatusText(junk));

        string samePath = Path.Combine(dir, "same-latest.json");
        File.WriteAllText(samePath, ManifestJson(AppVersion.Number, "file:///C:/TechBench.exe"));
        string sameUrl = new Uri(samePath).AbsoluteUri;
        UpdateCheck same = Updater.Check(sameUrl);
        Check("same version file feed is current", Updater.IsCurrent(same), Updater.StatusText(same));
        Check("file feed records the manifest url",
            same.Manifest != null && same.Manifest.SourceUrl == sameUrl,
            same.Manifest == null ? Updater.StatusText(same) : (same.Manifest.SourceUrl ?? "(null source)"));
        Check("same version says current",
            Updater.StatusText(same).IndexOf("is current", StringComparison.Ordinal) >= 0, Updater.StatusText(same));

        string newerPath = Path.Combine(dir, "newer-latest.json");
        File.WriteAllText(newerPath, ManifestJson("9.9.9", "http://127.0.0.1/TechBench.exe"));
        UpdateCheck newer = Updater.Check(new Uri(newerPath).AbsoluteUri);
        Check("newer file feed is available", Updater.IsAvailable(newer) && !Updater.IsCurrent(newer), Updater.StatusText(newer));
        Check("newer file feed is not worded as current",
            Updater.StatusText(newer).IndexOf("is current", StringComparison.Ordinal) < 0, Updater.StatusText(newer));
        Check("same-version success path still says current",
            Updater.IsCurrent(same) && Updater.StatusText(same).IndexOf("is current", StringComparison.Ordinal) >= 0,
            Updater.StatusText(same));
        Check("newer success path still says available",
            Updater.IsAvailable(newer)
            && Updater.StatusText(newer).IndexOf("is available", StringComparison.Ordinal) >= 0,
            Updater.StatusText(newer));

        string shellPath = Path.Combine(FindRepoRoot(), "src", "Shell", "ShellForm.cs");
        string shell = File.Exists(shellPath) ? File.ReadAllText(shellPath) : "";
        Check("about does not claim a public latest.json",
            shell.IndexOf("public latest.json", StringComparison.OrdinalIgnoreCase) < 0, "public latest.json");
        Check("about names an anonymous manifest URL",
            shell.IndexOf("anonymous manifest", StringComparison.Ordinal) >= 0
            && shell.IndexOf("built-in URL", StringComparison.Ordinal) >= 0, "about");
        Check("about names shop feed overrides",
            shell.IndexOf("update-url.txt", StringComparison.Ordinal) >= 0
            && shell.IndexOf("update-feed-path.txt", StringComparison.Ordinal) >= 0, "about");
        Check("about still says no token and apply after quit",
            shell.IndexOf("never stores a GitHub token", StringComparison.Ordinal) >= 0
            && shell.IndexOf("apply after you quit", StringComparison.Ordinal) >= 0
            && shell.IndexOf("never mid-session", StringComparison.Ordinal) >= 0, "about");
        string updaterSrc = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Safety", "Updater.cs"));
        Check("updater does not tell the tech to host a public feed",
            updaterSrc.IndexOf("Host latest.json on a public HTTPS URL", StringComparison.Ordinal) < 0
            && updaterSrc.IndexOf("public feed", StringComparison.OrdinalIgnoreCase) < 0, "old copy");

        ShopShareFeedTests();
        HttpFeedTests();
        UpdaterHardeningTests();
        try { Directory.Delete(dir, true); } catch { }
    }

    static void CheckPrivateFeedFailure(string label, UpdateCheck result)
    {
        string text = Updater.StatusText(result);
        Check(label + " is not a success", !Updater.IsCurrent(result) && !Updater.IsAvailable(result), text);
        Check(label + " does not say up to date",
            text.IndexOf("up to date", StringComparison.OrdinalIgnoreCase) < 0
            && text.IndexOf("is current", StringComparison.OrdinalIgnoreCase) < 0,
            text);
        Check(label + " does not ask for a public feed",
            text.IndexOf("public HTTPS", StringComparison.OrdinalIgnoreCase) < 0
            && text.IndexOf("public feed", StringComparison.OrdinalIgnoreCase) < 0
            && text.IndexOf("Host latest.json", StringComparison.OrdinalIgnoreCase) < 0,
            text);
        Check(label + " names a private shop setup",
            text.IndexOf("Update feed unavailable", StringComparison.Ordinal) >= 0
            && text.IndexOf("never uses a GitHub token", StringComparison.Ordinal) >= 0
            && text.IndexOf("shop Setup", StringComparison.Ordinal) >= 0,
            text);
    }

    static string ManifestJson(string version, string url)
    {
        return "{\"version\":\"" + version
            + "\",\"sha256\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\",\"url\":\""
            + url + "\"}";
    }

    static string ManifestJson(string version, string url, string sha256)
    {
        return "{\"version\":\"" + version + "\",\"sha256\":\"" + sha256 + "\",\"url\":\"" + url + "\"}";
    }

    static void ShopShareFeedTests()
    {
        string prevExe = Updater.ExeDirOverride;
        string cfgDir = Path.Combine(Path.GetTempPath(), "tb-feed-cfg-" + Guid.NewGuid().ToString("N"));
        string share = Path.Combine(Path.GetTempPath(), "tb-feed-share-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cfgDir);
        Directory.CreateDirectory(share);
        Updater.ExeDirOverride = cfgDir;
        try
        {
            File.WriteAllText(Path.Combine(cfgDir, "update-feed-path.txt"), share);
            string manifestUrl = Updater.ShopShareManifestUrl();
            Check("shop share manifest url is a file uri",
                manifestUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase), manifestUrl);
            Eq("ManifestUrl uses shop share", Updater.ManifestUrl(null), manifestUrl);

            File.WriteAllText(Path.Combine(cfgDir, "update-url.txt"), "file:///Z:/override/latest.json");
            Check("update-url overrides shop share",
                Updater.ManifestUrl(null) == "file:///Z:/override/latest.json", Updater.ManifestUrl(null));
            File.Delete(Path.Combine(cfgDir, "update-url.txt"));

            byte[] payload = Encoding.UTF8.GetBytes("shop-share-techbench-payload");
            string sha = Updater.Sha256Bytes(payload);
            string exePath = Path.Combine(share, "TechBench.exe");
            File.WriteAllBytes(exePath, payload);
            string exeUrl = new Uri(exePath).AbsoluteUri;
            File.WriteAllText(Path.Combine(share, "latest.json"),
                ManifestJson("9.9.9", exeUrl, sha));

            UpdateCheck offer = Updater.Check(manifestUrl);
            Check("verified shop share offers newer build", Updater.IsAvailable(offer), Updater.StatusText(offer));
            Check("verified shop share is not called current",
                Updater.StatusText(offer).IndexOf("is current", StringComparison.Ordinal) < 0,
                Updater.StatusText(offer));

            File.WriteAllText(Path.Combine(share, "latest.json"),
                ManifestJson("9.9.9", exeUrl, new string('a', 64)));
            UpdateCheck badHash = Updater.Check(manifestUrl);
            Check("hash mismatch is not available", !Updater.IsAvailable(badHash), Updater.StatusText(badHash));
            Check("hash mismatch is not current", !Updater.IsCurrent(badHash), Updater.StatusText(badHash));
            Check("hash mismatch says could not check",
                Updater.StatusText(badHash).IndexOf("Couldn't check", StringComparison.Ordinal) >= 0
                && Updater.StatusText(badHash).IndexOf("is current", StringComparison.Ordinal) < 0,
                Updater.StatusText(badHash));

            File.Delete(Path.Combine(share, "latest.json"));
            UpdateCheck missing = Updater.Check(manifestUrl);
            Check("unreachable shop share is not current", !Updater.IsCurrent(missing), Updater.StatusText(missing));
            Check("unreachable shop share is not available", !Updater.IsAvailable(missing), Updater.StatusText(missing));
            Check("unreachable shop share says could not check",
                Updater.StatusText(missing).IndexOf("Couldn't check", StringComparison.Ordinal) >= 0
                && Updater.StatusText(missing).IndexOf("is current", StringComparison.Ordinal) < 0,
                Updater.StatusText(missing));

            File.WriteAllText(Path.Combine(cfgDir, "update-feed-path.txt"), "# comment only\n\n");
            string cfgErr = Updater.ShopShareConfigError();
            Check("comment-only feed path reports config error",
                cfgErr.IndexOf("usable", StringComparison.OrdinalIgnoreCase) >= 0
                || cfgErr.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0,
                cfgErr);
            Eq("comment-only feed path falls through to default manifest",
                Updater.ManifestUrl(null), Updater.DefaultManifestUrl);
            UpdateCheck badPath = Updater.Check(Updater.DefaultManifestUrl);
            string badText = Updater.StatusText(badPath);
            Check("bad feed path is not treated as private GitHub feed",
                badText.IndexOf("Couldn't check", StringComparison.Ordinal) >= 0
                && badText.IndexOf("private", StringComparison.OrdinalIgnoreCase) < 0,
                badText);
            Check("DescribeManifestSource names feed",
                Updater.DescribeManifestSource(null).IndexOf("feed", StringComparison.OrdinalIgnoreCase) >= 0,
                Updater.DescribeManifestSource(null));
        }
        finally
        {
            Updater.ExeDirOverride = prevExe;
            try { Directory.Delete(cfgDir, true); } catch { }
            try { Directory.Delete(share, true); } catch { }
        }
    }

    static void HttpFeedTests()
    {
        HttpListener listener = null;
        int port = 0;
        var rng = new Random();
        for (int i = 0; i < 8 && listener == null; i++)
        {
            port = 18000 + rng.Next(2000);
            var attempt = new HttpListener();
            try
            {
                attempt.Prefixes.Add("http://127.0.0.1:" + port + "/");
                attempt.Start();
                listener = attempt;
            }
            catch
            {
                try { attempt.Close(); } catch { }
            }
        }
        Check("local http feed", listener != null, "could not bind 127.0.0.1");
        if (listener == null) return;
        try
        {
            string root = "http://127.0.0.1:" + port + "/";
            UpdateCheck denied = CheckHttp(listener, 404, "missing", root + "latest.json");
            Check("http 404 is not current", !Updater.IsCurrent(denied), Updater.StatusText(denied));
            Check("http 404 reads as failed",
                Updater.StatusText(denied).IndexOf("Update check failed", StringComparison.Ordinal) >= 0
                && Updater.StatusText(denied).IndexOf("404", StringComparison.Ordinal) >= 0
                && Updater.StatusText(denied).IndexOf("is current", StringComparison.Ordinal) < 0,
                Updater.StatusText(denied));
            CheckPrivateFeedFailure("http 404", denied);

            UpdateCheck unauthorized = CheckHttp(listener, 401, "no", root + "unauth.json");
            CheckPrivateFeedFailure("http 401", unauthorized);
            UpdateCheck forbidden = CheckHttp(listener, 403, "no", root + "forbid.json");
            CheckPrivateFeedFailure("http 403", forbidden);

            UpdateCheck broken = CheckHttp(listener, 200, "{ not json", root + "bad.json");
            Check("http 200 unreadable json is not current", !Updater.IsCurrent(broken), Updater.StatusText(broken));

            UpdateCheck err = CheckHttp(listener, 500, "nope", root + "err.json");
            Check("http 500 is not current", !Updater.IsCurrent(err), Updater.StatusText(err));
            Check("http 500 reads as failed",
                Updater.StatusText(err).IndexOf("Update check failed", StringComparison.Ordinal) >= 0
                && Updater.StatusText(err).IndexOf("is current", StringComparison.Ordinal) < 0,
                Updater.StatusText(err));

            UpdateCheck offer = CheckHttp(listener, 200, ManifestJson("9.9.9", root + "TechBench.exe"), root + "ok.json");
            Check("http feed can report a newer build", Updater.IsAvailable(offer), Updater.StatusText(offer));
            Check("http feed records source url",
                offer.Manifest != null && offer.Manifest.SourceUrl == root + "ok.json",
                offer.Manifest == null ? Updater.StatusText(offer) : (offer.Manifest.SourceUrl ?? "(null source)"));
            Check("http is not forced to https", root.StartsWith("http://", StringComparison.OrdinalIgnoreCase), root);
        }
        finally
        {
            try { listener.Close(); } catch { }
        }
    }

    static UpdateCheck CheckHttp(HttpListener listener, int status, string body, string url)
    {
        Exception boom = null;
        var worker = new Thread(delegate()
        {
            try
            {
                HttpListenerContext ctx = listener.GetContext();
                byte[] bytes = Encoding.UTF8.GetBytes(body ?? "");
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                boom = ex;
            }
        });
        worker.IsBackground = true;
        worker.Start();
        UpdateCheck result = Updater.Check(url);
        bool finished = worker.Join(12000);
        if (boom != null)
            Check("http feed server", false, boom.Message);
        Check("http feed server finished", finished && !worker.IsAlive,
            (worker.IsAlive ? "still running" : "join timed out") + " " + (url ?? ""));
        return result;
    }

    static void UpdaterHardeningTests()
    {
        Eq("manifest cap is 64 KB", Updater.MaxManifestBytes, 65536L);
        Eq("payload cap is 32 MB", Updater.MaxPayloadBytes, 33554432L);
        Eq("redirect cap is 5", Updater.MaxRedirects, 5);
        RedirectRuleTests();
        PayloadOriginRuleTests();
        OversizeManifestTests();
        NearCapManifestTest();
        CrossHostRedirectTest();
        SameHostRedirectTest();
        TooManyRedirectsTest();
        PayloadOversizeTest(false);
        PayloadOversizeTest(true);
        PayloadHashMismatchTest();
        PayloadHappyPathTest();
        PayloadOriginNoRequestTest();
        HttpManifestOtherHostPayloadTest();
        SilentBackgroundTest();
    }

    static void RedirectRuleTests()
    {
        string why;
        bool down = Updater.RedirectAllowed(new Uri("https://example.com/a"), new Uri("http://example.com/b"), out why);
        Check("https to http redirect refused", !down, why);
        Check("https to http redirect message",
            why == "Redirect from https to http was refused.", why);
        bool httpsSame = Updater.RedirectAllowed(new Uri("https://example.com/a"), new Uri("https://example.com/b"), out why);
        Check("https same host redirect allowed", httpsSame, why);
        bool host = Updater.RedirectAllowed(new Uri("http://127.0.0.1:1234/a"), new Uri("http://localhost:1234/b"), out why);
        Check("redirect host change refused", !host, why);
        Check("redirect host change names both hosts",
            why.IndexOf("127.0.0.1", StringComparison.Ordinal) >= 0
            && why.IndexOf("localhost", StringComparison.Ordinal) >= 0, why);
        bool port = Updater.RedirectAllowed(new Uri("http://127.0.0.1:1234/a"), new Uri("http://127.0.0.1:1235/a"), out why);
        Check("redirect port change refused", !port, why);
        bool schemeUp = Updater.RedirectAllowed(new Uri("http://example.com/a"), new Uri("https://example.com/b"), out why);
        Check("http to https same-host default-port redirect allowed", schemeUp, why);
        bool schemeUpHost = Updater.RedirectAllowed(new Uri("http://example.com/a"), new Uri("https://other.example/b"), out why);
        Check("http to https redirect on another host refused", !schemeUpHost, why);
        bool schemeUpPort = Updater.RedirectAllowed(new Uri("http://example.com:8080/a"), new Uri("https://example.com/b"), out why);
        Check("http non-default port to https redirect refused", !schemeUpPort, why);
        bool schemeUpToPort = Updater.RedirectAllowed(new Uri("http://example.com/a"), new Uri("https://example.com:8443/b"), out why);
        Check("http to https non-default port redirect refused", !schemeUpToPort, why);
        bool defPort = Updater.RedirectAllowed(new Uri("http://example.com/a"), new Uri("http://example.com:80/b"), out why);
        Check("redirect to explicit default port allowed", defPort, why);
    }

    static void PayloadOriginRuleTests()
    {
        string why;
        Check("same-origin http payload allowed",
            Updater.PayloadOriginAllowed("http://127.0.0.1/latest.json", "http://127.0.0.1/TechBench.exe", out why), why);
        Check("payload host change refused",
            !Updater.PayloadOriginAllowed("http://127.0.0.1/latest.json", "http://localhost/TechBench.exe", out why), why);
        Check("payload host change names the payload host",
            why.IndexOf("localhost", StringComparison.Ordinal) >= 0, why);
        Check("payload port change refused",
            !Updater.PayloadOriginAllowed("http://127.0.0.1:8080/latest.json", "http://127.0.0.1:9090/TechBench.exe", out why), why);
        Check("https manifest to http payload refused",
            !Updater.PayloadOriginAllowed("https://example.com/latest.json", "http://example.com/TechBench.exe", out why), why);
        Check("http manifest to https payload allowed",
            Updater.PayloadOriginAllowed("http://example.com/latest.json", "https://example.com/TechBench.exe", out why), why);
        Check("file local to file local allowed",
            Updater.PayloadOriginAllowed("file:///C:/feed/latest.json", "file:///C:/TechBench.exe", out why), why);
        string tempManifest = new Uri(Path.Combine(Path.GetTempPath(), "tb-latest.json")).AbsoluteUri;
        Check("temp file manifest to local file payload allowed",
            Updater.PayloadOriginAllowed(tempManifest, "file:///C:/TechBench.exe", out why),
            why + " | " + tempManifest);
        Check("file manifest to http payload allowed",
            Updater.PayloadOriginAllowed("file:///C:/feed/latest.json", "http://127.0.0.1/TechBench.exe", out why), why);
        Check("file manifest to https payload allowed",
            Updater.PayloadOriginAllowed("file:///C:/feed/latest.json", "https://127.0.0.1/TechBench.exe", out why), why);
        Check("file manifest to other scheme refused",
            !Updater.PayloadOriginAllowed("file:///C:/feed/latest.json", "ftp://127.0.0.1/TechBench.exe", out why), why);
        Check("non-http manifest scheme refused",
            !Updater.PayloadOriginAllowed("ftp://127.0.0.1/latest.json", "ftp://127.0.0.1/TechBench.exe", out why), why);
        Check("http manifest to file payload refused",
            !Updater.PayloadOriginAllowed("http://127.0.0.1/latest.json", "file:///C:/TechBench.exe", out why), why);
        Check("relative payload refused",
            !Updater.PayloadOriginAllowed("http://127.0.0.1/latest.json", "TechBench.exe", out why), why);
        Check("payload host compare ignores case",
            Updater.PayloadOriginAllowed("http://Example.com/latest.json", "http://example.com/TechBench.exe", out why), why);
        Check("explicit default http port matches",
            Updater.PayloadOriginAllowed("http://example.com/latest.json", "http://example.com:80/TechBench.exe", out why), why);
        Check("http non-default port cannot upgrade to https",
            !Updater.PayloadOriginAllowed("http://example.com:8080/latest.json", "https://example.com/TechBench.exe", out why), why);
        Check("unc file host case difference allowed",
            Updater.PayloadOriginAllowed("file://shop/share/latest.json", "file://SHOP/share/TechBench.exe", out why), why);
        Check("unc file host change allowed",
            Updater.PayloadOriginAllowed("file://shop/share/latest.json", "file://other/share/TechBench.exe", out why), why);
    }

    static void OversizeManifestTests()
    {
        byte[] big = RepeatByte((byte)'x', 70000);
        UpdateCheck withLen = ServeCheck("oversize manifest with Content-Length",
            new ScriptStep[] { Step(200, big, false, null) }, "latest.json", null);
        AssertUpdateRefused("oversize manifest with Content-Length", withLen, "64 KB");

        UpdateCheck chunked = ServeCheck("oversize manifest without Content-Length",
            new ScriptStep[] { Step(200, big, true, null) }, "chunked.json", null);
        AssertUpdateRefused("oversize manifest without Content-Length", chunked, "64 KB");
    }

    static void NearCapManifestTest()
    {
        int port;
        HttpListener listener = BindLoopback(out port);
        Check("near-cap manifest listener", listener != null, "could not bind 127.0.0.1");
        if (listener == null) return;
        Thread worker = null;
        try
        {
            string root = "http://127.0.0.1:" + port.ToString() + "/";
            string payload = root + "TechBench.exe";
            string notes = new string('n', 60000);
            string json = "{\"version\":\"9.9.9\",\"sha256\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\",\"url\":\""
                + payload + "\",\"notes\":\"" + notes + "\"}";
            byte[] body = Encoding.UTF8.GetBytes(json);
            Check("near-cap manifest is under 64 KB",
                body.Length < Updater.MaxManifestBytes && body.Length > 60000, body.Length.ToString());
            Exception[] serverError = new Exception[1];
            worker = ServeSteps(listener, new ScriptStep[] { Step(200, body, false, null) }, null, serverError);
            UpdateCheck near = Updater.Check(root + "near.json");
            CheckServerDone("near-cap manifest", serverError, worker);
            Check("manifest just under the cap still parses",
                Updater.IsAvailable(near)
                && near.Manifest != null
                && near.Manifest.Notes != null
                && near.Manifest.Notes.Length == 60000,
                Updater.StatusText(near));
            Check("near-cap manifest records source url",
                near.Manifest != null && near.Manifest.SourceUrl == root + "near.json",
                near.Manifest == null ? Updater.StatusText(near) : (near.Manifest.SourceUrl ?? "(null source)"));
        }
        finally
        {
            CloseFeed(listener, worker);
        }
    }

    static void CrossHostRedirectTest()
    {
        int port;
        HttpListener listener = BindLoopback(out port);
        Check("cross-host redirect listener", listener != null, "could not bind 127.0.0.1");
        if (listener == null) return;
        List<string> paths = new List<string>();
        Thread worker = null;
        try
        {
            string root = "http://127.0.0.1:" + port.ToString() + "/";
            string loc = "http://localhost:" + port.ToString() + "/x";
            // Client refuses before a second request; a writer fault is not a failure.
            Exception[] serverError = new Exception[1];
            worker = ServeSteps(listener, new ScriptStep[] { Step(302, new byte[0], false, loc) }, paths, serverError);
            UpdateCheck refused = Updater.Check(root + "start.json");
            worker.Join(12000);
            AssertUpdateRefused("cross-host redirect", refused, "another host");
            Check("cross-host redirect names localhost",
                Updater.StatusText(refused).IndexOf("localhost", StringComparison.OrdinalIgnoreCase) >= 0,
                Updater.StatusText(refused));
            Check("cross-host redirect did not request the other host",
                paths.Count == 1, "hits=" + paths.Count.ToString() + Seen(paths));
        }
        finally
        {
            CloseFeed(listener, worker);
        }
    }

    static void SameHostRedirectTest()
    {
        int port;
        HttpListener listener = BindLoopback(out port);
        Check("same-host redirect listener", listener != null, "could not bind 127.0.0.1");
        if (listener == null) return;
        List<string> paths = new List<string>();
        Thread worker = null;
        try
        {
            string root = "http://127.0.0.1:" + port.ToString() + "/";
            string moved = root + "moved.json";
            byte[] body = Encoding.UTF8.GetBytes(ManifestJson("9.9.9", root + "TechBench.exe"));
            ScriptStep[] steps = new ScriptStep[]
            {
                Step(302, new byte[0], false, moved),
                Step(200, body, false, null)
            };
            Exception[] serverError = new Exception[1];
            worker = ServeSteps(listener, steps, paths, serverError);
            UpdateCheck followed = Updater.Check(root + "start.json");
            CheckServerDone("same-host redirect", serverError, worker);
            Check("same-host redirect is followed", Updater.IsAvailable(followed), Updater.StatusText(followed));
            Check("same-host redirect records the requested url",
                followed.Manifest != null && followed.Manifest.SourceUrl == root + "start.json",
                followed.Manifest == null ? Updater.StatusText(followed) : (followed.Manifest.SourceUrl ?? "(null source)"));
            bool second = paths.Count == 2 && paths[1] != null
                && paths[1].IndexOf("moved.json", StringComparison.Ordinal) >= 0;
            Check("same-host redirect fetched moved.json", second, "hits=" + paths.Count.ToString() + Seen(paths));
        }
        finally
        {
            CloseFeed(listener, worker);
        }
    }

    static void TooManyRedirectsTest()
    {
        int port;
        HttpListener listener = BindLoopback(out port);
        Check("too many redirects listener", listener != null, "could not bind 127.0.0.1");
        if (listener == null) return;
        List<string> paths = new List<string>();
        Thread worker = null;
        try
        {
            string root = "http://127.0.0.1:" + port.ToString() + "/";
            ScriptStep[] steps = new ScriptStep[6];
            for (int i = 0; i < steps.Length; i++)
                steps[i] = Step(302, new byte[0], false, root + "step" + i.ToString() + ".json");
            Exception[] serverError = new Exception[1];
            worker = ServeSteps(listener, steps, paths, serverError);
            UpdateCheck refused = Updater.Check(root + "chain.json");
            CheckServerDone("too many redirects", serverError, worker);
            AssertUpdateRefused("too many redirects", refused, "more than 5");
            Check("too many redirects stopped after the cap",
                paths.Count == Updater.MaxRedirects + 1, "hits=" + paths.Count.ToString());
        }
        finally
        {
            CloseFeed(listener, worker);
        }
    }

    static void PayloadOversizeTest(bool chunked)
    {
        string label = chunked
            ? "oversize payload without Content-Length"
            : "oversize payload with Content-Length";
        int port;
        HttpListener listener = BindLoopback(out port);
        Check(label + " listener", listener != null, "could not bind 127.0.0.1");
        if (listener == null) return;
        string dir = null;
        Thread worker = null;
        try
        {
            dir = NewTempDir("tb-cap-");
            string root = "http://127.0.0.1:" + port.ToString() + "/";
            byte[] body = RepeatByte((byte)'p', 4096);
            // Client stops at the cap; a short write on the server is expected.
            Exception[] serverError = new Exception[1];
            worker = ServeSteps(listener, new ScriptStep[] { Step(200, body, chunked, null) }, null, serverError);
            Exception ex = CatchEx(delegate
            {
                Updater.DownloadAndStage(dir, root + "latest.json", root + "TechBench.exe", new string('a', 64), 1024L);
            });
            worker.Join(12000);
            Check(label + " throws", ex != null, "no throw");
            Check(label + " names the limit",
                ex != null && ex.Message.IndexOf("larger than", StringComparison.Ordinal) >= 0,
                ex == null ? "(null)" : ex.Message);
            Check(label + " uses shop setup wording",
                ex != null
                && ex.Message.IndexOf("shop Setup", StringComparison.Ordinal) >= 0
                && ex.Message.IndexOf("never uses a GitHub token", StringComparison.Ordinal) >= 0,
                ex == null ? "(null)" : ex.Message);
            AssertNothingStaged(label, dir);
        }
        finally
        {
            CloseFeed(listener, worker);
            if (dir != null) try { Directory.Delete(dir, true); } catch { }
        }
    }

    static void PayloadHashMismatchTest()
    {
        int port;
        HttpListener listener = BindLoopback(out port);
        Check("payload hash mismatch listener", listener != null, "could not bind 127.0.0.1");
        if (listener == null) return;
        string dir = null;
        Thread worker = null;
        try
        {
            dir = NewTempDir("tb-hash-");
            string root = "http://127.0.0.1:" + port.ToString() + "/";
            byte[] body = Encoding.UTF8.GetBytes("not-the-real-update");
            Exception[] serverError = new Exception[1];
            worker = ServeSteps(listener, new ScriptStep[] { Step(200, body, false, null) }, null, serverError);
            Exception ex = CatchEx(delegate
            {
                Updater.DownloadAndStage(dir, root + "latest.json", root + "TechBench.exe",
                    new string('b', 64), Updater.MaxPayloadBytes);
            });
            CheckServerDone("payload hash mismatch", serverError, worker);
            Check("payload hash mismatch throws",
                ex != null && ex.Message.IndexOf("did not match", StringComparison.Ordinal) >= 0,
                ex == null ? "no throw" : ex.Message);
            Check("payload hash mismatch uses shop setup wording",
                ex != null
                && ex.Message.IndexOf("shop Setup", StringComparison.Ordinal) >= 0
                && ex.Message.IndexOf("never uses a GitHub token", StringComparison.Ordinal) >= 0,
                ex == null ? "(null)" : ex.Message);
            AssertNothingStaged("payload hash mismatch", dir);
        }
        finally
        {
            CloseFeed(listener, worker);
            if (dir != null) try { Directory.Delete(dir, true); } catch { }
        }
    }

    static void PayloadHappyPathTest()
    {
        int port;
        HttpListener listener = BindLoopback(out port);
        Check("payload happy path listener", listener != null, "could not bind 127.0.0.1");
        if (listener == null) return;
        string dir = null;
        Thread worker = null;
        try
        {
            dir = NewTempDir("tb-ok-");
            string root = "http://127.0.0.1:" + port.ToString() + "/";
            byte[] body = Encoding.UTF8.GetBytes("tech-bench-payload-v129");
            string sha = Updater.Sha256Bytes(body);
            Exception[] serverError = new Exception[1];
            worker = ServeSteps(listener, new ScriptStep[] { Step(200, body, false, null) }, null, serverError);
            Exception ex = CatchEx(delegate
            {
                Updater.DownloadAndStage(dir, root + "latest.json", root + "TechBench.exe",
                    sha.ToUpperInvariant(), Updater.MaxPayloadBytes);
            });
            CheckServerDone("payload happy path", serverError, worker);
            Check("payload happy path does not throw", ex == null, ex == null ? "" : ex.Message);
            string staged = Path.Combine(dir, "TechBench.exe.new");
            Check("payload happy path staged the exe", File.Exists(staged), staged);
            if (File.Exists(staged))
            {
                byte[] got = File.ReadAllBytes(staged);
                Check("payload happy path bytes match", BytesEqual(got, body), "len=" + got.Length.ToString());
            }
            Check("payload happy path verifies", Updater.HasVerifiedPending(dir), dir);
            Check("payload happy path left no .part file",
                !File.Exists(Path.Combine(dir, "TechBench.exe.new.part")), dir);
        }
        finally
        {
            CloseFeed(listener, worker);
            if (dir != null) try { Directory.Delete(dir, true); } catch { }
        }
    }

    static void PayloadOriginNoRequestTest()
    {
        int port;
        HttpListener listener = BindLoopback(out port);
        Check("payload origin refusal listener", listener != null, "could not bind 127.0.0.1");
        if (listener == null) return;
        string dir = null;
        List<string> paths = new List<string>();
        Thread worker = null;
        try
        {
            dir = NewTempDir("tb-origin-");
            string root = "http://127.0.0.1:" + port.ToString() + "/";
            // Origin is refused before any request, so the worker stays blocked in GetContext.
            Exception[] serverError = new Exception[1];
            worker = ServeSteps(listener,
                new ScriptStep[] { Step(200, Encoding.UTF8.GetBytes("should-not-download"), false, null) },
                paths, serverError);
            Exception ex = CatchEx(delegate
            {
                Updater.DownloadAndStage(dir, "http://127.0.0.1:1/latest.json", root + "TechBench.exe",
                    new string('c', 64), 1024L);
            });
            Check("payload origin mismatch throws", ex != null, "no throw");
            Check("payload origin mismatch made no request", paths.Count == 0, "hits=" + paths.Count.ToString());
            Check("payload origin mismatch uses shop setup wording",
                ex != null
                && ex.Message.IndexOf("shop Setup", StringComparison.Ordinal) >= 0
                && ex.Message.IndexOf("never uses a GitHub token", StringComparison.Ordinal) >= 0,
                ex == null ? "(null)" : ex.Message);
            AssertNothingStaged("payload origin mismatch", dir);
        }
        finally
        {
            CloseFeed(listener, worker);
            if (dir != null) try { Directory.Delete(dir, true); } catch { }
        }
    }

    static void HttpManifestOtherHostPayloadTest()
    {
        int port;
        HttpListener listener = BindLoopback(out port);
        Check("http manifest other-host payload listener", listener != null, "could not bind 127.0.0.1");
        if (listener == null) return;
        Thread worker = null;
        try
        {
            string root = "http://127.0.0.1:" + port.ToString() + "/";
            string payloadHost = "localhost";
            string payload = "http://" + payloadHost + ":" + port.ToString() + "/TechBench.exe";
            byte[] body = Encoding.UTF8.GetBytes(ManifestJson("9.9.9", payload));
            Exception[] serverError = new Exception[1];
            worker = ServeSteps(listener, new ScriptStep[] { Step(200, body, false, null) }, null, serverError);
            UpdateCheck cross = Updater.Check(root + "latest.json");
            CheckServerDone("http manifest other-host payload", serverError, worker);
            AssertUpdateRefused("http manifest other-host payload", cross, payloadHost);
        }
        finally
        {
            CloseFeed(listener, worker);
        }
    }

    static void SilentBackgroundTest()
    {
        string shellPath = Path.Combine(FindRepoRoot(), "src", "Shell", "ShellForm.cs");
        string shell = File.Exists(shellPath) ? File.ReadAllText(shellPath) : "";
        string finish = MethodSlice(shell, "void FinishCheck(", "void InstallReadyUpdate(");
        string install = MethodSlice(shell, "void InstallReadyUpdate(", "Control BuildAdaptersPanel(");
        Check("FinishCheck returns early when not interactive",
            finish.IndexOf("if (!interactive) return;", StringComparison.Ordinal) >= 0, "FinishCheck");
        int can = install.IndexOf("CanApplyNow", StringComparison.Ordinal);
        int download = install.IndexOf("DownloadAndStage", StringComparison.Ordinal);
        Check("InstallReadyUpdate checks CanApplyNow before downloading",
            can >= 0 && download > can, "can=" + can.ToString() + " download=" + download.ToString());
    }

    static void AssertUpdateRefused(string label, UpdateCheck result, string extra)
    {
        string text = Updater.StatusText(result);
        Check(label + " is not current", !Updater.IsCurrent(result), text);
        Check(label + " is not available", !Updater.IsAvailable(result), text);
        Check(label + " begins with update check failed",
            text.IndexOf("Update check failed", StringComparison.Ordinal) == 0, text);
        CheckPrivateFeedFailure(label, result);
        if (!string.IsNullOrEmpty(extra))
            Check(label + " names the reason", text.IndexOf(extra, StringComparison.Ordinal) >= 0, text);
    }

    static void AssertNothingStaged(string label, string dir)
    {
        Check(label + " left no .part file",
            !File.Exists(Path.Combine(dir, "TechBench.exe.new.part")), label);
        Check(label + " left no TechBench.exe.new",
            !File.Exists(Path.Combine(dir, "TechBench.exe.new")), label);
    }

    static UpdateCheck ServeCheck(string label, ScriptStep[] steps, string path, List<string> paths)
    {
        int port;
        HttpListener listener = BindLoopback(out port);
        Check(label + " listener", listener != null, "could not bind 127.0.0.1");
        if (listener == null) return new UpdateCheck { Error = "no listener" };
        Thread worker = null;
        try
        {
            // Oversize feeds abort mid-body; do not fail on the writer's fault.
            Exception[] serverError = new Exception[1];
            worker = ServeSteps(listener, steps, paths, serverError);
            string url = "http://127.0.0.1:" + port.ToString() + "/" + (path ?? "");
            UpdateCheck result = Updater.Check(url);
            if (worker != null) worker.Join(12000);
            return result;
        }
        finally
        {
            CloseFeed(listener, worker);
        }
    }

    static ScriptStep Step(int status, byte[] body, bool chunked, string location)
    {
        ScriptStep step = new ScriptStep();
        step.Status = status;
        step.Body = body;
        step.Chunked = chunked;
        step.Location = location;
        return step;
    }

    // Full-read tests only. A client that aborts the body can fault the writer.
    static void CheckServerDone(string label, Exception[] error, Thread worker)
    {
        bool finished = false;
        if (worker != null) finished = worker.Join(12000);
        string errText = "";
        if (error != null && error[0] != null) errText = error[0].ToString();
        Check(label + " server error", error == null || error[0] == null, errText);
        bool alive = worker != null && worker.IsAlive;
        Check(label + " server finished", finished && !alive, alive ? "still running" : "join timed out");
    }

    static Thread ServeSteps(HttpListener listener, ScriptStep[] steps, List<string> paths, Exception[] error)
    {
        Thread worker = new Thread(delegate()
        {
            try
            {
                for (int i = 0; i < steps.Length; i++)
                {
                    HttpListenerContext ctx = listener.GetContext();
                    if (paths != null)
                    {
                        string raw = "";
                        try
                        {
                            if (ctx.Request != null && ctx.Request.Url != null)
                                raw = ctx.Request.RawUrl ?? "";
                        }
                        catch { raw = ""; }
                        paths.Add(raw);
                    }
                    ScriptStep step = steps[i];
                    try
                    {
                        int status = step.Status <= 0 ? 200 : step.Status;
                        if (!string.IsNullOrEmpty(step.Location))
                            ctx.Response.RedirectLocation = step.Location;
                        ctx.Response.StatusCode = status;
                        ctx.Response.KeepAlive = false;
                        byte[] body = step.Body ?? new byte[0];
                        if (step.Chunked)
                            ctx.Response.SendChunked = true;
                        else
                            ctx.Response.ContentLength64 = body.Length;
                        int offset = 0;
                        while (offset < body.Length)
                        {
                            int n = body.Length - offset;
                            if (n > 8192) n = 8192;
                            ctx.Response.OutputStream.Write(body, offset, n);
                            offset += n;
                        }
                        ctx.Response.OutputStream.Close();
                    }
                    catch (Exception ex)
                    {
                        if (error != null && error[0] == null) error[0] = ex;
                        try { ctx.Response.Abort(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                if (error != null && error[0] == null) error[0] = ex;
            }
        });
        worker.IsBackground = true;
        worker.Start();
        return worker;
    }

    static HttpListener BindLoopback(out int port)
    {
        port = 0;
        Random rng = new Random();
        for (int attempt = 0; attempt < 12; attempt++)
        {
            int candidate = 21000 + rng.Next(20000);
            HttpListener listener = new HttpListener();
            try
            {
                listener.Prefixes.Add("http://127.0.0.1:" + candidate.ToString() + "/");
                listener.Start();
                try { listener.IgnoreWriteExceptions = true; } catch { }
                port = candidate;
                return listener;
            }
            catch
            {
                try { listener.Close(); } catch { }
            }
        }
        return null;
    }

    static void CloseFeed(HttpListener listener, Thread worker)
    {
        try { if (listener != null) listener.Close(); } catch { }
        if (worker != null) worker.Join(12000);
    }

    static Exception CatchEx(Action act)
    {
        try
        {
            act();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    static string MethodSlice(string src, string startSig, string endSig)
    {
        if (string.IsNullOrEmpty(src)) return "";
        int start = src.IndexOf(startSig, StringComparison.Ordinal);
        if (start < 0) return "";
        int end = src.IndexOf(endSig, start + startSig.Length, StringComparison.Ordinal);
        if (end < 0) end = src.Length;
        return src.Substring(start, end - start);
    }

    static string NewTempDir(string prefix)
    {
        string dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static byte[] RepeatByte(byte value, int count)
    {
        byte[] buf = new byte[count];
        for (int i = 0; i < count; i++) buf[i] = value;
        return buf;
    }

    static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    static string Seen(List<string> paths)
    {
        if (paths == null || paths.Count == 0) return "";
        string seen = "";
        for (int i = 0; i < paths.Count; i++) seen = seen + " " + (paths[i] ?? "");
        return seen;
    }

    sealed class ScriptStep
    {
        public int Status;
        public byte[] Body;
        public bool Chunked;
        public string Location;
    }

}
