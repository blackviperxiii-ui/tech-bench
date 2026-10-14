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
    [SelfTestCase("job report", Order = 200)]
    static void ReportTests()
    {
        var d = new ReportData
        {
            When = new DateTime(2026, 5, 1, 14, 30, 0),
            JobTag = "HP450 12345",
            Adapter = "Cummins Inc. INLINE 7  (CMNSI7)",
            Vin = "1ABC", Sw = "SW9", CompId = "CUMMINS", Hours = "1234.5 h",
            Rpm = 800, Red = true, Amber = true,
            Def = "n/a (not talking)", Coolant = "90 C", Oil = "400 kPa", Batt = "27.6 V", Fuel = "3.5 L/h",
            HistoryNote = "This unit has been here before"
        };
        d.Active.AddRange(Dtcs(5246, 0));
        d.Prev.AddRange(Dtcs(1761, 9));
        d.Markers.Add("09:00:01  CRANK  RPM=180");

        List<string> lines = JobReport.Lines(d);
        string text = string.Join("\n", lines.ToArray());
        Check("has a title", lines[0].Contains("DIAGNOSTIC REPORT"), lines[0]);
        Check("job on the report", text.Contains("HP450 12345"), "job missing");
        Check("hours on the report", text.Contains("1234.5 h"), "hours missing");
        Check("active fault listed", text.Contains("SPN 5246"), "active fault missing");
        Check("previous fault listed", text.Contains("SPN 1761"), "previous fault missing");
        Check("lamp states spelled out", text.Contains("Red Stop ON"), "lamps missing");
        Check("marker carried through", text.Contains("CRANK"), "marker missing");
        Check("history note carried through", text.Contains("been here before"), "history missing");
        Check("signature line present", text.Contains("Signature"), "no signature line");
        Check("states no emissions function was disabled", text.Contains("disabled to produce this report"), "missing disclaimer");
        Check("unset snapshot diff omitted", !text.Contains("Need Snapshot"), "leaked placeholder text");

        var empty = new ReportData();
        string emptyText = JobReport.Text(empty);
        Check("empty report still renders", emptyText.Contains("DIAGNOSTIC REPORT"), "no title");
        Check("empty report says no job", emptyText.Contains("(not entered)"), "no job placeholder");
        Check("empty report marks faults as none", emptyText.Contains("(none)"), "no none marker");
        Check("empty report shows em dash for rpm", emptyText.Contains("RPM:      —"), emptyText);
        Check("no clear run, no code clear section", !emptyText.Contains("LAST CODE CLEAR"), emptyText);

        var cleared = new ReportData { CodeClear = "CODE CLEAR — reset all codes\r\nRESULT: cleared — no active codes came back.\r\n" };
        string clearedText = JobReport.Text(cleared);
        Check("code clear section printed", clearedText.Contains("LAST CODE CLEAR"), clearedText);
        Check("code clear result carried onto the report", clearedText.Contains("  RESULT: cleared — no active codes came back."), clearedText);
    }

    [SelfTestCase("work orders / IntelliDealer gateway", Order = 260)]
    static void WorkOrderTests()
    {
        string prevFolder = IdSettings.FolderOverride;
        string prevExeDir = IdSettings.ExeDirOverride;
        string prevWo = WorkOrderStore.FolderOverride;
        string prevExe = WorkOrderStore.ExeDirOverride;
        var prevHttp = IdGateway.HttpOverride;
        string root = Path.Combine(Path.GetTempPath(), "tb-wo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string kb = Path.Combine(root, "kb");
        Directory.CreateDirectory(Path.Combine(kb, "data"));
        string share = Path.Combine(root, "share");
        Directory.CreateDirectory(share);
        IdSettings.FolderOverride = Path.Combine(root, "cfg");
        WorkOrderStore.FolderOverride = Path.Combine(root, "local-wo");
        WorkOrderStore.ExeDirOverride = Path.Combine(root, "exe");
        IdSettings.ExeDirOverride = WorkOrderStore.ExeDirOverride;
        Directory.CreateDirectory(WorkOrderStore.ExeDirOverride);
        try
        {
            Eq("sanitize key", WorkOrderStore.SafeKey("WO 12/34"), "WO-12-34");
            Eq("empty key", WorkOrderStore.SafeKey("***"), "");

            var wo = new WorkOrder
            {
                Number = "44551",
                Segment = "01",
                Customer = "Acme Quarry",
                Model = "HP450",
                Serial = "12345",
                Notes = "No start. Check 4-pin.",
                Description = "Won't crank"
            };
            Eq("key", wo.Key(), "44551-01");
            Check("job tag has WO", wo.JobTag().Contains("WO 44551-01"), wo.JobTag());

            var s = new IdSettings { TechId = "viper", ShareFolder = share };
            WorkOrderStore.Save(wo, s, kb);
            string packet = Path.Combine(WorkOrderStore.LocalRoot(), "44551-01");
            Check("wo.json written", File.Exists(Path.Combine(packet, "wo.json")), packet);
            Check("notes.txt written", File.ReadAllText(Path.Combine(packet, "notes.txt")).Contains("4-pin"), "notes");
            Check("shop shard packet",
                File.Exists(Path.Combine(WorkOrderStore.ShopTechDir(kb, "viper"), "44551-01", "wo.json")),
                "missing data\\shop\\viper\\work-orders");

            WorkOrder loaded = WorkOrderStore.Load(packet);
            Check("reload packet", loaded != null, packet);
            if (loaded != null)
            {
                Eq("reload customer", loaded.Customer, "Acme Quarry");
                Check("reload notes", loaded.Notes.Contains("4-pin"), loaded.Notes);
            }

            string jsonPath = Path.Combine(packet, "wo.json");
            string notesPath = Path.Combine(packet, "notes.txt");
            byte[] noteBytes = File.ReadAllBytes(notesPath);
            Check("notes.txt is UTF-8 with BOM",
                noteBytes.Length >= 3 && noteBytes[0] == 0xEF && noteBytes[1] == 0xBB && noteBytes[2] == 0xBF,
                "bom");
            byte[] jsonBytes = File.ReadAllBytes(jsonPath);
            Check("wo.json is UTF-8 without BOM",
                jsonBytes.Length > 0 && !(jsonBytes.Length >= 3 && jsonBytes[0] == 0xEF && jsonBytes[1] == 0xBB && jsonBytes[2] == 0xBF)
                && jsonBytes[0] == (byte)'{',
                "bom");
            string firstJson = File.ReadAllText(jsonPath);
            wo.Notes = "Second visit notes.";
            WorkOrderStore.Save(wo, s, kb);
            string secondJson = File.ReadAllText(jsonPath);
            string bakJson = File.ReadAllText(jsonPath + ".bak");
            Eq("wo.json.bak is previous content", bakJson, firstJson);
            Check("wo.json is new content",
                secondJson.IndexOf("Second visit notes.", StringComparison.Ordinal) >= 0 && secondJson != firstJson,
                secondJson);
            Check("notes.txt.bak is previous notes",
                File.ReadAllText(notesPath + ".bak").IndexOf("4-pin", StringComparison.Ordinal) >= 0, "notes bak");
            Check("notes.txt is new notes",
                File.ReadAllText(notesPath).IndexOf("Second visit notes.", StringComparison.Ordinal) >= 0, "notes");
            Check("no leftover tmp after second save", !HasPacketTemp(packet), packet);
            WorkOrder bakWo = new JavaScriptSerializer().Deserialize<WorkOrder>(bakJson);
            Check("bak still parses",
                bakWo != null && bakWo.Number == "44551" && (bakWo.Notes ?? "").IndexOf("4-pin", StringComparison.Ordinal) >= 0,
                bakWo == null ? "null" : (bakWo.Notes ?? ""));
            string shopPacket = Path.Combine(WorkOrderStore.ShopTechDir(kb, "viper"), "44551-01");
            Check("shop copy ignores wo.json.bak", !File.Exists(Path.Combine(shopPacket, "wo.json.bak")), "json bak copied");
            Check("shop copy ignores notes.txt.bak", !File.Exists(Path.Combine(shopPacket, "notes.txt.bak")), "notes bak copied");
            Check("shop copy has no tmp sidecar", !HasPacketTemp(shopPacket), shopPacket);
            Check("shop shard wo.json matches local with no tmp",
                File.ReadAllText(Path.Combine(shopPacket, "wo.json")) == File.ReadAllText(jsonPath)
                    && !HasPacketTemp(shopPacket),
                HasPacketTemp(shopPacket) ? "tmp left" : "json differs");

            string stray = WorkOrderStore.WriteAtomicTemp(jsonPath, "{ truncated", new UTF8Encoding(false));
            Check("crash temp sits beside wo.json",
                stray.IndexOf(jsonPath + ".tmp-", StringComparison.Ordinal) == 0, stray);
            Eq("crash leaves wo.json intact", File.ReadAllText(jsonPath), secondJson);
            WorkOrder afterCrash = WorkOrderStore.Load(packet);
            Check("crash leaves wo.json parseable",
                afterCrash != null && afterCrash.Number == "44551"
                && afterCrash.Notes.IndexOf("Second visit notes.", StringComparison.Ordinal) >= 0,
                afterCrash == null ? "null" : afterCrash.Notes);
            File.Delete(stray);
            Check("stray temp removed", !HasPacketTemp(packet), stray);

            wo.ReportText = "report v1";
            WorkOrderStore.Save(wo, s, kb);
            wo.ReportText = "report v2";
            WorkOrderStore.Save(wo, s, kb);
            Eq("report.txt is new report", File.ReadAllText(Path.Combine(packet, "report.txt")), "report v2");
            Eq("report.txt.bak is previous report", File.ReadAllText(Path.Combine(packet, "report.txt.bak")), "report v1");
            Check("no leftover tmp after report saves", !HasPacketTemp(packet), packet);

            string beforeFallback = File.ReadAllText(jsonPath);
            WorkOrderStore.ForceCopyFallbackForTest = true;
            try
            {
                wo.Notes = "fallback notes";
                WorkOrderStore.Save(wo, s, kb);
                Check("fallback wo.json is new content",
                    File.ReadAllText(jsonPath).IndexOf("fallback notes", StringComparison.Ordinal) >= 0, "new");
                Eq("fallback wo.json.bak is previous content", File.ReadAllText(jsonPath + ".bak"), beforeFallback);
                Check("fallback leaves no tmp", !HasPacketTemp(packet), packet);
            }
            finally
            {
                WorkOrderStore.ForceCopyFallbackForTest = false;
            }

            string pic = Path.Combine(root, "shot.png");
            File.WriteAllText(pic, "png-bytes");
            WorkOrderStore.AttachFile(wo, pic, s, kb);
            Check("media listed", wo.Media.Count >= 1, "count=" + wo.Media.Count);

            string mediaDir = Path.Combine(packet, "media");
            File.WriteAllText(Path.Combine(mediaDir, "shot.png.bak"), "old-png");
            File.WriteAllText(Path.Combine(mediaDir, "shot.png.tmp-dead"), "partial-png");
            WorkOrder mediaLoaded = WorkOrderStore.Load(packet);
            bool sawPng = false, sawMediaBak = false, sawMediaTmp = false;
            if (mediaLoaded != null && mediaLoaded.Media != null)
            {
                foreach (string mediaName in mediaLoaded.Media)
                {
                    if (string.Equals(mediaName, "shot.png", StringComparison.OrdinalIgnoreCase)) sawPng = true;
                    if (string.Equals(mediaName, "shot.png.bak", StringComparison.OrdinalIgnoreCase)) sawMediaBak = true;
                    if (mediaName.IndexOf(".tmp-", StringComparison.OrdinalIgnoreCase) >= 0) sawMediaTmp = true;
                }
            }
            Check("media photo still listed", sawPng, "missing png");
            Check("media ignores bak", !sawMediaBak, "bak listed");
            Check("media ignores tmp", !sawMediaTmp, "tmp listed");
            WorkOrderStore.Save(wo, s, kb);
            Check("copied packet ignores media bak",
                !File.Exists(Path.Combine(shopPacket, "media", "shot.png.bak")), "bak copied");
            Check("copied packet ignores media tmp",
                !File.Exists(Path.Combine(shopPacket, "media", "shot.png.tmp-dead")), "tmp copied");
            List<WorkOrder> listed = WorkOrderStore.ListAll(s, kb);
            int n44551 = 0;
            foreach (WorkOrder listedWo in listed)
                if (listedWo.Number == "44551") n44551++;
            Eq("bak sidecar is not a second work order", n44551, 1);
            string conflictDetail = "";
            if (WorkOrderStore.Conflicts.Count > 0) conflictDetail = WorkOrderStore.Conflicts[0].ToString();
            Check("sidecar files are not a work-order conflict", WorkOrderStore.Conflicts.Count == 0, conflictDetail);

            wo.ReportText = "TECH BENCH — DIAGNOSTIC REPORT";
            string dest = WorkOrderStore.Share(wo, s, kb);
            Check("share dest exists", Directory.Exists(dest), dest);
            Check("shared in kb _shared",
                File.Exists(Path.Combine(WorkOrderStore.ShopSharedDir(kb), "44551-01", "wo.json")),
                "kb shared missing");
            Check("shared in share\\work-orders",
                File.Exists(Path.Combine(share, "work-orders", "44551-01", "wo.json")),
                "share folder missing");
            Check("shared in share data\\shop\\_shared",
                File.Exists(Path.Combine(share, "data", "shop", "_shared", "work-orders", "44551-01", "wo.json")),
                "sync-compatible path missing");

            List<WorkOrder> fromJson = WorkOrderStore.ParseAssignedJson(
                "[{\"Number\":\"99\",\"Customer\":\"Bob\",\"Model\":\"XHP750\",\"Serial\":\"S1\"}]");
            Eq("json count", fromJson.Count, 1);
            Eq("json number", fromJson[0].Number, "99");
            List<WorkOrder> wrapped = WorkOrderStore.ParseAssignedJson(
                "{\"value\":[{\"WorkOrder\":\"77\",\"Segment\":\"02\",\"CustomerName\":\"Cat\"}]}");
            Eq("odata wrap", wrapped.Count, 1);
            Eq("odata wo", wrapped[0].Number, "77");
            List<WorkOrder> csv = WorkOrderStore.ParseAssignedCsv("Number,Customer,Model\n88,Delta,HP1600\n");
            Eq("csv count", csv.Count, 1);
            Eq("csv model", csv[0].Model, "HP1600");

            File.WriteAllText(Path.Combine(WorkOrderStore.ExeDirOverride, "id-work-orders.json"),
                "[{\"Number\":\"SIDECAR\",\"Customer\":\"From file\"}]");
            List<WorkOrder> all = WorkOrderStore.ListAll(s, kb);
            bool sawLocal = false, sawFile = false;
            foreach (WorkOrder x in all)
            {
                if (x.Number == "44551") sawLocal = true;
                if (x.Number == "SIDECAR") sawFile = true;
            }
            Check("list includes saved WO", sawLocal, "missing 44551");
            Check("list includes sidecar file", sawFile, "missing SIDECAR");

            string sync = Path.Combine(IdSettings.Folder(), "sync.json");
            File.WriteAllText(sync, "{\"TechId\":\"alice\",\"SyncFolder\":\"Z:\\\\usb\"}");
            Eq("reads other-agent sync folder", IdSettings.ReadSiblingShare(WorkOrderStore.ExeDirOverride), "Z:\\usb");
            string after = File.ReadAllText(sync);
            Check("does not smash sync.json", after.Contains("alice") && after.Contains("Z:"), after);

            var blank = new IdSettings();
            IdCallResult missing = IdGateway.FetchAssigned(blank);
            Check("no creds is not a live fetch", !missing.Posted, missing.Message);
            IdCallResult noSign = IdGateway.SignOff(blank, wo);
            Check("sign-off refused without gateway", !noSign.Posted, noSign.Message);
            Check("sign-off names payroll/DMS", noSign.Message.Contains("payroll") || noSign.Message.Contains("API Gateway"), noSign.Message);

            var live = new IdSettings
            {
                GatewayUrl = "https://dealer.azure-api.net",
                SubscriptionKey = "test-key",
                TechNumber = "T12",
                AssignedPath = "/service/technicians/{tech}/workorders",
                SignOffPath = "/service/workorders/{wo}/signoff"
            };
            Eq("expand assigned", IdGateway.Expand(live.AssignedPath, live, null),
                "/service/technicians/T12/workorders");
            Eq("combine url", IdGateway.CombineUrl(live.GatewayUrl, "/x"),
                "https://dealer.azure-api.net/x");

            IdGateway.HttpOverride = delegate(IdHttpRequest req)
            {
                if (req.Url.Contains("workorders") && req.Method == "GET")
                    return new IdHttpResponse { Status = 200, Body = "[{\"Number\":\"G1\",\"Customer\":\"Gateway\"}]" };
                return new IdHttpResponse { Status = 404, Error = "not found" };
            };
            IdCallResult fetched = IdGateway.FetchAssigned(live);
            Check("gateway fetch posts on 200", fetched.Posted, fetched.Message);
            Eq("gateway fetch count", fetched.WorkOrders.Count, 1);
            Eq("gateway source", fetched.WorkOrders[0].Source, "gateway");

            IdCallResult denied = IdGateway.SignOff(live, wo);
            Check("sign-off 404 is not success", !denied.Posted, denied.Message);
            Check("did not fake sign-off", wo.ApiSignOff == false, "ApiSignOff was set");

            IdGateway.HttpOverride = delegate(IdHttpRequest req)
            {
                Check("sign-off sends subscription key",
                    req.Headers.ContainsKey("Ocp-Apim-Subscription-Key")
                    && req.Headers["Ocp-Apim-Subscription-Key"] == "test-key",
                    "missing header");
                return new IdHttpResponse { Status = 200, Body = "{\"ok\":true}" };
            };
            IdCallResult signed = IdGateway.SignOff(live, wo);
            Check("sign-off 200 is posted", signed.Posted, signed.Message);

            IdGateway.HttpOverride = delegate(IdHttpRequest req)
            {
                return new IdHttpResponse { Status = 200, Body = "{\"ok\":true}" };
            };
            string media = Path.Combine(packet, "media", "shot.png");
            IdCallResult mm = IdGateway.PostMultimedia(live, wo, media);
            Check("multimedia 200 posts", mm.Posted, mm.Message);
            IdCallResult mmBlank = IdGateway.PostMultimedia(blank, wo, media);
            Check("multimedia without creds is local only", !mmBlank.Posted, mmBlank.Message);

            Eq("list still has packets after gateway tests", all.Count >= 1, true);

            CredProtectTests();
            TokenCacheTests(live, wo);
            WorkOrderMergeTests(s, kb);
            WorkOrderBakRecoveryTests(s, kb);
            JobStripSettingsTests(s, kb, root);
        }
        finally
        {
            WorkOrderStore.ForceCopyFallbackForTest = false;
            IdSettings.FolderOverride = prevFolder;
            IdSettings.ExeDirOverride = prevExeDir;
            WorkOrderStore.FolderOverride = prevWo;
            WorkOrderStore.ExeDirOverride = prevExe;
            IdGateway.HttpOverride = prevHttp;
            IdGateway.ClearTokenCache();
            try { Directory.Delete(root, true); } catch { }
        }
    }

    static void JobStripSettingsTests(IdSettings s, string kb, string root)
    {
        string prevPath = AppSettings.PathOverride;
        string settingsPath = Path.Combine(root, "settings.json");
        AppSettings.PathOverride = settingsPath;
        try
        {
            var prefs = new AppSettings
            {
                Model = "HP450",
                Serial = "999",
                Customer = "Strip Customer",
                LastWo = "44551-01"
            };
            prefs.SaveTo(settingsPath);
            var roundTrip = AppSettings.LoadFrom(settingsPath);
            Eq("job settings customer", roundTrip.Customer, "Strip Customer");
            Eq("job settings last wo", roundTrip.LastWo, "44551-01");

            File.WriteAllText(settingsPath, "{\"Model\":\"HP450\",\"Serial\":\"999\"}");
            var legacy = AppSettings.LoadFrom(settingsPath);
            Eq("legacy job customer empty", legacy.Customer, "");
            Eq("legacy last wo empty", legacy.LastWo, "");

            File.WriteAllText(settingsPath,
                "{\"Model\":\"HP450\",\"Serial\":\"999\",\"Customer\":\"KeepMe\",\"LastWo\":\"44551-01\"}");
            var windowOnly = AppSettings.LoadFrom(settingsPath);
            windowOnly.Customer = "";
            windowOnly.LastWo = "";
            windowOnly.Save();
            var merged = AppSettings.LoadFrom(settingsPath);
            Eq("shell save keeps job customer", merged.Customer, "KeepMe");
            Eq("shell save keeps last wo", merged.LastWo, "44551-01");

            List<WorkOrder> listed = WorkOrderStore.ListAll(s, kb);
            Eq("restore matches saved wo key",
                AppSettings.MatchWorkOrderKey(listed, "44551-01"), "44551-01");
            Eq("missing wo key does not match",
                AppSettings.MatchWorkOrderKey(listed, "gone-99"), "");

            var wipe = new AppSettings { Customer = "X", LastWo = "44551-01" };
            wipe.SaveTo(settingsPath);
            wipe.Customer = "";
            wipe.LastWo = "";
            wipe.SaveTo(settingsPath);
            var afterClear = AppSettings.LoadFrom(settingsPath);
            Eq("clear job wipes customer", afterClear.Customer, "");
            Eq("clear job wipes last wo", afterClear.LastWo, "");

            string woSrc = File.ReadAllText(Path.Combine(FindRepoRoot(), "WorkOrderControl.cs"));
            Check("clear job button present",
                woSrc.IndexOf("Clear job", StringComparison.Ordinal) >= 0, "WorkOrderControl");
            Check("restore waits for list load",
                woSrc.IndexOf("MatchWorkOrderKey(_orders", StringComparison.Ordinal) >= 0, "WorkOrderControl");

            string shellSrc = File.ReadAllText(Path.Combine(FindRepoRoot(), "ShellForm.cs"));
            Check("shell hooks job strip cleared",
                shellSrc.IndexOf("JobStripCleared", StringComparison.Ordinal) >= 0, "ShellForm");
            Check("shell restores saved job customer",
                shellSrc.IndexOf("SavedJobCustomer", StringComparison.Ordinal) >= 0
                && shellSrc.IndexOf("_settings.Customer", StringComparison.Ordinal) >= 0, "ShellForm");
        }
        finally
        {
            AppSettings.PathOverride = prevPath;
        }
    }

    static bool HasPacketTemp(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
        string[] files;
        try { files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories); }
        catch { return false; }
        for (int i = 0; i < files.Length; i++)
        {
            string name = Path.GetFileName(files[i]);
            if (name.IndexOf(".tmp-", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

}
