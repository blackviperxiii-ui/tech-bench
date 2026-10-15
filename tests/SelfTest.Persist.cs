// Startup persist guards: settings writes during load must not wipe saved state.
// Run with test.bat.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TechBench;

static partial class SelfTest
{
    [SelfTestCase("persist restart and bad store", Order = 221)]
    static void PersistRestartAndBadStoreTests()
    {
        SettingsWriteGate.ResetForTests();

        string dir = Path.Combine(Path.GetTempPath(), "tb-persist-restart-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string prevSettings = AppSettings.PathOverride;
        string path = Path.Combine(dir, "settings.json");
        AppSettings.PathOverride = path;
        try
        {
            // Missing store: defaults, nothing created by a read.
            AppSettings none = AppSettings.Load();
            Eq("missing store customer empty", none.Customer, "");
            Eq("missing store last wo empty", none.LastWo, "");
            Check("read does not create the store", !File.Exists(path), path);

            // Persist customer + last WO, then "restart" (gate reset, fresh Load from disk).
            Eq("save customer trims", SettingsStore.SaveJobCustomer("  Acme Quarry  "), "Acme Quarry");
            Eq("save last wo trims", SettingsStore.SaveJobWorkOrder(" 44551-01 "), "44551-01");
            SettingsWriteGate.ResetForTests();
            AppSettings restarted = AppSettings.Load();
            Eq("customer restored after restart", restarted.Customer, "Acme Quarry");
            Eq("last wo restored after restart", restarted.LastWo, "44551-01");
            Eq("null last wo saves empty", SettingsStore.SaveJobWorkOrder(null), "");
            Eq("null last wo persisted", AppSettings.Load().LastWo, "");
            Eq("customer kept when only wo cleared", AppSettings.Load().Customer, "Acme Quarry");

            // Corrupt or hostile stores fall back to defaults and never throw.
            string[] bad =
            {
                "",
                "   ",
                "{ not json",
                "{\"Customer\":\"Trunc",
                "null",
                "[]",
                "{\"Width\":\"abc\"}",
                "{\"Customer\":null,\"LastWo\":null}"
            };
            for (int i = 0; i < bad.Length; i++)
            {
                File.WriteAllText(path, bad[i]);
                AppSettings s = AppSettings.Load();
                Check("bad store #" + i + " loads defaults",
                    s != null && s.Customer == "" && s.LastWo == "", bad[i]);
            }

            // Writing over a corrupt store recovers: new values persist and read back.
            File.WriteAllText(path, "{ corrupt");
            Eq("save over corrupt store", SettingsStore.SaveJobCustomer("Recovered Co"), "Recovered Co");
            Eq("corrupt store recovered", AppSettings.Load().Customer, "Recovered Co");
            File.WriteAllText(path, "{ corrupt");
            Eq("wo save over corrupt store", SettingsStore.SaveJobWorkOrder("12-34"), "12-34");
            Eq("wo recovered from corrupt store", AppSettings.Load().LastWo, "12-34");

            // A saved WO key that is gone from the list restores to nothing.
            Eq("stale wo key restores empty", AppSettings.MatchWorkOrderKey(new System.Collections.Generic.List<WorkOrder>(), "gone-99"), "");
            Eq("null order list restores empty", AppSettings.MatchWorkOrderKey(null, "12-34"), "");
        }
        finally
        {
            AppSettings.PathOverride = prevSettings;
            SettingsWriteGate.ResetForTests();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [SelfTestCase("persist startup (no wipe)", Order = 220)]
    static void PersistStartupTests()
    {
        SettingsWriteGate.ResetForTests();

        string dir = Path.Combine(Path.GetTempPath(), "tb-persist-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string prevSettings = AppSettings.PathOverride;
        string prevPrefs = AdapterPrefs.FolderOverride;
        AppSettings.PathOverride = Path.Combine(dir, "settings.json");
        AdapterPrefs.FolderOverride = dir;
        try
        {
            var job = new AppSettings
            {
                Model = "HP450",
                Serial = "S-99",
                Customer = "Acme Quarry",
                LastWo = "44551-01",
                Width = 1180,
                Height = 720
            };
            job.SaveTo(AppSettings.PathOverride);
            AdapterPrefs.Save(new AdapterPrefsData
            {
                AdapterId = "CMNSI7",
                ShellPage = 2,
                InnerPage = 4
            });

            string settingsPath = AppSettings.PathOverride;
            string prefsPath = AdapterPrefs.PathName();
            string settingsBefore = File.ReadAllText(settingsPath);
            string prefsBefore = File.ReadAllText(prefsPath);
            string settingsHash = Sha256Hex(settingsBefore);
            string prefsHash = Sha256Hex(prefsBefore);

            // Simulated launch: controls fill and fire TextChanged / SelectedIndexChanged /
            // ShowPage(0) persist. While the write gate is closed those must not touch disk.
            SettingsWriteGate.BeginLoad();
            Check("gate closed during load", SettingsWriteGate.IsLoading, "not loading");
            Check("writes refused during load", !SettingsWriteGate.AllowWrite(), "allowed");

            var wipeJob = new AppSettings
            {
                Model = "",
                Serial = "",
                Customer = "",
                LastWo = "",
                Width = 800,
                Height = 600
            };
            wipeJob.Save();
            wipeJob.SaveTo(settingsPath);
            AdapterPrefs.Merge("", 0, 0);
            AdapterPrefs.Save(new AdapterPrefsData { AdapterId = "", ShellPage = 0, InnerPage = 0 });

            Eq("settings hash unchanged during load", Sha256Hex(File.ReadAllText(settingsPath)), settingsHash);
            Eq("adapter-prefs hash unchanged during load", Sha256Hex(File.ReadAllText(prefsPath)), prefsHash);
            Eq("settings body unchanged during load", File.ReadAllText(settingsPath), settingsBefore);
            Eq("prefs body unchanged during load", File.ReadAllText(prefsPath), prefsBefore);

            var still = AppSettings.Load();
            Eq("customer survives simulated startup", still.Customer, "Acme Quarry");
            Eq("last wo survives simulated startup", still.LastWo, "44551-01");
            Eq("model survives simulated startup", still.Model, "HP450");
            AdapterPrefsData prefsStill = AdapterPrefs.Load();
            Eq("adapter id survives simulated startup", prefsStill.AdapterId, "CMNSI7");
            Eq("shell page survives simulated startup", prefsStill.ShellPage, 2);
            Eq("inner page survives simulated startup", prefsStill.InnerPage, 4);

            SettingsWriteGate.EndLoad();
            Check("gate open after EndLoad", !SettingsWriteGate.IsLoading, "still loading");
            Check("writes allowed after EndLoad", SettingsWriteGate.AllowWrite(), "refused");

            // Nested BeginLoad: one EndLoad must not reopen while outer load is active.
            SettingsWriteGate.BeginLoad();
            SettingsWriteGate.BeginLoad();
            SettingsWriteGate.EndLoad();
            Check("nested load still closed", SettingsWriteGate.IsLoading, "opened early");
            AdapterPrefs.Merge("WIPED", 1, 1);
            Eq("nested load still blocks prefs write", AdapterPrefs.Load().AdapterId, "CMNSI7");
            SettingsWriteGate.EndLoad();

            // After load, a real tech change must persist.
            AdapterPrefs.Merge("OTHER", 3, 1);
            AdapterPrefsData after = AdapterPrefs.Load();
            Eq("post-load adapter merge writes", after.AdapterId, "OTHER");
            Eq("post-load shell merge writes", after.ShellPage, 3);
            Eq("post-load inner merge writes", after.InnerPage, 1);

            var realJob = AppSettings.Load();
            realJob.Customer = "Beta Co";
            realJob.SaveTo(settingsPath);
            Eq("post-load job save writes", AppSettings.Load().Customer, "Beta Co");
            Check("settings write keeps .bak", File.Exists(settingsPath + ".bak"), settingsPath + ".bak");

            // SettingsStore owns job-strip + adapter merge routing.
            Eq("SettingsStore.SaveJobCustomer", SettingsStore.SaveJobCustomer("Gamma LLC"), "Gamma LLC");
            Eq("job customer on disk via store", AppSettings.Load().Customer, "Gamma LLC");
            Eq("SettingsStore.SaveJobWorkOrder", SettingsStore.SaveJobWorkOrder("99-01"), "99-01");
            Eq("last wo on disk via store", AppSettings.Load().LastWo, "99-01");
            SettingsStore.ClearJobStrip();
            Eq("ClearJobStrip wipes customer", AppSettings.Load().Customer, "");
            Eq("ClearJobStrip wipes last wo", AppSettings.Load().LastWo, "");
            SettingsStore.MergeAdapterPrefs("CMNSI7", 2, 4);
            AdapterPrefsData viaStore = AdapterPrefs.Load();
            Eq("MergeAdapterPrefs id", viaStore.AdapterId, "CMNSI7");
            Eq("MergeAdapterPrefs shell", viaStore.ShellPage, 2);
            Check("adapter-prefs write keeps .bak", File.Exists(prefsPath + ".bak"), prefsPath + ".bak");

            SettingsStore.BeginLoad();
            Check("SettingsStore gate closed", SettingsStore.IsLoading, "open");
            SettingsStore.SaveJobCustomer("DuringLoad");
            Eq("job write refused during SettingsStore load", AppSettings.Load().Customer, "");
            SettingsStore.EndLoad();
            Check("SettingsWriteGate shares SettingsStore depth", !SettingsWriteGate.IsLoading, "still loading");

            // Source-level guards: startup must opt out of persist and wrap fills.
            string shellSrc = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Shell", "ShellForm.cs"));
            Check("shell ShowPage startup skips persist",
                shellSrc.IndexOf("ShowPage(0, false)", StringComparison.Ordinal) >= 0
                && shellSrc.IndexOf("ShowPage(shell, false)", StringComparison.Ordinal) >= 0,
                "ShellForm missing persistShellPage:false on startup");
            Check("shell wraps job fill with write gate",
                shellSrc.IndexOf("SettingsWriteGate.BeginLoad()", StringComparison.Ordinal) >= 0
                && shellSrc.IndexOf("_filling = true", StringComparison.Ordinal) >= 0,
                "ShellForm");
            Check("shell routes page persist through SettingsStore",
                shellSrc.IndexOf("SettingsStore.MergeAdapterPrefs", StringComparison.Ordinal) >= 0
                && shellSrc.IndexOf("SettingsStore.SaveAppSettings", StringComparison.Ordinal) >= 0,
                "ShellForm");

            string inlineSrc = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Features", "Inline7", "Inline7Control.cs"));
            Check("INLINE 7 blocks persist while applying prefs",
                inlineSrc.IndexOf("_applyingAdapterPrefs", StringComparison.Ordinal) >= 0
                && inlineSrc.IndexOf("SettingsWriteGate.BeginLoad()", StringComparison.Ordinal) >= 0,
                "Inline7Control");
            Check("INLINE 7 routes prefs through SettingsStore",
                inlineSrc.IndexOf("SettingsStore.MergeAdapterPrefs", StringComparison.Ordinal) >= 0,
                "Inline7Control");

            string woSrc = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Features", "WorkOrders", "WorkOrderControl.cs"));
            Check("WO reload does not auto-pick after job restore",
                woSrc.IndexOf("_orders.Count > 0 && !_jobWoRestoreDone", StringComparison.Ordinal) >= 0,
                "WorkOrderControl");
            Check("job strip routes through SettingsStore",
                woSrc.IndexOf("SettingsStore.SaveJobCustomer", StringComparison.Ordinal) >= 0
                && woSrc.IndexOf("SettingsStore.ClearJobStrip", StringComparison.Ordinal) >= 0,
                "WorkOrderControl");

            string appSrc = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Core", "Settings", "AppSettings.cs"));
            Check("AppSettings.SaveTo uses SettingsStore.WriteText",
                appSrc.IndexOf("SettingsStore.WriteText", StringComparison.Ordinal) >= 0, "AppSettings");
        }
        finally
        {
            SettingsStore.ResetForTests();
            AppSettings.PathOverride = prevSettings;
            AdapterPrefs.FolderOverride = prevPrefs;
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    static string Sha256Hex(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
        using (var sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
