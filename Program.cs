using System;
using System.IO;
using System.Windows.Forms;

namespace TechBench
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string kbRoot = KbIndex.FindRoot();
            var kb = new KbIndex();
            try { kb.Load(kbRoot); }
            catch (Exception ex)
            {
                MessageBox.Show("Could not load knowledge base from:\n" + kbRoot + "\n\n" + ex.Message, "Tech Bench");
            }

            Application.Run(new ShellForm(kb));
        }
    }
}
