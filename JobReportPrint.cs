using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;

namespace J1939Reader
{
    /// <summary>
    /// Printing for JobReport. Split from the text generation so the report content can be verified
    /// in SelfTest without a printer or a desktop.
    /// </summary>
    internal static class JobReportPrint
    {
        public static void Print(ReportData data, bool preview, IWin32Window owner)
        {
            List<string> lines = JobReport.Lines(data);
            int index = 0;
            using (var doc = new PrintDocument())
            using (var font = new Font("Consolas", 9f))
            {
                doc.DocumentName = "Tech Bench report";
                doc.PrintPage += delegate(object s, PrintPageEventArgs e)
                {
                    float y = e.MarginBounds.Top;
                    float lh = font.GetHeight(e.Graphics);
                    while (index < lines.Count && y + lh <= e.MarginBounds.Bottom)
                    {
                        e.Graphics.DrawString(lines[index], font, Brushes.Black, e.MarginBounds.Left, y);
                        y += lh;
                        index++;
                    }
                    e.HasMorePages = index < lines.Count;
                };
                if (preview)
                {
                    using (var dlg = new PrintPreviewDialog())
                    {
                        dlg.Document = doc;
                        dlg.Width = 900;
                        dlg.Height = 700;
                        var form = dlg as Form;
                        if (form != null) form.StartPosition = FormStartPosition.CenterParent;
                        dlg.ShowDialog(owner);
                    }
                }
                else
                {
                    using (var dlg = new PrintDialog())
                    {
                        dlg.Document = doc;
                        if (dlg.ShowDialog(owner) != DialogResult.OK) return;
                        doc.Print();
                    }
                }
            }
        }
    }
}
