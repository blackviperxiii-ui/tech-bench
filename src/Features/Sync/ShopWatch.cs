using System.Collections.Generic;
using System.IO;

namespace TechBench
{
    /// <summary>Which folders a shop sync should watch for changes. Pure apart from Directory.Exists.</summary>
    internal static class ShopWatch
    {
        public static List<string> Dirs(string kbRoot, ShopSyncSettings settings)
        {
            var dirs = new List<string>();
            if (string.IsNullOrEmpty(kbRoot)) return dirs;
            dirs.Add(Path.Combine(kbRoot, ShopSync.ShopRel));
            dirs.Add(Path.Combine(kbRoot, ShopSync.NotesRel));
            dirs.Add(Path.Combine(kbRoot, ShopSync.FilesRel));
            if (settings != null && !ShopSync.IsSharedTree(kbRoot, settings) && Directory.Exists(settings.SyncFolder))
                dirs.Add(settings.SyncFolder);
            return dirs;
        }
    }
}
