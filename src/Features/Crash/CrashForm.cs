using System;
using System.Diagnostics;
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
        readonly string _logPath;

        public CrashForm(string message, string clipboardText)
            : this(message, clipboardText, null)
        {
        }

        public CrashForm(string message, string clipboardText, string logPath)
        {
            _clipboardText = clipboardText ?? "";
            _logPath = logPath ?? "";
            Text = "Tech Bench";
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = true;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(520, 200);

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
            var openLog = new Button { Text = "Open log folder", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
            string logDir = string.IsNullOrEmpty(_logPath) ? "" : (Path.GetDirectoryName(_logPath) ?? "");
            openLog.Enabled = logDir.Length > 0 && Directory.Exists(logDir);
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
            openLog.Click += delegate { OpenLogFolder(); };
            buttons.Controls.Add(restart);
            buttons.Controls.Add(copy);
            buttons.Controls.Add(openLog);

            Controls.Add(label);
            Controls.Add(buttons);
        }

        void OpenLogFolder()
        {
            try
            {
                string dir = Path.GetDirectoryName(_logPath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    MessageBox.Show(this, "The crash log folder is not available.",
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open the log folder: " + ex.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
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
