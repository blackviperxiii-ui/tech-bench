using System;
using System.Windows.Forms;
using J1939Reader;

namespace TechBench
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (WantsSmoke(args))
                return RunSmoke(args);

            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            if (Updater.TryBeginPendingSwap(exeDir))
                return 0;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Crash("UI thread", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash("background", e.ExceptionObject as Exception);

            string kbRoot = KbIndex.FindRoot();
            var kb = new KbIndex();
            try { kb.Load(kbRoot); }
            catch (Exception ex)
            {
                // Search status line names the failure. Do not block launch with a dialog —
                // INLINE 7 still works with an empty index.
                kb.Status = "Could not load knowledge base from " + kbRoot + ": " + ex.Message;
            }

            Application.Run(new ShellForm(kb));
            return 0;
        }

        static bool WantsSmoke(string[] args)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--smoke", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        static int RunSmoke(string[] args)
        {
            string outPath = null;
            bool wantOut = false;
            try
            {
                if (args != null)
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (!string.Equals(args[i], "--smoke-out", StringComparison.OrdinalIgnoreCase))
                            continue;
                        wantOut = true;
                        if (i + 1 < args.Length) outPath = args[i + 1];
                    }
                }
                if (wantOut && string.IsNullOrEmpty(outPath))
                {
                    InstallSmoke.EmitLine("smoke FAIL: --smoke-out needs a path");
                    return 1;
                }

                SmokeResult result = InstallSmoke.Run();
                InstallSmoke.Emit(result);
                if (wantOut && !InstallSmoke.TryWriteReport(outPath, result.Line))
                    return 1;
                return result.ExitCode;
            }
            catch (Exception ex)
            {
                string line = "smoke FAIL: " + (ex == null || ex.Message == null ? "smoke failed" : ex.Message);
                InstallSmoke.EmitLine(line);
                if (wantOut && !string.IsNullOrEmpty(outPath))
                    InstallSmoke.TryWriteReport(outPath, line);
                return 1;
            }
        }

        static void Crash(string where, Exception ex)
        {
            string path = SessionIo.LogError(where, ex);
            string detail = ex == null ? "(no detail)" : ex.Message;
            MessageBox.Show(
                "Tech Bench hit an unexpected error (" + where + "):\n\n" + detail +
                (path == null ? "" : "\n\nDetails written to:\n" + path),
                "Tech Bench", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
