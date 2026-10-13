// Offline checks for crash log writing (TB1d).
using System;
using System.IO;
using TechBench;

static partial class SelfTest
{
    static void CrashFormTests()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tb-crash-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string prev = CrashForm.LogDirectoryOverride;
        CrashForm.LogDirectoryOverride = dir;
        try
        {
            var ex = new InvalidOperationException("self-test crash marker");
            string path = CrashForm.WriteCrashLog("SelfTest", ex);
            string expected = Path.Combine(dir, "techbench-crash.log");
            Eq("crash log path", path, expected);
            Check("crash log file exists", File.Exists(expected), expected);
            string text = File.ReadAllText(expected);
            Check("crash log contains where", text.IndexOf("SelfTest", StringComparison.Ordinal) >= 0, text);
            Check("crash log contains exception", text.IndexOf("self-test crash marker", StringComparison.Ordinal) >= 0, text);

            string clip = CrashForm.BuildClipboardText("SelfTest", ex, path);
            Check("clipboard text includes exception", clip.IndexOf("self-test crash marker", StringComparison.Ordinal) >= 0, clip);
            Check("clipboard text includes log path", clip.IndexOf(expected, StringComparison.Ordinal) >= 0, clip);
        }
        finally
        {
            CrashForm.LogDirectoryOverride = prev;
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
