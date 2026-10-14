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
    static void CredProtectTests()
    {
        var creds = new IdSettings
        {
            GatewayUrl = "https://dealer.azure-api.net",
            ClientId = "cid",
            ClientSecret = "oauth-secret",
            SubscriptionKey = "sub-key",
            TechId = "viper"
        };
        creds.Save();
        string stored = File.ReadAllText(IdSettings.PathName());
        Check("LocalAppData json has no plaintext secret", stored.IndexOf("oauth-secret") < 0, stored);
        Check("LocalAppData json has no plaintext subscription key", stored.IndexOf("sub-key") < 0, stored);
        Check("LocalAppData json keeps ciphertext",
            stored.IndexOf("ClientSecretProtected") >= 0 && stored.IndexOf("SubscriptionKeyProtected") >= 0, stored);

        string cipher = IdSettings.ProtectSecret("shop-secret");
        Check("protect is not plaintext", cipher.Length > 0 && cipher != "shop-secret", cipher);
        Eq("DPAPI/protector round-trip", IdSettings.UnprotectSecret(cipher), "shop-secret");
        Eq("corrupt ciphertext is empty", IdSettings.UnprotectSecret("%%%not-valid%%%"), "");
        Eq("missing ciphertext is empty", IdSettings.UnprotectSecret(""), "");
        Eq("null ciphertext is empty", IdSettings.UnprotectSecret(null), "");

        var loaded = IdSettings.LoadFrom(IdSettings.PathName());
        Eq("load decrypts secret", loaded.ClientSecret, "oauth-secret");
        Eq("load decrypts subscription key", loaded.SubscriptionKey, "sub-key");
        Eq("in-memory gateway url", loaded.GatewayUrl, "https://dealer.azure-api.net");

        File.WriteAllText(IdSettings.PathName(),
            "{\"GatewayUrl\":\"https://dealer.azure-api.net\",\"ClientSecretProtected\":\"%%%\",\"SubscriptionKeyProtected\":\"not-base64\"}");
        var corrupt = IdSettings.LoadFrom(IdSettings.PathName());
        Eq("corrupt secret becomes empty", corrupt.ClientSecret, "");
        Eq("corrupt key becomes empty", corrupt.SubscriptionKey, "");
        Eq("corrupt file still loads url", corrupt.GatewayUrl, "https://dealer.azure-api.net");

        string beside = Path.Combine(WorkOrderStore.ExeDirOverride, "id-settings.json");
        creds.ClientSecret = "oauth-secret";
        creds.SubscriptionKey = "sub-key";
        creds.SaveTo(beside);
        string besideJson = File.ReadAllText(beside);
        Check("Save beside exe writes no plaintext secret", besideJson.IndexOf("oauth-secret") < 0, besideJson);
        Check("Save beside exe writes no plaintext key", besideJson.IndexOf("sub-key") < 0, besideJson);
        Check("Save beside exe writes no ciphertext blob",
            besideJson.IndexOf("ClientSecretProtected\":\"") < 0
            || besideJson.IndexOf("\"ClientSecretProtected\":\"\"") >= 0, besideJson);

        creds.GatewayUrl = "";
        creds.Save();
        File.WriteAllText(beside,
            "{\"GatewayUrl\":\"https://overlay.azure-api.net\",\"SubscriptionKey\":\"should-not-load\",\"ClientSecret\":\"also-no\"}");
        var merged = IdSettings.Load();
        Eq("beside overlays blank gateway url", merged.GatewayUrl, "https://overlay.azure-api.net");
        Eq("beside secret is not loaded", merged.ClientSecret, "oauth-secret");
        Eq("beside subscription key is not loaded", merged.SubscriptionKey, "sub-key");

        File.Delete(IdSettings.PathName());
        File.WriteAllText(beside,
            "{\"GatewayUrl\":\"https://exe.azure-api.net\",\"ClientSecret\":\"leaked\",\"SubscriptionKey\":\"leaked-key\"}");
        var onlyBeside = IdSettings.Load();
        Eq("beside-only url used", onlyBeside.GatewayUrl, "https://exe.azure-api.net");
        Eq("beside-only secret ignored", onlyBeside.ClientSecret, "");
        Eq("beside-only key ignored", onlyBeside.SubscriptionKey, "");

        string legacyPath = Path.Combine(IdSettings.Folder(), "legacy-plain-key.json");
        File.WriteAllText(legacyPath,
            "{\"GatewayUrl\":\"https://legacy.azure-api.net\",\"SubscriptionKey\":\"legacy-plain-key\"}");
        var legacy = IdSettings.LoadFrom(legacyPath);
        string legacyJson = File.ReadAllText(legacyPath);
        Check("plaintext subscription key migrated off disk", legacyJson.IndexOf("legacy-plain-key") < 0, legacyJson);
        Check("migrated file keeps DPAPI field",
            legacyJson.IndexOf("SubscriptionKeyProtected") >= 0, legacyJson);
        Eq("migrated key still loads", legacy.SubscriptionKey, "legacy-plain-key");
        try { File.Delete(legacyPath); } catch { }

        GatewayServiceTodayTests();
    }

    static void GatewayServiceTodayTests()
    {
        var prevHttp = IdGateway.HttpOverride;
        try
        {
            IdGateway.ClearTokenCache();
            var urlOnly = new IdSettings { GatewayUrl = "https://dealer.azure-api.net" };
            Check("gateway off without subscription key", !urlOnly.GatewayConfigured(), urlOnly.GatewayUrl);
            var oauthOnly = new IdSettings
            {
                GatewayUrl = "https://dealer.azure-api.net",
                ClientId = "cid",
                ClientSecret = "sec"
            };
            Check("oauth alone does not enable gateway", !oauthOnly.GatewayConfigured(), "oauth");

            IdCallResult missing = IdGateway.FetchAssigned(urlOnly);
            Check("unconfigured ServiceToday fetch", !missing.Posted, missing.Message);
            Check("unconfigured names gateway", missing.Message.IndexOf("API Gateway") >= 0, missing.Message);

            var live = new IdSettings
            {
                GatewayUrl = "https://dealer.azure-api.net",
                SubscriptionKey = "test-key",
                TechNumber = "T12",
                AssignedPath = "/service/technicians/{tech}/workorders"
            };

            IdGateway.HttpOverride = delegate(IdHttpRequest req)
            {
                Check("ServiceToday GET only",
                    req.Method == "GET" && req.Url.IndexOf("workorders") >= 0, req.Method + " " + req.Url);
                Check("subscription key on ServiceToday GET",
                    req.Headers.ContainsKey("Ocp-Apim-Subscription-Key")
                    && req.Headers["Ocp-Apim-Subscription-Key"] == "test-key",
                    "missing key");
                return new IdHttpResponse
                {
                    Status = 201,
                    Body = "[{\"Number\":\"ST-1\",\"Customer\":\"ServiceToday\"}]"
                };
            };
            IdCallResult ok = IdGateway.FetchAssigned(live);
            Check("ServiceToday 201 is success", ok.Posted, ok.Message);
            Eq("ServiceToday wo count", ok.WorkOrders.Count, 1);
            Eq("ServiceToday wo number", ok.WorkOrders[0].Number, "ST-1");

            IdGateway.HttpOverride = delegate(IdHttpRequest req)
            {
                return new IdHttpResponse { Status = 401, Body = "unauthorized" };
            };
            IdCallResult unauthorized = IdGateway.FetchAssigned(live);
            Check("401 not posted", !unauthorized.Posted, unauthorized.Message);
            Check("401 prompts re-sign-in", unauthorized.NeedsReSignIn, unauthorized.Message);
            Check("401 message mentions sign in",
                unauthorized.Message.IndexOf("sign in", StringComparison.OrdinalIgnoreCase) >= 0,
                unauthorized.Message);

            int attempts = 0;
            IdGateway.HttpOverride = delegate(IdHttpRequest req)
            {
                attempts++;
                return new IdHttpResponse { Status = 403, Body = "forbidden" };
            };
            IdCallResult forbidden = IdGateway.FetchAssigned(live);
            Check("403 not posted", !forbidden.Posted, forbidden.Message);
            Eq("403 is not retried", attempts, 1);

            attempts = 0;
            IdGateway.HttpOverride = delegate(IdHttpRequest req)
            {
                attempts++;
                if (attempts == 1)
                    return new IdHttpResponse { Status = 429, RetryAfterSeconds = 0 };
                return new IdHttpResponse { Status = 204, Body = "" };
            };
            IdCallResult throttled = IdGateway.FetchAssigned(live);
            Check("429 then 204 succeeds", throttled.Posted, throttled.Message);
            Eq("429 retried once", attempts, 2);
        }
        finally
        {
            IdGateway.HttpOverride = prevHttp;
            IdGateway.ClearTokenCache();
        }
    }

    static void TokenCacheTests(IdSettings live, WorkOrder wo)
    {
        IdGateway.ClearTokenCache();
        int hits = 0;
        var oauth = new IdSettings
        {
            GatewayUrl = live.GatewayUrl,
            TokenUrl = "https://idp.example/token",
            ClientId = "cid",
            ClientSecret = "csec",
            SignOffPath = live.SignOffPath,
            TechNumber = live.TechNumber
        };
        IdGateway.HttpOverride = delegate(IdHttpRequest req)
        {
            hits++;
            if (req.Url.IndexOf("/token") >= 0)
                return new IdHttpResponse { Status = 200, Body = "{\"access_token\":\"tok-1\",\"expires_in\":3600}" };
            return new IdHttpResponse { Status = 500, Body = "no" };
        };
        Eq("token first fetch", IdGateway.Token(oauth), "tok-1");
        Eq("token reused until near expiry", IdGateway.Token(oauth), "tok-1");
        Eq("token endpoint hit once", hits, 1);

        oauth.ClientSecret = "rotated";
        IdGateway.HttpOverride = delegate(IdHttpRequest req)
        {
            hits++;
            return new IdHttpResponse { Status = 200, Body = "{\"access_token\":\"tok-2\",\"expires_in\":3600}" };
        };
        Eq("token cleared when credentials change", IdGateway.Token(oauth), "tok-2");
        Eq("token endpoint hit after cred change", hits, 2);

        IdGateway.ClearTokenCache();
        hits = 0;
        oauth.ClientSecret = "csec";
        IdGateway.HttpOverride = delegate(IdHttpRequest req)
        {
            hits++;
            return new IdHttpResponse { Status = 200, Body = "{\"access_token\":\"short\",\"expires_in\":30}" };
        };
        IdGateway.Token(oauth);
        IdGateway.Token(oauth);
        Eq("token not reused inside 60s skew", hits, 2);

        IdGateway.ClearTokenCache();
        IdGateway.HttpOverride = delegate(IdHttpRequest req)
        {
            return new IdHttpResponse { Status = 400, Body = "{\"error\":\"denied\"}" };
        };
        Eq("token 400 is empty", IdGateway.Token(oauth), "");
        Check("token 400 sets LastTokenError",
            IdGateway.LastTokenError.IndexOf("OAuth", StringComparison.OrdinalIgnoreCase) >= 0,
            IdGateway.LastTokenError);

        IdGateway.HttpOverride = delegate(IdHttpRequest req)
        {
            return new IdHttpResponse { Status = 401, Body = "nope" };
        };
        IdCallResult denied = IdGateway.SignOff(live, wo);
        Check("gateway 401 is not success", !denied.Posted, denied.Message);
        Check("2xx still required", denied.Status == 401 || !denied.Posted, "status=" + denied.Status);

        Eq("combine keeps relative on gateway",
            IdGateway.CombineUrl("https://dealer.azure-api.net", "/svc/x"),
            "https://dealer.azure-api.net/svc/x");
        Eq("combine same-host absolute allowed",
            IdGateway.CombineUrl("https://dealer.azure-api.net", "https://dealer.azure-api.net/svc/y"),
            "https://dealer.azure-api.net/svc/y");
        Eq("combine refuses off-host absolute",
            IdGateway.CombineUrl("https://dealer.azure-api.net", "https://evil.example/steal"),
            "");
        Eq("combine refuses https-to-http absolute",
            IdGateway.CombineUrl("https://dealer.azure-api.net", "http://dealer.azure-api.net/svc"),
            "");

        string why;
        Check("gateway redirect same host allowed",
            IdGateway.RedirectAllowed(
                new Uri("https://dealer.azure-api.net/a"),
                new Uri("https://dealer.azure-api.net/b"),
                out why), why);
        Check("gateway redirect other host refused",
            !IdGateway.RedirectAllowed(
                new Uri("https://dealer.azure-api.net/a"),
                new Uri("https://evil.example/b"),
                out why), why);
        Check("gateway redirect https to http refused",
            !IdGateway.RedirectAllowed(
                new Uri("https://dealer.azure-api.net/a"),
                new Uri("http://dealer.azure-api.net/b"),
                out why), why);
    }

    static void WorkOrderMergeTests(IdSettings s, string kb)
    {
        var clash = new WorkOrder
        {
            Number = "9001",
            Segment = "01",
            Customer = "Clash Co",
            Notes = "Local notes A"
        };
        WorkOrderStore.Save(clash, s, kb);
        string localDir = Path.Combine(WorkOrderStore.LocalRoot(), "9001-01");
        string shopDir = Path.Combine(WorkOrderStore.ShopTechDir(kb, "viper"), "9001-01");
        WorkOrder shopWo = WorkOrderStore.Load(shopDir);
        Check("shop shard exists for merge", shopWo != null, shopDir);
        shopWo.Notes = "Remote notes B";
        shopWo.UpdatedUtc = DateTime.UtcNow.AddHours(2).ToString("o");
        File.WriteAllText(Path.Combine(shopDir, "notes.txt"), shopWo.Notes, Encoding.UTF8);
        var ser = new JavaScriptSerializer();
        File.WriteAllText(Path.Combine(shopDir, "wo.json"), ser.Serialize(shopWo));

        var result = new ShopSyncResult();
        WorkOrderStore.CollectConflicts(s, kb, result);
        WorkOrder listed = FindWo(WorkOrderStore.ListAll(s, kb), "9001");
        Check("newer remote packet listed", listed != null && listed.Notes.IndexOf("Remote notes B") >= 0,
            listed == null ? "missing 9001" : listed.Notes);
        Check("local notes still on disk",
            File.ReadAllText(Path.Combine(localDir, "notes.txt")).IndexOf("Local notes A") >= 0, "local dropped");
        ShopConflict woClash = null;
        foreach (ShopConflict c in WorkOrderStore.Conflicts)
        {
            if (c.Kind == "work-order" && (c.Key ?? "").IndexOf("9001") >= 0) woClash = c;
        }
        Check("same WO key disagreement is a conflict", woClash != null, "conflicts=" + WorkOrderStore.Conflicts.Count);
        Check("CollectConflicts reuses ShopConflict list", result.Conflicts >= 1, "n=" + result.Conflicts);

        if (woClash != null)
        {
            ShopSync.Resolve(kb, woClash, "remote");
            WorkOrder after = FindWo(WorkOrderStore.ListAll(s, kb), "9001");
            int still = 0;
            foreach (ShopConflict c in WorkOrderStore.Conflicts)
                if (c.Kind == "work-order" && (c.Key ?? "").IndexOf("9001") >= 0) still++;
            Check("resolve remote clears WO conflict", still == 0, "still=" + still);
            Check("resolve copied remote notes to local",
                File.ReadAllText(Path.Combine(localDir, "notes.txt")).IndexOf("Remote notes B") >= 0, "not copied");
            Check("listed notes after resolve", after != null && after.Notes.IndexOf("Remote notes B") >= 0,
                after == null ? "missing" : after.Notes);
        }

        var same = new WorkOrder { Number = "9002", Segment = "01", Notes = "Same notes" };
        WorkOrderStore.Save(same, s, kb);
        string shop2 = Path.Combine(WorkOrderStore.ShopTechDir(kb, "viper"), "9002-01");
        WorkOrder shopSame = WorkOrderStore.Load(shop2);
        string newer = DateTime.UtcNow.AddHours(3).ToString("o");
        shopSame.UpdatedUtc = newer;
        File.WriteAllText(Path.Combine(shop2, "wo.json"), ser.Serialize(shopSame));
        WorkOrder listedSame = FindWo(WorkOrderStore.ListAll(s, kb), "9002");
        bool clash9002 = false;
        foreach (ShopConflict c in WorkOrderStore.Conflicts)
            if (c.Kind == "work-order" && (c.Key ?? "").IndexOf("9002") >= 0) clash9002 = true;
        Check("same content hash is not a conflict", !clash9002, "9002 conflicted");
        Check("newer timestamp kept when notes match",
            listedSame != null && listedSame.UpdatedUtc == newer, listedSame == null ? "missing" : listedSame.UpdatedUtc);
    }

    static WorkOrder FindWo(List<WorkOrder> list, string number)
    {
        if (list == null) return null;
        foreach (WorkOrder x in list)
            if (x != null && x.Number == number) return x;
        return null;
    }

    static void WorkOrderBakRecoveryTests(IdSettings s, string kb)
    {
        string packet = null;
        string shopPacket = null;
        string onlyBak = null;
        try
        {
            var wo = new WorkOrder
            {
                Number = "77701",
                Segment = "01",
                Customer = "Prev Co",
                Notes = "prev note"
            };
            WorkOrderStore.Save(wo, s, kb);
            packet = WorkOrderStore.PacketDir(WorkOrderStore.LocalRoot(), wo.Key());
            shopPacket = WorkOrderStore.PacketDir(WorkOrderStore.ShopTechDir(kb, s.TechId), wo.Key());
            string jsonPath = Path.Combine(packet, "wo.json");
            string firstJson = File.ReadAllText(jsonPath);
            wo.Customer = "Curr Co";
            wo.Notes = "curr note";
            WorkOrderStore.Save(wo, s, kb);

            WorkOrder preferred = WorkOrderStore.Load(packet);
            Check("valid wo.json is preferred over bak",
                preferred != null && preferred.Customer == "Curr Co",
                preferred == null ? "null" : preferred.Customer);

            string bakText = File.ReadAllText(jsonPath + ".bak");
            WorkOrder recovered = new JavaScriptSerializer().Deserialize<WorkOrder>(bakText);
            string bakDetail = recovered == null ? "null" : (recovered.Number + "/" + recovered.Customer);
            if (bakText != firstJson) bakDetail = "bak text differs; " + bakDetail;
            Check("wo.json.bak holds the previous version",
                bakText == firstJson
                    && recovered != null
                    && recovered.Number == "77701"
                    && recovered.Customer == "Prev Co",
                bakDetail);

            onlyBak = Path.Combine(WorkOrderStore.LocalRoot(), "77702");
            Directory.CreateDirectory(onlyBak);
            File.WriteAllText(Path.Combine(onlyBak, "wo.json.bak"),
                "{\"Number\":\"77702\",\"Customer\":\"Bak Only\"}");
            WorkOrder loadedBak = WorkOrderStore.Load(onlyBak);
            WorkOrder listed = FindWo(WorkOrderStore.ListAll(s, kb), "77702");
            Check("bak-only folder is not loaded",
                loadedBak == null,
                loadedBak == null ? "null" : (loadedBak.Number + "/" + loadedBak.Customer));
            Check("bak-only packet folder is not listed",
                listed == null,
                listed == null ? "absent" : (listed.Number + "/" + listed.Customer));
        }
        finally
        {
            try { if (!string.IsNullOrEmpty(packet)) Directory.Delete(packet, true); } catch { }
            try { if (!string.IsNullOrEmpty(shopPacket)) Directory.Delete(shopPacket, true); } catch { }
            try { if (!string.IsNullOrEmpty(onlyBak)) Directory.Delete(onlyBak, true); } catch { }
        }
    }

}
