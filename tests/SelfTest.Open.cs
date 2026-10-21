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
    static void LaunchBlockTests()
    {
        string[] blocked = new string[]
        {
            ".exe", ".com", ".bat", ".cmd", ".scr", ".pif", ".msi", ".msp",
            ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
            ".hta", ".cpl", ".reg", ".lnk", ".jar",
            ".appref-ms", ".application", ".gadget", ".msc", ".psc1", ".scf",
            ".url", ".vb", ".ws", ".inf", ".dll", ".sys",
            ".chm", ".docm", ".xlsm", ".pptm", ".dotm", ".xltm", ".ppsm"
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
        // shlwapi answers a size query with S_FALSE; reading that as "no app" refused every document.
        uint txtLen, unkLen;
        int txtHr = SafeOpen.ExecutableSizeQuery(".txt", out txtLen);
        int unkHr = SafeOpen.ExecutableSizeQuery(".tbzqx9", out unkLen);
        Console.WriteLine("  assoc size query: .txt hr=0x" + txtHr.ToString("X8") + " len=" + txtLen
            + "  .tbzqx9 hr=0x" + unkHr.ToString("X8") + " len=" + unkLen);
        Check("txt has an associated app", SafeOpen.HasExecutableAssociation(@"C:\kb\notes.txt"), ".txt");
        Check("unknown extension has no app", !SafeOpen.HasExecutableAssociation("x.tbzqx9"), ".tbzqx9");
        Check("no extension has no app", !SafeOpen.HasExecutableAssociation("README"), "README");
        Check("block message survives a null path",
            LaunchPolicy.BlockedLaunchMessage(null).IndexOf("won't open", StringComparison.Ordinal) >= 0, "null msg");

        string root = FindRepoRoot();
        string searchPath = Path.Combine(root, "src", "Features", "Search", "SearchControl.cs");
        string search = File.Exists(searchPath) ? File.ReadAllText(searchPath) : "";
        Check("OpenSel uses SafeOpen",
            search.IndexOf("SafeOpen.TryOpen", StringComparison.Ordinal) >= 0, "SearchControl");
        string woPath = Path.Combine(root, "src", "Features", "WorkOrders", "WorkOrderControl.cs");
        string woSrc = File.Exists(woPath) ? File.ReadAllText(woPath) : "";
        Check("OpenMedia uses SafeOpen",
            woSrc.IndexOf("SafeOpen.TryOpen", StringComparison.Ordinal) >= 0, "WorkOrderControl");
        Check("OpenMedia uses ResolveMediaPath",
            woSrc.IndexOf("WorkOrderStore.ResolveMediaPath", StringComparison.Ordinal) >= 0, "WorkOrderControl");
        string kbPath = Path.Combine(root, "src", "Features", "Kb", "KbIndex.cs");
        string kbSrc = File.Exists(kbPath) ? File.ReadAllText(kbPath) : "";
        Check("LaunchPolicy delegates to SafeOpen",
            kbSrc.IndexOf("SafeOpen.IsBlockedLaunchPath", StringComparison.Ordinal) >= 0, "KbIndex");
        Check("KbIndex resolves manuals under root only",
            kbSrc.IndexOf("ResolveDocumentPath", StringComparison.Ordinal) >= 0
            && kbSrc.IndexOf("IsPathUnderRoot", StringComparison.Ordinal) >= 0, "KbIndex resolve");

        string kb = Path.Combine(Path.GetTempPath(), "tb-kb-doc-" + Guid.NewGuid().ToString("N"));
        string outsideDir = Path.Combine(Path.GetTempPath(), "tb-kb-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(kb, "manuals"));
        Directory.CreateDirectory(outsideDir);
        string inside = Path.Combine(kb, "manuals", "unit.pdf");
        File.WriteAllText(inside, "pdf");
        Eq("relative manual under KB resolves",
            KbIndex.ResolveDocumentPath(kb, "manuals/unit.pdf", "unit.pdf"),
            Path.GetFullPath(inside));
        string outside = Path.Combine(outsideDir, "secret.pdf");
        File.WriteAllText(outside, "x");
        Check("absolute path outside KB refused",
            KbIndex.ResolveDocumentPath(kb, outside, "secret.pdf") == null, "outside");
        Check("Windows drive path is treated as absolute",
            KbIndex.IsAbsolutePath(@"C:\Users\other\secret.pdf"), "drive");
        Check("Windows drive path outside KB refused",
            KbIndex.ResolveDocumentPath(kb, @"C:\Users\other\secret.pdf", "nope.pdf") == null, "drive outside");
        File.WriteAllText(Path.Combine(kb, "secret.pdf"), "x");
        Eq("basename under KB root resolves when present",
            KbIndex.ResolveDocumentPath(kb, outside, "secret.pdf"),
            Path.GetFullPath(Path.Combine(kb, "secret.pdf")));
        Check("IsPathUnderRoot rejects escape",
            !KbIndex.IsPathUnderRoot(kb, Path.Combine(kb, "..", "escape.pdf")), "escape");
        try { Directory.Delete(kb, true); } catch { }
        try { Directory.Delete(outsideDir, true); } catch { }
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
        string prevFolder = ShopSettings.FolderOverride;
        string prevExe = ShopSettings.ExeDirOverride;
        string prevWo = WorkOrderStore.FolderOverride;
        string prevWoExe = WorkOrderStore.ExeDirOverride;
        string prevSync = ShopSync.SettingsFolderOverride;
        string kb = MkRoot();
        string usb = Path.Combine(Path.GetTempPath(), "tb-side-" + Guid.NewGuid().ToString("N"));
        string cfg = Path.Combine(Path.GetTempPath(), "tb-side-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(usb);
        string exe = Path.Combine(cfg, "exe");
        Directory.CreateDirectory(exe);
        ShopSettings.FolderOverride = cfg;
        ShopSettings.ExeDirOverride = exe;
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
            ShopSettings.FolderOverride = prevFolder;
            ShopSettings.ExeDirOverride = prevExe;
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
