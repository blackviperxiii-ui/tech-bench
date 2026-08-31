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
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Crash("UI thread", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash("background", e.ExceptionObject as Exception);

            bool found;
            string kbRoot = KbIndex.FindRoot(out found);
            var kb = new KbIndex();
            try { kb.Load(kbRoot); }
            catch (Exception ex)
            {
                MessageBox.Show("Could not load knowledge base from:\n" + kbRoot + "\n\n" + ex.Message, "Tech Bench");
            }
            if (!found)
            {
                MessageBox.Show(
                    "No knowledge base found.\n\nExpected data\\kb.json under:\n" + kbRoot +
                    "\n\nSearch will be empty until the air-compressor-kb folder is there. You can also point at it with " +
                    "a kb-path.txt next to TechBench.exe, or the TECHBENCH_KB environment variable.",
                    "Tech Bench", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
