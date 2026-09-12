using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TechBench
{
    internal sealed class SearchControl : UserControl
    {
        readonly TextBox _q;
        readonly ComboBox _kind;
        readonly ListBox _list;
        readonly TextBox _detail;
        readonly TextBox _status;
        readonly Button _open;
        readonly Timer _debounce = new Timer();
        KbIndex _kb;
        Hit _sel;

        /// <summary>Currently highlighted entry, so the shell can seed the code editor from it.</summary>
        public Hit Selected { get { return _sel; } }

        public SearchControl(KbIndex kb)
        {
            _kb = kb;
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9.5f);
            Padding = new Padding(8);

            // Query stretches; kind/buttons wrap. A fixed 360×20 row left the path as "OneDriv…".
            var qHost = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(0, 0, 0, 4) };
            _q = new TextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
            qHost.Controls.Add(_q);
            var bar = UiLayout.WrapBar(new Padding(0, 0, 0, 4));
            _kind = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 130,
                Margin = new Padding(0, 2, 8, 4)
            };
            foreach (string k in new[] { "ALL", "CODE", "PASSWORD", "MANUAL", "FILTER", "EQUIP", "NOTE", "FILE" })
                _kind.Items.Add(k);
            _kind.SelectedIndex = 0;
            var go = new Button { Text = "Search", AutoSize = true, Margin = new Padding(0, 1, 8, 4) };
            _open = new Button { Text = "Open file", AutoSize = true, Enabled = false, Margin = new Padding(0, 1, 0, 4) };
            bar.Controls.Add(_kind);
            bar.Controls.Add(go);
            bar.Controls.Add(_open);

            _status = UiLayout.WrapText("");
            _status.Dock = DockStyle.Top;
            _status.ForeColor = Color.DimGray;
            _status.Padding = new Padding(0, 0, 0, 4);

            _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            _detail = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                MaxLength = 0,
                Font = new Font("Consolas", 9.5f)
            };
            // Raw SplitContainer + SplitterDistance in the ctor clamps to the default
            // 150×100 handle, then the hit list disappears and the detail TextBox
            // fills the whole client — the "one giant text box" shop techs hit.
            var split = UiLayout.Split(Orientation.Vertical, 420, 180, 180, _list, _detail);

            Controls.Add(split);
            Controls.Add(_status);
            Controls.Add(bar);
            Controls.Add(qHost);

            go.Click += delegate { RunSearch(); };
            _q.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RunSearch(); }
                else if (e.KeyCode == Keys.Down && _list.Items.Count > 0)
                {
                    e.SuppressKeyPress = true;
                    _list.Focus();
                    if (_list.SelectedIndex < 0) _list.SelectedIndex = 0;
                }
            };
            // Search as you type, but only after a short pause: a tech typing a serial should not
            // trigger a full scan per keystroke.
            _debounce.Interval = 220;
            _debounce.Tick += delegate { _debounce.Stop(); RunSearch(); };
            _q.TextChanged += delegate { _debounce.Stop(); _debounce.Start(); };
            _kind.SelectedIndexChanged += delegate { RunSearch(); };
            _list.SelectedIndexChanged += delegate { ShowSel(); };
            _list.DoubleClick += delegate { OpenSel(); };
            _open.Click += delegate { OpenSel(); };
            Disposed += delegate { _debounce.Dispose(); };

            _status.Text = kb == null ? "" : kb.Status;
        }

        public void Rebind(KbIndex kb)
        {
            _kb = kb;
            RunSearch();
        }

        public void Prefill(string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return;
            _debounce.Stop();
            _q.Text = q;
            RunSearch();
        }

        public void FocusQuery()
        {
            _q.Focus();
            _q.SelectAll();
        }

        void RunSearch()
        {
            _debounce.Stop();
            if (_kb == null) return;
            string kind = _kind.SelectedItem == null ? "ALL" : _kind.SelectedItem.ToString();
            int total;
            var hits = _kb.Search(_q.Text, kind, KbIndex.MaxResults, out total);
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (Hit h in hits) _list.Items.Add(h);
            _list.EndUpdate();
            string count = total > hits.Count
                ? total + " hits (showing best " + hits.Count + ")"
                : total + " hits";
            _status.Text = count + "  ·  " + _kb.Status;
            if (hits.Count > 0) { _list.SelectedIndex = 0; ShowSel(); }
            else
            {
                _sel = null;
                _open.Enabled = false;
                _detail.Text = _kb.All.Count == 0
                    ? "The knowledge base is empty.\r\n\r\nExpected data\\kb.json under:\r\n" + _kb.Root +
                      "\r\n\r\nPoint at it with a kb-path.txt next to TechBench.exe or the TECHBENCH_KB environment variable."
                    : "No hits.";
            }
        }

        void ShowSel()
        {
            _sel = _list.SelectedItem as Hit;
            if (_sel == null) { _detail.Text = ""; _open.Enabled = false; return; }
            string extra = "";
            bool canOpen = false;
            if (_sel.Kind == "MANUAL" || _sel.Kind == "FILE" || _sel.Kind == "NOTE")
            {
                if (string.IsNullOrEmpty(_sel.Path))
                    extra = "\r\n\r\n(No file path recorded for this document.)";
                else if (Exists(_sel.Path))
                    canOpen = true;
                else
                    extra = "\r\n\r\n(File not reachable right now — is the USB drive plugged in?)";
            }
            _detail.Text = _sel.Title + "\r\n" + _sel.Subtitle + "\r\n\r\n" + _sel.Body + extra;
            _open.Enabled = canOpen;
        }

        static bool Exists(string path)
        {
            try { return File.Exists(path); }
            catch { return false; }
        }

        void OpenSel()
        {
            if (_sel == null || string.IsNullOrEmpty(_sel.Path)) return;
            if (!Exists(_sel.Path))
            {
                MessageBox.Show(this, "Cannot reach:\n" + _sel.Path + "\n\nCheck that the USB drive or network share is connected.",
                    "Open file", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { Process.Start(_sel.Path); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Open file"); }
        }
    }
}
