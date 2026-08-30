using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using J1939Reader;

namespace TechBench
{
    public class ShellForm : Form
    {
        readonly TextBox _model;
        readonly TextBox _serial;
        readonly Inline7Control _inline;
        readonly SearchControl _search;
        readonly TabControl _tabs;

        public ShellForm(KbIndex kb)
        {
            Text = "Tech Bench";
            Width = 1180;
            Height = 800;
            MinimumSize = new Size(960, 640);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            string assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
            try
            {
                string ico = Path.Combine(assets, "app.ico");
                if (File.Exists(ico)) Icon = new Icon(ico);
            }
            catch { }

            var job = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.FromArgb(22, 32, 48) };
            var l1 = new Label { Text = "Model", ForeColor = Color.WhiteSmoke, Left = 10, Top = 12, Width = 48, AutoSize = false };
            _model = new TextBox { Left = 58, Top = 9, Width = 180, Height = 24 };
            var l2 = new Label { Text = "Serial", ForeColor = Color.WhiteSmoke, Left = 250, Top = 12, Width = 48, AutoSize = false };
            _serial = new TextBox { Left = 298, Top = 9, Width = 260, Height = 24 };
            var useJob = new Button { Text = "Search this job", Left = 570, Top = 8, Width = 130, Height = 26 };
            var hint = new Label
            {
                Text = "Service passwords in Search are for trained use. INLINE 7: close USB-Link Explorer first.",
                ForeColor = Color.Silver, Left = 710, Top = 13, Width = 440, AutoSize = false
            };
            useJob.Click += (s, e) =>
            {
                string q = (_model.Text + " " + _serial.Text).Trim();
                _tabs.SelectedIndex = 0;
                _search.Prefill(q);
                SyncJob();
            };
            // TextChanged, not Leave: saving a session straight after typing the serial used to write the
            // file without the job tag because focus had never left the box.
            _model.TextChanged += (s, e) => SyncJob();
            _serial.TextChanged += (s, e) => SyncJob();
            job.Controls.AddRange(new Control[] { l1, _model, l2, _serial, useJob, hint });

            _tabs = new TabControl { Dock = DockStyle.Fill };
            try
            {
                var il = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(32, 32) };
                string s = Path.Combine(assets, "search32.png");
                string i = Path.Combine(assets, "inline32.png");
                string a = Path.Combine(assets, "app32.png");
                if (File.Exists(s)) il.Images.Add("search", Image.FromFile(s));
                if (File.Exists(i)) il.Images.Add("inline", Image.FromFile(i));
                if (File.Exists(a)) il.Images.Add("app", Image.FromFile(a));
                if (il.Images.Count > 0) _tabs.ImageList = il;
            }
            catch { }
            _search = new SearchControl(kb);
            _inline = new Inline7Control();

            var pSearch = new TabPage("Search");
            pSearch.Controls.Add(_search);
            if (_tabs.ImageList != null && _tabs.ImageList.Images.ContainsKey("search")) pSearch.ImageKey = "search";
            var pInline = new TabPage("INLINE 7");
            pInline.Controls.Add(_inline);
            if (_tabs.ImageList != null && _tabs.ImageList.Images.ContainsKey("inline")) pInline.ImageKey = "inline";
            var pMore = new TabPage("More adapters");
            if (_tabs.ImageList != null && _tabs.ImageList.Images.ContainsKey("app")) pMore.ImageKey = "app";
            pMore.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 12f),
                Text = "Sullair / IR Xe / other datalinks come later.\r\nThis tab is a placeholder so the app can grow without a rewrite."
            });
            _tabs.TabPages.Add(pSearch);
            _tabs.TabPages.Add(pInline);
            _tabs.TabPages.Add(pMore);

            Controls.Add(_tabs);
            Controls.Add(job);

            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if ((e.Control && e.KeyCode == Keys.F) || e.KeyCode == Keys.F3)
                {
                    _tabs.SelectedIndex = 0;
                    _search.FocusQuery();
                    e.Handled = true;
                }
            };
        }

        void SyncJob()
        {
            string tag = (_model.Text + " " + _serial.Text).Trim();
            _inline.JobTag = tag;
            Text = string.IsNullOrEmpty(tag) ? "Tech Bench" : ("Tech Bench  ·  " + tag);
        }
    }
}
