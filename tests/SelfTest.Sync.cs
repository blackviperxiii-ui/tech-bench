using System;
using System.Collections.Generic;
using System.IO;
using TechBench;

static partial class SelfTest
{
    [SelfTestCase("shop watch folders", Order = 251)]
    static void ShopWatchTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "tb-watch-" + Guid.NewGuid().ToString("N"));
        string other = Path.Combine(Path.GetTempPath(), "tb-watch-usb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(other);
        try
        {
            Eq("no kb root watches nothing", ShopWatch.Dirs("", new ShopSyncSettings()).Count, 0);
            Eq("null kb root watches nothing", ShopWatch.Dirs(null, null).Count, 0);

            List<string> shared = ShopWatch.Dirs(root, new ShopSyncSettings { SyncFolder = "" });
            Eq("shared tree watches three folders", shared.Count, 3);
            Check("first is shop folder", shared[0].StartsWith(root, StringComparison.Ordinal)
                && shared[0].EndsWith("data\\shop", StringComparison.Ordinal), shared[0]);
            Check("second is notes folder", shared[1].EndsWith("data\\notes", StringComparison.Ordinal), shared[1]);
            Check("third is files folder", shared[2].EndsWith("data\\files", StringComparison.Ordinal), shared[2]);

            Eq("null settings watches three folders", ShopWatch.Dirs(root, null).Count, 3);
            Eq("sync folder equal to kb root is shared", ShopWatch.Dirs(root, new ShopSyncSettings { SyncFolder = root }).Count, 3);
            Eq("missing sync folder is not watched",
                ShopWatch.Dirs(root, new ShopSyncSettings { SyncFolder = Path.Combine(other, "gone") }).Count, 3);

            List<string> usb = ShopWatch.Dirs(root, new ShopSyncSettings { SyncFolder = other });
            Eq("separate sync folder adds a fourth", usb.Count, 4);
            Eq("fourth is the sync folder itself", usb[3], other);
            Check("Dirs creates nothing", !Directory.Exists(shared[0]), "created " + shared[0]);

            string shellSrc = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Shell", "ShellForm.cs"));
            Check("shell watches through ShopWatcher",
                shellSrc.IndexOf("new ShopWatcher(this,", StringComparison.Ordinal) >= 0
                && shellSrc.IndexOf("FileSystemWatcher", StringComparison.Ordinal) < 0,
                "ShellForm still owns FileSystemWatcher");
        }
        finally
        {
            try { Directory.Delete(root, true); }
            catch { }
            try { Directory.Delete(other, true); }
            catch { }
        }
    }
}
