using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TechBench
{
    /// <summary>
    /// Shop → Sync: who this PC is, which folder to share, last result, and explicit conflict
    /// resolution. Nothing here talks to GitHub.
    /// </summary>
    internal sealed class SyncForm : Form
    {
        readonly TextBox _tech;
        readonly TextBox _folder;
        readonly TextBox _status;
        readonly ListBox _conflicts;
        readonly string _kbRoot;
        ShopSyncResult _last;

        public ShopSyncResult LastResult { get { return _last; } }

        public SyncForm(string kbRoot, ShopSyncResult last)
        {
            _kbRoot = kbRoot;
            _last = last;
            Text = "Shop sync";
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(720, 560);
            MinimumSize = new Size(560, 420);

            ShopSyncSettings s = ShopSync.LoadSettings();

            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 3,
                AutoSize = true,
                Padding = new Padding(12, 12, 12, 4)
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            top.Controls.Add(new Label { Text = "Tech name", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, 0, 0);
            _tech = new TextBox { Dock = DockStyle.Fill, Text = s.TechId };
            top.Controls.Add(_tech, 1, 0);
            top.Controls.Add(new Label { Text = "", AutoSize = true }, 2, 0);

            top.Controls.Add(new Label { Text = "Sync folder", AutoSize = true, Margin = new Padding(0, 8, 8, 0) }, 0, 1);
            _folder = new TextBox { Dock = DockStyle.Fill, Text = s.SyncFolder ?? "" };
            top.Controls.Add(_folder, 1, 1);
            var browse = new Button { Text = "Browse…", AutoSize = true, Margin = new Padding(8, 2, 0, 0) };
            browse.Click += delegate { Browse(); };
            top.Controls.Add(browse, 2, 1);

            var hint = new Label
            {
                Dock = DockStyle.Top,
                Padding = new Padding(12, 0, 12, 8),
                Height = 52,
                ForeColor = Color.DimGray,
                Text = "Leave the folder blank if this knowledge base is already the shared tree (OneDrive). "
                     + "Otherwise pick a USB stick, network share, or a folder both techs copy to. "
                     + "Conflicts are listed below — nothing overwrites a note without you picking."
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(12, 0, 12, 8)
            };
            var save = new Button { Text = "Save settings", AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
            var sync = new Button { Text = "Sync now", AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
            var useKb = new Button { Text = "Use KB folder", AutoSize = true };
            save.Click += delegate { Persist(); RefreshStatus("Settings saved."); };
            sync.Click += delegate { Persist(); DoSync(); };
            useKb.Click += delegate { _folder.Text = ""; Persist(); RefreshStatus("Using the knowledge-base folder."); };
            buttons.Controls.Add(save);
            buttons.Controls.Add(sync);
            buttons.Controls.Add(useKb);

            _status = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 90,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                MaxLength = 0
            };

            var resolveBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Padding = new Padding(12, 8, 12, 12)
            };
            var mine = new Button { Text = "Keep mine", AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
            var theirs = new Button { Text = "Keep theirs", AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
            var both = new Button { Text = "Keep both", AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
            mine.Click += delegate { Resolve("local"); };
            theirs.Click += delegate { Resolve("remote"); };
            both.Click += delegate { Resolve("both"); };
            resolveBar.Controls.Add(mine);
            resolveBar.Controls.Add(theirs);
            resolveBar.Controls.Add(both);
            resolveBar.Controls.Add(close);
            AcceptButton = close;

            _conflicts = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            var cap = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                Padding = new Padding(12, 4, 12, 0),
                Text = "Conflicts"
            };

            Controls.Add(_conflicts);
            Controls.Add(cap);
            Controls.Add(_status);
            Controls.Add(buttons);
            Controls.Add(hint);
            Controls.Add(top);
            Controls.Add(resolveBar);

            ShowResult(_last);
        }

        void Browse()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Folder both techs can see (USB, network, or OneDrive).";
                if (!string.IsNullOrWhiteSpace(_folder.Text) && Directory.Exists(_folder.Text))
                    dlg.SelectedPath = _folder.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _folder.Text = dlg.SelectedPath;
            }
        }

        void Persist()
        {
            var s = new ShopSyncSettings
            {
                TechId = _tech.Text,
                SyncFolder = _folder.Text.Trim()
            };
            ShopSync.SaveSettings(s);
            _tech.Text = ShopSync.LoadSettings().TechId;
        }

        void DoSync()
        {
            if (string.IsNullOrEmpty(_kbRoot))
            {
                MessageBox.Show(this, "No knowledge base folder is configured.", "Shop sync",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _last = ShopSync.Run(_kbRoot, ShopSync.LoadSettings());
            try { ShopSync.ImportHistory(_kbRoot, J1939Reader.SessionIo.Folder()); }
            catch { }
            ShowResult(_last);
        }

        void Resolve(string choice)
        {
            var c = _conflicts.SelectedItem as ShopConflict;
            if (c == null)
            {
                MessageBox.Show(this, "Select a conflict first.", "Shop sync",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                ShopSync.Resolve(_kbRoot, c, choice);
                DoSync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Shop sync", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void RefreshStatus(string extra)
        {
            ShopSyncSettings s = ShopSync.LoadSettings();
            var r = _last ?? new ShopSyncResult { SharedTree = ShopSync.IsSharedTree(_kbRoot, s) };
            r.Techs = ShopSync.TechIds(_kbRoot).Count;
            string line = ShopSync.Describe(_kbRoot, s, r);
            if (!string.IsNullOrEmpty(extra)) line += "\r\n" + extra;
            _status.Text = line;
        }

        void ShowResult(ShopSyncResult r)
        {
            _last = r;
            _conflicts.Items.Clear();
            if (r == null)
            {
                RefreshStatus("");
                return;
            }
            foreach (ShopConflict c in r.ConflictList) _conflicts.Items.Add(c);
            if (_conflicts.Items.Count > 0) _conflicts.SelectedIndex = 0;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(r.Status);
            foreach (string n in r.Notes) sb.AppendLine(n);
            if (r.ConflictList.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Select a conflict, then Keep mine / Keep theirs / Keep both.");
                ShopConflict c = r.ConflictList[0];
                if (c.Kind == "code")
                    sb.AppendLine("This code exists in two tech folders with different text.");
            }
            _status.Text = sb.ToString();
        }
    }
}
