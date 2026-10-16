using System;
using System.IO;
using System.Text;
using TechBench;

static partial class SelfTest
{
    [SelfTestCase("shop settings migration", Order = 261)]
    static void ShopSettingsTests()
    {
        string prevFolder = ShopSettings.FolderOverride;
        string prevExe = ShopSettings.ExeDirOverride;
        string root = Path.Combine(Path.GetTempPath(), "tb-shopset-" + Guid.NewGuid().ToString("N"));
        string cfg = Path.Combine(root, "cfg");
        string exe = Path.Combine(root, "exe");
        Directory.CreateDirectory(cfg);
        Directory.CreateDirectory(exe);
        ShopSettings.FolderOverride = cfg;
        ShopSettings.ExeDirOverride = exe;
        try
        {
            string shopPath = Path.Combine(cfg, "shop-settings.json");
            string legacyPath = Path.Combine(cfg, "id-settings.json");
            string legacyJson = "{\"TechId\":\"alice\",\"TechNumber\":\"T9\",\"Company\":\"01\",\"Location\":\"02\","
                + "\"GatewayUrl\":\"https://dealer.example\",\"SubscriptionKey\":\"\",\"ClientId\":\"cid\","
                + "\"ClientSecret\":\"\",\"TokenUrl\":\"https://token.example\","
                + "\"SubscriptionKeyProtected\":\"AQAAkeyblob\",\"ClientSecretProtected\":\"AQAAsecretblob\","
                + "\"ShareFolder\":\"S:\\\\share\",\"AssignedFile\":\"C:\\\\wo.json\","
                + "\"AssignedPath\":\"/a\",\"LogOnPath\":\"/b\",\"LogOffPath\":\"/c\",\"SignOffPath\":\"/d\",\"MultimediaPath\":\"/e\"}";
            File.WriteAllText(legacyPath, legacyJson);
            byte[] legacyBefore = File.ReadAllBytes(legacyPath);

            ShopSettings migrated = ShopSettings.Load();
            Eq("migrates tech id", migrated.TechId, "alice");
            Eq("migrates share folder", migrated.ShareFolder, "S:\\share");
            Eq("migrates assigned file", migrated.AssignedFile, "C:\\wo.json");
            Eq("migration writes only the three shop fields", File.ReadAllText(shopPath),
                "{\"TechId\":\"alice\",\"ShareFolder\":\"S:\\\\share\",\"AssignedFile\":\"C:\\\\wo.json\"}");
            Check("old settings file is byte-identical",
                Convert.ToBase64String(File.ReadAllBytes(legacyPath)) == Convert.ToBase64String(legacyBefore),
                File.ReadAllText(legacyPath));

            File.WriteAllText(legacyPath, legacyJson.Replace("alice", "bob"));
            Eq("old settings file is not read after migration", ShopSettings.Load().TechId, "alice");

            var saved = new ShopSettings { TechId = "carol", ShareFolder = "", AssignedFile = "" };
            saved.Save();
            File.WriteAllText(Path.Combine(exe, "shop-settings.json"), "{\"TechId\":\"exe-tech\",\"ShareFolder\":\"B:\\\\beside\"}");
            File.WriteAllText(Path.Combine(exe, "id-settings.json"),
                "{\"AssignedFile\":\"D:\\\\list.csv\",\"SubscriptionKeyProtected\":\"AQAAleak\"}");
            ShopSettings overlaid = ShopSettings.Load();
            Eq("local tech id wins over beside-exe", overlaid.TechId, "carol");
            Eq("beside-exe file fills blank share", overlaid.ShareFolder, "B:\\beside");
            Eq("beside-exe old file fills blank assigned file", overlaid.AssignedFile, "D:\\list.csv");
            Eq("loading does not rewrite saved settings", File.ReadAllText(shopPath),
                "{\"TechId\":\"carol\",\"ShareFolder\":\"\",\"AssignedFile\":\"\"}");

            File.WriteAllText(legacyPath, legacyJson);
            File.WriteAllText(shopPath, "");
            Eq("empty shop file re-runs migration", ShopSettings.Load().TechId, "alice");
            File.WriteAllText(shopPath, "{\"TechId\":\"al");
            Eq("truncated shop file re-runs migration", ShopSettings.Load().TechId, "alice");
            Eq("truncated shop file is repaired", ShopSettings.LoadFrom(shopPath).TechId, "alice");

            File.Delete(shopPath);
            File.Delete(legacyPath);
            File.Delete(Path.Combine(exe, "shop-settings.json"));
            File.Delete(Path.Combine(exe, "id-settings.json"));
            File.WriteAllText(Path.Combine(cfg, "sync.json"), "{\"SyncFolder\":\"Z:\\\\usb\"}");
            ShopSettings fresh = ShopSettings.Load();
            Eq("no files: tech id defaults to the Windows user", fresh.TechId, ShopSettings.DefaultTechId());
            Eq("no files: share comes from sync.json", fresh.ShareFolder, "Z:\\usb");
            Check("no files: nothing is written", !File.Exists(shopPath), shopPath);

            File.WriteAllText(legacyPath, "{\"TechId\":42,\"ShareFolder\":null}");
            ShopSettings odd = ShopSettings.ReadLegacy(legacyPath);
            Eq("non-string legacy tech id is ignored", odd.TechId, "");
            Eq("null legacy share is ignored", odd.ShareFolder, "");
            File.WriteAllText(legacyPath, "{ not json");
            Eq("corrupt legacy file reads blank", ShopSettings.ReadLegacy(legacyPath).IsBlank(), true);
            ShopSettings.Load();
            Check("corrupt legacy file writes nothing", !File.Exists(shopPath), shopPath);

            Eq("sanitize spaces and dots", ShopSettings.Sanitize(" Viper B. "), "Viper-B");
            Eq("sanitize empty", ShopSettings.Sanitize("***"), "tech");
        }
        finally
        {
            ShopSettings.FolderOverride = prevFolder;
            ShopSettings.ExeDirOverride = prevExe;
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [SelfTestCase("legacy work-order packet loads", Order = 262)]
    static void LegacyWorkOrderPacketTests()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tb-legacy-wo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string posted = "PostedTo" + "Intelli" + "Dealer";
            File.WriteAllText(Path.Combine(dir, "wo.json"),
                "{\"Number\":\"555\",\"Segment\":\"01\",\"Customer\":\"Old Co\",\"Source\":\"gateway\","
                + "\"ClockState\":\"signed-off\",\"ApiMessage\":\"ok\",\"ApiLogOn\":true,\"ApiSignOff\":true,"
                + "\"Clock\":[{\"Kind\":\"signoff\",\"WhenUtc\":\"2026-01-02T03:04:05Z\",\"Detail\":\"Signed off\",\"" + posted + "\":true},"
                + "{\"Kind\":\"shop-logon\",\"WhenUtc\":\"2026-01-02T01:00:00Z\",\"Detail\":\"Shop record only.\",\"" + posted + "\":false}]}",
                new UTF8Encoding(false));
            WorkOrder wo = WorkOrderStore.Load(dir);
            Check("legacy packet loads", wo != null, dir);
            if (wo == null) return;
            Eq("legacy packet number", wo.Key(), "555-01");
            Eq("legacy packet customer", wo.Customer, "Old Co");
            Eq("legacy packet list label", wo.ListLabel(), "555-01  Old Co");
            Eq("legacy clock count", wo.Clock.Count, 2);
            Eq("legacy sign-off renders as history", wo.Clock[0].ToString(), "2026-01-02T03:04:05Z  signoff  [shop]  Signed off");
            Eq("legacy shop clock renders", wo.Clock[1].ToString(), "2026-01-02T01:00:00Z  shop-logon  [shop]  Shop record only.");

            WorkOrderClock ev = wo.AddClock("shop-logoff", "Shop record only.");
            Eq("shop clock keeps kind", ev.Kind, "shop-logoff");
            Eq("shop clock appended", wo.Clock.Count, 3);

            string control = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Features", "WorkOrders", "WorkOrderControl.cs"));
            Check("work-order page makes no web calls",
                control.IndexOf("WebRequest", StringComparison.Ordinal) < 0
                && control.IndexOf("HttpClient", StringComparison.Ordinal) < 0
                && control.IndexOf("http", StringComparison.OrdinalIgnoreCase) < 0, "WorkOrderControl");
            Check("work-order page keeps log on and log off",
                control.IndexOf("ShopClock(\"logon\")", StringComparison.Ordinal) >= 0
                && control.IndexOf("ShopClock(\"logoff\")", StringComparison.Ordinal) >= 0, "WorkOrderControl");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
