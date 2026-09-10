using System;
using System.Windows.Forms;
using J1939Reader;

namespace TechBench
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            if (Updater.TryBeginPendingSwap(exeDir))
                return;

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
                // Search status line names the failure. Do not block launch with a dialog.
                kb.Status = "Could not load knowledge base from " + kbRoot + ": " + ex.Message;
            }

            Application.Run(new ShellForm(kb));
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
