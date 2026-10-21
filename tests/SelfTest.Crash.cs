// Offline checks for crash log writing (TB1d).
using System;
using System.IO;
using System.Threading;
using TechBench;

static partial class SelfTest
{
    [SelfTestCase("crash log (TB1d)", Order = 270)]
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

            string program = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Shell", "Program.cs"));
            int show = program.IndexOf("form.ShowDialog()", StringComparison.Ordinal);
            int exit = program.IndexOf("Environment.Exit(1)", StringComparison.Ordinal);
            Check("Crash handler exits after the dialog returns",
                show >= 0 && exit > show, "Program.Crash");

            // A background-thread crash lands on an MTA thread; the dialog and clipboard need STA.
            string seen = "not run";
            string thrown = "none";
            var mta = new Thread(delegate()
            {
                CrashForm.RunOnSta(delegate { seen = Thread.CurrentThread.GetApartmentState().ToString(); });
                try { CrashForm.RunOnSta(delegate { throw new InvalidOperationException("sta marker"); }); }
                catch (InvalidOperationException e) { thrown = e.Message; }
            });
            mta.SetApartmentState(ApartmentState.MTA);
            mta.Start();
            mta.Join();
            Eq("crash dialog runs on STA from an MTA thread", seen, "STA");
            Eq("crash dialog failure reaches the caller", thrown, "sta marker");
        }
        finally
        {
            CrashForm.LogDirectoryOverride = prev;
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
