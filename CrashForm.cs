using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace TechBench
{
    /// <summary>TB1d: fatal error dialog with local crash log, copy details, and restart.</summary>
    internal sealed class CrashForm : Form
    {
        const string LogFileName = "techbench-crash.log";

        /// <summary>SelfTest: write the crash log under this folder instead of next to the exe.</summary>
        internal static string LogDirectoryOverride;

        readonly string _clipboardText;

        public CrashForm(string message, string clipboardText)
        {
            _clipboardText = clipboardText ?? "";
            Text = "Tech Bench";
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = true;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(480, 200);

            var label = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Text = message ?? "",
                Padding = new Padding(12, 12, 12, 4)
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(12, 4, 12, 12),
                WrapContents = false
            };
            var restart = new Button { Text = "Restart", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
            var copy = new Button { Text = "Copy details", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
            restart.Click += delegate
            {
                try { Application.Restart(); }
                catch { }
                Environment.Exit(0);
            };
            copy.Click += delegate
            {
                try
                {
                    if (!string.IsNullOrEmpty(_clipboardText))
                        Clipboard.SetText(_clipboardText);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not copy to the clipboard: " + ex.Message,
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            buttons.Controls.Add(restart);
            buttons.Controls.Add(copy);

            Controls.Add(label);
            Controls.Add(buttons);
        }

        /// <summary>Append a crash record next to the exe (local only; no network).</summary>
        public static string WriteCrashLog(string where, Exception ex)
        {
            try
            {
                string dir = LogDirectoryOverride;
                if (string.IsNullOrEmpty(dir))
                    dir = AppDomain.CurrentDomain.BaseDirectory;
                string path = Path.Combine(dir, LogFileName);
                var sb = new StringBuilder();
                sb.AppendLine("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + where + " ===");
                sb.AppendLine(ex == null ? "(no exception object)" : ex.ToString());
                sb.AppendLine();
                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
                return path;
            }
            catch { return null; }
        }

        public static string BuildClipboardText(string where, Exception ex, string logPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Tech Bench crash");
            sb.AppendLine("Where: " + (where ?? ""));
            if (!string.IsNullOrEmpty(logPath))
                sb.AppendLine("Log: " + logPath);
            sb.AppendLine();
            sb.AppendLine(ex == null ? "(no exception object)" : ex.ToString());
            return sb.ToString();
        }
    }
}
