using System.Drawing;
using System.Windows.Forms;

namespace TechBench
{
    /// <summary>TB1d: fatal error dialog with copy-to-clipboard path.</summary>
    internal sealed class CrashForm : Form
    {
        public CrashForm(string message)
        {
            Text = "Tech Bench";
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(480, 160);
            var label = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Text = message ?? "",
                Padding = new Padding(12)
            };
            Controls.Add(label);
        }
    }
}
