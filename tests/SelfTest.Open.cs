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
    static void SubscriptionKeyMaskTests()
    {
        string path = Path.Combine(FindRepoRoot(), "IdSettingsForm.cs");
        string src = File.Exists(path) ? File.ReadAllText(path) : "";
        Check("IdSettingsForm.cs is readable", src.Length > 0, path);
        Check("subscription key field is masked", SourceAssignsPasswordChar(src, "_key"), "_key");
        Check("oauth secret field stays masked", SourceAssignsPasswordChar(src, "_secret"), "_secret");
    }

    static bool SourceAssignsPasswordChar(string src, string field)
    {
        if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(field)) return false;
        string needle = field + ".UseSystemPasswordChar";
        int from = 0;
        while (from < src.Length)
        {
            int i = src.IndexOf(needle, from, StringComparison.Ordinal);
            if (i < 0) return false;
            int lineStart = src.LastIndexOf('\n', i);
            if (lineStart < 0) lineStart = 0;
            else lineStart = lineStart + 1;
            int lineEnd = src.IndexOf('\n', i);
            if (lineEnd < 0) lineEnd = src.Length;
            string line = src.Substring(lineStart, lineEnd - lineStart);
            string trim = line.Trim();
            if (trim.StartsWith("//") || trim.StartsWith("*") || trim.StartsWith("/*"))
            {
                from = i + needle.Length;
                continue;
            }
            int semi = line.IndexOf(';');
            string stmt = semi >= 0 ? line.Substring(0, semi) : line;
            if (stmt.IndexOf(needle, StringComparison.Ordinal) >= 0
                && (stmt.IndexOf("= true", StringComparison.Ordinal) >= 0
                    || stmt.IndexOf("=true", StringComparison.Ordinal) >= 0))
                return true;
            from = i + needle.Length;
        }
        return false;
    }

    static void LaunchBlockTests()
    {
        string[] blocked = new string[]
        {
            ".exe", ".com", ".bat", ".cmd", ".scr", ".pif", ".msi", ".msp",
            ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
            ".hta", ".cpl", ".reg", ".lnk", ".jar",
            ".appref-ms", ".application", ".gadget", ".msc", ".psc1", ".scf",
            ".url", ".vb", ".ws", ".inf", ".dll", ".sys"
        };
        for (int i = 0; i < blocked.Length; i++)
        {
            string ext = blocked[i];
            Check("blocked " + ext, LaunchPolicy.IsBlockedLaunchPath("manual" + ext), ext);
            Check("blocked upper " + ext, LaunchPolicy.IsBlockedLaunchPath("MANUAL" + ext.ToUpperInvariant()), ext);
        }
        Check("double extension is blocked", LaunchPolicy.IsBlockedLaunchPath("x.pdf.exe"), "pdf.exe");
        Check("double extension upper is blocked", LaunchPolicy.IsBlockedLaunchPath("X.PDF.EXE"), "PDF.EXE");
        Check("document that mentions exe is allowed", !LaunchPolicy.IsBlockedLaunchPath("x.exe.pdf"), "exe.pdf");
        Check("trailing dot exe is blocked", LaunchPolicy.IsBlockedLaunchPath("x.exe."), "dot");
        Check("trailing space exe is blocked", LaunchPolicy.IsBlockedLaunchPath("x.exe "), "space");
        Check("trailing dot and space exe is blocked", LaunchPolicy.IsBlockedLaunchPath("x.exe. "), "dot space");
        Check("alternate stream exe is blocked", LaunchPolicy.IsBlockedLaunchPath("x.pdf:evil.exe"), "ads");
        Check("alternate stream upper is blocked", LaunchPolicy.IsBlockedLaunchPath("x.pdf:evil.EXE"), "ads upper");
        Check("drive path alternate stream is blocked", LaunchPolicy.IsBlockedLaunchPath(@"C:\kb\x.pdf:evil.exe"), "drive ads");
        Check("drive-relative exe is blocked", LaunchPolicy.IsBlockedLaunchPath("C:setup.exe"), "C:setup.exe");
        Check("drive-relative pdf is allowed", !LaunchPolicy.IsBlockedLaunchPath("C:manual.pdf"), "C:manual.pdf");
        Check("drive pdf path is allowed", !LaunchPolicy.IsBlockedLaunchPath(@"C:\kb\x.pdf"), @"C:\kb\x.pdf");
        Check("exe with zone stream is blocked", LaunchPolicy.IsBlockedLaunchPath("x.exe:Zone.Identifier"), "zone");
        Check("full path exe is blocked", LaunchPolicy.IsBlockedLaunchPath(@"C:\kb\files\setup.EXE"), "full");
        Check("null path is not launched", LaunchPolicy.IsBlockedLaunchPath(null), "null");
        Check("empty path is not launched", LaunchPolicy.IsBlockedLaunchPath(""), "empty");
        Check("json is not a script", !LaunchPolicy.IsBlockedLaunchPath("kb.json"), "json");
        Check("ps1xml is not ps1", !LaunchPolicy.IsBlockedLaunchPath("x.ps1xml"), "ps1xml");
        string[] allowed = new string[] { "x.pdf", "x.jpg", "x.png", "x.txt", "x.docx", "x.xlsx", "x.exe.pdf" };
        for (int i = 0; i < allowed.Length; i++)
            Check("allowed " + allowed[i], !LaunchPolicy.IsBlockedLaunchPath(allowed[i]), allowed[i]);
        Check("document with trailing dot is allowed", !LaunchPolicy.IsBlockedLaunchPath("photo.jpg."), "jpg.");
        Check("document with trailing space is allowed", !LaunchPolicy.IsBlockedLaunchPath("photo.jpg "), "jpg space");
        string msg = LaunchPolicy.BlockedLaunchMessage(@"C:\kb\files\setup.exe");
        Check("block message names the file", msg.IndexOf("setup.exe", StringComparison.Ordinal) >= 0, msg);
        Check("block message explains the refusal",
            msg.IndexOf("won't open program or script files", StringComparison.Ordinal) >= 0
            && msg.IndexOf("Explorer", StringComparison.Ordinal) >= 0, msg);
        Check("block message survives a null path",
            LaunchPolicy.BlockedLaunchMessage(null).IndexOf("won't open", StringComparison.Ordinal) >= 0, "null msg");

        string root = FindRepoRoot();
        string search = File.Exists(Path.Combine(root, "SearchControl.cs"))
            ? File.ReadAllText(Path.Combine(root, "SearchControl.cs")) : "";
        Check("OpenSel uses SafeOpen",
            search.IndexOf("SafeOpen.TryOpen", StringComparison.Ordinal) >= 0, "SearchControl");
        string woSrc = File.Exists(Path.Combine(root, "WorkOrderControl.cs"))
            ? File.ReadAllText(Path.Combine(root, "WorkOrderControl.cs")) : "";
        Check("OpenMedia uses SafeOpen",
            woSrc.IndexOf("SafeOpen.TryOpen", StringComparison.Ordinal) >= 0, "WorkOrderControl");
        string kbSrc = File.Exists(Path.Combine(root, "KbIndex.cs"))
            ? File.ReadAllText(Path.Combine(root, "KbIndex.cs")) : "";
        Check("LaunchPolicy delegates to SafeOpen",
            kbSrc.IndexOf("SafeOpen.IsBlockedLaunchPath", StringComparison.Ordinal) >= 0, "KbIndex");
    }

    static void ManifestVersionTests()
    {
        string path = Path.Combine(FindRepoRoot(), "app.manifest");
        string xml = File.Exists(path) ? File.ReadAllText(path) : "";
        Check("app.manifest is readable", xml.Length > 0, path);
        Eq("manifest assemblyIdentity version", ManifestIdentityVersion(xml), AppVersion.Number + ".0");
    }

    static string ManifestIdentityVersion(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return "";
        int i = xml.IndexOf("<assemblyIdentity", StringComparison.Ordinal);
        if (i < 0) return "";
        int end = xml.IndexOf('>', i);
        if (end < 0) return "";
        string tag = xml.Substring(i, end - i);
        const string key = "version=\"";
        int v = tag.IndexOf(key, StringComparison.Ordinal);
        if (v < 0) return "";
        v += key.Length;
        int q = tag.IndexOf('"', v);
        if (q < 0) return "";
        return tag.Substring(v, q - v);
    }

    static void PacketSidecarSyncTests()
    {
        string prevFolder = IdSettings.FolderOverride;
        string prevExe = IdSettings.ExeDirOverride;
        string prevWo = WorkOrderStore.FolderOverride;
        string prevWoExe = WorkOrderStore.ExeDirOverride;
        string prevSync = ShopSync.SettingsFolderOverride;
        string kb = MkRoot();
        string usb = Path.Combine(Path.GetTempPath(), "tb-side-" + Guid.NewGuid().ToString("N"));
        string cfg = Path.Combine(Path.GetTempPath(), "tb-side-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(usb);
        string exe = Path.Combine(cfg, "exe");
        Directory.CreateDirectory(exe);
        IdSettings.FolderOverride = cfg;
        IdSettings.ExeDirOverride = exe;
        WorkOrderStore.FolderOverride = Path.Combine(cfg, "wo");
        WorkOrderStore.ExeDirOverride = exe;
        ShopSync.SettingsFolderOverride = cfg;
        try
        {
            string dir = Path.Combine(kb, "data", "shop", "alice", "work-orders", "9");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "wo.json"), "{\"Number\":\"9\"}");
            File.WriteAllText(Path.Combine(dir, "wo.json.bak"), "{\"Number\":\"old\"}");
            File.WriteAllText(Path.Combine(dir, "wo.json.tmp-abc"), "{ partial");
            File.WriteAllText(Path.Combine(dir, "notes.txt.tmp-zzz"), "partial notes");
            ShopSyncResult synced = ShopSync.Run(kb, new ShopSyncSettings { TechId = "alice", SyncFolder = usb });
            string remote = Path.Combine(usb, "data", "shop", "alice", "work-orders", "9");
            Check("sync copies wo.json", File.Exists(Path.Combine(remote, "wo.json")), "missing");
            Check("sync ignores wo.json.bak", !File.Exists(Path.Combine(remote, "wo.json.bak")), "bak copied");
            Check("sync ignores wo.json.tmp", !File.Exists(Path.Combine(remote, "wo.json.tmp-abc")), "tmp copied");
            Check("sync ignores notes tmp", !File.Exists(Path.Combine(remote, "notes.txt.tmp-zzz")), "notes tmp copied");
            bool sidecarConflict = false;
            foreach (ShopConflict c in synced.ConflictList)
            {
                string rel = c.RelativePath ?? "";
                if (rel.IndexOf(".bak", StringComparison.OrdinalIgnoreCase) >= 0
                    || rel.IndexOf(".tmp-", StringComparison.OrdinalIgnoreCase) >= 0)
                    sidecarConflict = true;
            }
            Check("sync does not conflict on bak or tmp", !sidecarConflict, "conflict");

            File.WriteAllText(Path.Combine(remote, "report.txt.bak"), "old report");
            File.WriteAllText(Path.Combine(remote, "report.txt.tmp-qq"), "partial report");
            ShopSync.Run(kb, new ShopSyncSettings { TechId = "alice", SyncFolder = usb });
            Check("sync does not pull bak", !File.Exists(Path.Combine(dir, "report.txt.bak")), "bak pulled");
            Check("sync does not pull tmp", !File.Exists(Path.Combine(dir, "report.txt.tmp-qq")), "tmp pulled");
        }
        finally
        {
            IdSettings.FolderOverride = prevFolder;
            IdSettings.ExeDirOverride = prevExe;
            WorkOrderStore.FolderOverride = prevWo;
            WorkOrderStore.ExeDirOverride = prevWoExe;
            ShopSync.SettingsFolderOverride = prevSync;
            try { Directory.Delete(kb, true); } catch { }
            try { Directory.Delete(usb, true); } catch { }
            try { Directory.Delete(cfg, true); } catch { }
        }
    }


    [SelfTestCase("open / launch policy", Order = 290)]
    static void OpenTests()
    {
        string err;
        Check("blocked exe is refused",
            !SafeOpen.TryOpen(@"C:\kb\files\setup.exe", out err)
            && err != null
            && err.IndexOf("won't open program or script", StringComparison.Ordinal) >= 0
            && err.IndexOf("setup.exe", StringComparison.Ordinal) >= 0, err);
        string missing = Path.Combine(Path.GetTempPath(), "tb-missing-" + Guid.NewGuid().ToString("N") + ".pdf");
        Check("missing file is refused",
            !SafeOpen.TryOpen(missing, out err)
            && err != null
            && err.IndexOf("Cannot find this file", StringComparison.Ordinal) >= 0
            && err.IndexOf(missing, StringComparison.Ordinal) >= 0, err);

        string dir = Path.Combine(Path.GetTempPath(), "tb-open-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string noAssoc = Path.Combine(dir, "note.tb_no_assoc_test");
        File.WriteAllText(noAssoc, "x");
        Check("no-association file is refused",
            !SafeOpen.TryOpen(noAssoc, out err)
            && err != null
            && err.IndexOf("No program is associated", StringComparison.Ordinal) >= 0, err);
        try { Directory.Delete(dir, true); } catch { }
    }

}
