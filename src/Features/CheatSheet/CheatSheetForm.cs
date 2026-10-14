using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace TechBench
{
    /// <summary>TB2b: Help / F1 cheat sheet (embedded assets/cheatsheet.rtf).</summary>
    internal sealed class CheatSheetForm : Form
    {
        internal const string RtfResourceName = "TechBench.assets.cheatsheet.rtf";

        readonly RichTextBox _body;

        public CheatSheetForm()
        {
            Text = "Tech Bench — quick reference";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9.5f);
            MinimumSize = new Size(480, 320);
            ClientSize = new Size(640, 480);
            KeyPreview = true;

            _body = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Window,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                DetectUrls = false
            };

            var close = new Button
            {
                Text = "Close",
                AutoSize = true,
                DialogResult = DialogResult.Cancel
            };
            var foot = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8, 4, 8, 8)
            };
            foot.Controls.Add(close);
            CancelButton = close;

            Controls.Add(_body);
            Controls.Add(foot);

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F1)
                {
                    BringToFront();
                    Activate();
                    Focus();
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    Close();
                    e.SuppressKeyPress = true;
                }
            };

            Shown += delegate { UiLayout.FitToWorkingArea(this); };
            Load += delegate { LoadRtf(); };
        }

        void LoadRtf()
        {
            Stream src = Assembly.GetExecutingAssembly().GetManifestResourceStream(RtfResourceName);
            if (src == null)
            {
                _body.Text = "Cheat sheet resource is missing from this build.\r\n\r\n"
                    + "Rebuild with build.bat so assets\\cheatsheet.rtf is embedded.";
                return;
            }
            using (src)
            {
                try { _body.LoadFile(src, RichTextBoxStreamType.RichText); }
                catch
                {
                    _body.Text = "Could not read the embedded cheat sheet (assets\\cheatsheet.rtf).";
                }
            }
            _body.SelectionStart = 0;
            _body.ScrollToCaret();
        }
    }
}
