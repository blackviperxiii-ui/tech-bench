using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TechBench
{
    internal sealed class SearchControl : UserControl
    {
        readonly KbIndex _kb;
        readonly TextBox _q;
        readonly ComboBox _kind;
        readonly ListBox _list;
        readonly TextBox _detail;
        readonly Label _status;
        readonly Button _open;
        Hit _sel;

        public SearchControl(KbIndex kb)
        {
            _kb = kb;
            Dock = DockStyle.Fill;
            Font = new Font("Segoe UI", 9.5f);

            _q = new TextBox { Left = 8, Top = 8, Width = 520, Height = 26, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _kind = new ComboBox { Left = 536, Top = 8, Width = 140, DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            foreach (string k in new[] { "ALL", "CODE", "PASSWORD", "MANUAL", "FILTER", "EQUIP" })
                _kind.Items.Add(k);
            _kind.SelectedIndex = 0;
            var go = new Button { Left = 684, Top = 7, Width = 80, Height = 28, Text = "Search", Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _open = new Button { Left = 770, Top = 7, Width = 100, Height = 28, Text = "Open PDF", Enabled = false, Anchor = AnchorStyles.Top | AnchorStyles.Right };

            _status = new Label { Left = 8, Top = 40, Width = 860, Height = 18, ForeColor = Color.DimGray, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _list = new ListBox { Left = 8, Top = 62, Width = 420, Height = 480, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left };
            _detail = new TextBox
            {
                Left = 436, Top = 62, Width = 434, Height = 480,
                Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true,
                Font = new Font("Consolas", 9.5f),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            go.Click += (s, e) => RunSearch();
            _q.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RunSearch(); } };
            _kind.SelectedIndexChanged += (s, e) => RunSearch();
            _list.SelectedIndexChanged += (s, e) => ShowSel();
            _list.DoubleClick += (s, e) => OpenSel();
            _open.Click += (s, e) => OpenSel();

            Controls.AddRange(new Control[] { _q, _kind, go, _open, _status, _list, _detail });
            _status.Text = kb.Status;
            Resize += (s, e) =>
            {
                _q.Width = Math.Max(200, Width - 400);
                _kind.Left = _q.Right + 8;
                go.Left = _kind.Right + 8;
                _open.Left = go.Right + 8;
                _list.Height = Math.Max(100, Height - 70);
                _list.Width = Math.Max(200, (Width - 24) / 2);
                _detail.Left = _list.Right + 8;
                _detail.Width = Math.Max(100, Width - _detail.Left - 8);
                _detail.Height = _list.Height;
            };
        }

        public void Prefill(string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return;
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
            if (_sel.Kind == "MANUAL")
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
                    "Open PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { Process.Start(_sel.Path); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Open PDF"); }
        }
    }
}
