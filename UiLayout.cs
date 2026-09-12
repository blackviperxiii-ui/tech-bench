using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace TechBench
{
    /// <summary>
    /// Shared WinForms layout helpers so shop-laptop sizes (and DPI-scaled working areas)
    /// keep primary buttons and wrapped text on screen instead of a fixed TabControl chrome.
    /// </summary>
    internal static class UiLayout
    {
        public static void FitToWorkingArea(Form f)
        {
            if (f == null || f.WindowState == FormWindowState.Maximized) return;
            Rectangle wa = Screen.FromControl(f).WorkingArea;
            if (wa.Width < 80 || wa.Height < 80) return;

            Size min = f.MinimumSize;
            int minW = min.Width;
            int minH = min.Height;
            if (minW > wa.Width) minW = wa.Width;
            if (minH > wa.Height) minH = wa.Height;
            if (minW != min.Width || minH != min.Height)
                f.MinimumSize = new Size(minW, minH);

            int w = f.Width;
            int h = f.Height;
            if (w > wa.Width) w = wa.Width;
            if (h > wa.Height) h = wa.Height;
            if (w < minW) w = minW;
            if (h < minH) h = minH;
            if (w != f.Width || h != f.Height) f.Size = new Size(w, h);

            int x = f.Left;
            int y = f.Top;
            if (x < wa.Left) x = wa.Left;
            if (y < wa.Top) y = wa.Top;
            if (x + f.Width > wa.Right) x = wa.Right - f.Width;
            if (y + f.Height > wa.Bottom) y = wa.Bottom - f.Height;
            if (x < wa.Left) x = wa.Left;
            if (y < wa.Top) y = wa.Top;
            if (x != f.Left || y != f.Top) f.Location = new Point(x, y);
        }

        public static Size SizeForScreen(int wantW, int wantH, int minW, int minH)
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int w = wantW;
            int h = wantH;
            if (wa.Width > 80 && w > wa.Width - 24) w = wa.Width - 24;
            if (wa.Height > 80 && h > wa.Height - 24) h = wa.Height - 24;
            if (w < minW) w = Math.Min(minW, wa.Width);
            if (h < minH) h = Math.Min(minH, wa.Height);
            return new Size(w, h);
        }

        public static FlowLayoutPanel WrapBar(Padding padding)
        {
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                WrapContents = true,
                Padding = padding
            };
            bool fitting = false;
            EventHandler fit = delegate
            {
                if (fitting) return;
                fitting = true;
                try
                {
                    int w = flow.ClientSize.Width;
                    if (flow.Parent != null)
                    {
                        int pw = flow.Parent.ClientSize.Width;
                        if (flow.Dock == DockStyle.Top && pw > 1) w = pw;
                    }
                    if (w < 1) w = Math.Max(1, flow.Width);
                    int h = FlowWrapHeight(flow, w);
                    int minH = 28;
                    if (flow.MinimumSize.Height > minH) minH = flow.MinimumSize.Height;
                    if (h < minH) h = minH;
                    if (Math.Abs(flow.Height - h) > 1) flow.Height = h;
                }
                finally { fitting = false; }
            };
            flow.Layout += delegate { fit(null, EventArgs.Empty); };
            flow.SizeChanged += delegate { fit(null, EventArgs.Empty); };
            flow.ControlAdded += delegate { fit(null, EventArgs.Empty); };
            flow.ParentChanged += delegate
            {
                Control p = flow.Parent;
                if (p == null) return;
                p.SizeChanged += delegate { fit(null, EventArgs.Empty); };
                p.Layout += delegate { fit(null, EventArgs.Empty); };
            };
            return flow;
        }

        /// <summary>
        /// Height that actually fits wrapped children. GetPreferredSize + child.Bottom
        /// stays one row when maximize layouts before the bar's client width lands.
        /// </summary>
        static int FlowWrapHeight(FlowLayoutPanel flow, int width)
        {
            int inner = width - flow.Padding.Horizontal;
            if (inner < 40) inner = 40;
            int x = 0;
            int y = flow.Padding.Top;
            int rowH = 0;
            foreach (Control ch in flow.Controls)
            {
                if (!ch.Visible) continue;
                Size ps = ch.PreferredSize;
                int cw = Math.Max(ch.Width, ps.Width) + ch.Margin.Horizontal;
                int chh = Math.Max(ch.Height, ps.Height) + ch.Margin.Vertical;
                if (cw < 8) cw = 80;
                if (chh < 8) chh = 24;
                if (x > 0 && x + cw > inner)
                {
                    y += rowH;
                    x = 0;
                    rowH = 0;
                }
                x += cw;
                if (chh > rowH) rowH = chh;
            }
            int h = y + rowH + flow.Padding.Bottom;
            if (h < 28) h = 28;
            return h;
        }

        /// <summary>
        /// Wrapping read-only box. WinForms WordWrap only breaks on spaces, so a KB path
        /// like C:\Users\jerem\OneDrive\… still paints "OneDriv…" unless we insert breaks.
        /// </summary>
        public static TextBox WrapText(string text)
        {
            var t = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                TabStop = false,
                WordWrap = true,
                ScrollBars = ScrollBars.None,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
            };
            string raw = text ?? "";
            bool fitting = false;
            EventHandler fit = delegate
            {
                if (fitting) return;
                fitting = true;
                try
                {
                    Control p = t.Parent;
                    int w = t.ClientSize.Width;
                    if (p != null)
                    {
                        int avail = p.ClientSize.Width - t.Left - t.Margin.Right - t.Padding.Horizontal;
                        if (t.Dock != DockStyle.None)
                            avail = p.ClientSize.Width - t.Margin.Horizontal - t.Padding.Horizontal;
                        if (avail > 24) w = avail;
                    }
                    if (w < 32) w = 32;
                    if (t.Dock == DockStyle.None && p is TableLayoutPanel)
                    {
                        int wantW = p.ClientSize.Width - t.Left - t.Margin.Right;
                        if (wantW > 32 && Math.Abs(t.Width - wantW) > 2)
                            t.Width = wantW;
                        w = Math.Max(32, t.ClientSize.Width);
                    }
                    string wrapped = BreakLong(raw, t.Font, Math.Max(24, w - 6));
                    if (t.Text != wrapped) t.Text = wrapped;
                    Size need = TextRenderer.MeasureText(
                        string.IsNullOrEmpty(wrapped) ? " " : wrapped,
                        t.Font,
                        new Size(w, int.MaxValue),
                        TextFormatFlags.TextBoxControl | TextFormatFlags.WordBreak);
                    int h = need.Height + t.Padding.Vertical + 6;
                    int min = t.Font.Height + t.Padding.Vertical + 6;
                    if (h < min) h = min;
                    if (t.Dock == DockStyle.Fill && !(p is TableLayoutPanel)) return;
                    if (t.Height != h) t.Height = h;
                }
                finally { fitting = false; }
            };
            t.ParentChanged += delegate
            {
                Control p = t.Parent;
                if (p == null) return;
                t.BackColor = p.BackColor;
                p.SizeChanged += delegate { fit(null, EventArgs.Empty); };
                p.Layout += delegate { fit(null, EventArgs.Empty); };
                fit(null, EventArgs.Empty);
            };
            t.TextChanged += delegate
            {
                if (fitting) return;
                raw = t.Text ?? "";
                fit(null, EventArgs.Empty);
            };
            t.SizeChanged += delegate { fit(null, EventArgs.Empty); };
            t.Text = raw;
            return t;
        }

        static string BreakLong(string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text) || width < 24) return text ?? "";
            var sb = new StringBuilder();
            string[] paras = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int p = 0; p < paras.Length; p++)
            {
                if (p > 0) sb.Append("\r\n");
                AppendWrapped(sb, paras[p], font, width);
            }
            return sb.ToString();
        }

        static int TextW(string s, Font font)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return TextRenderer.MeasureText(s, font, new Size(int.MaxValue, 0),
                TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding).Width;
        }

        static void AppendWrapped(StringBuilder sb, string line, Font font, int width)
        {
            if (TextW(line, font) <= width)
            {
                sb.Append(line);
                return;
            }
            int i = 0;
            while (i < line.Length)
            {
                int lo = 1, hi = line.Length - i, fitLen = 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    if (TextW(line.Substring(i, mid), font) <= width)
                    {
                        fitLen = mid;
                        lo = mid + 1;
                    }
                    else hi = mid - 1;
                }
                int take = fitLen;
                if (i + take < line.Length)
                {
                    int br = -1;
                    int floor = Math.Max(1, take / 3);
                    for (int k = take; k >= floor; k--)
                    {
                        char ch = line[i + k - 1];
                        if (ch == '\\' || ch == '/' || ch == ' ' || ch == '-' || ch == '_' || ch == '.' || ch == '·')
                        {
                            br = k;
                            break;
                        }
                    }
                    if (br > 0) take = br;
                }
                if (take < 1) take = 1;
                sb.Append(line, i, take);
                i += take;
                if (i < line.Length) sb.Append("\r\n");
            }
        }

        /// <summary>
        /// Large wrapping buttons for Search / Work orders / INLINE 7 / Adapters (and inner INLINE pages).
        /// Native TabControl headers vanish under a job strip at 192 DPI and do not look like switches.
        /// </summary>
        public static FlowLayoutPanel SwitchBar(string[] names, Action<int> onPick)
        {
            return SwitchBar(names, onPick, "shellSwitch", new Size(148, 40), 11.5f);
        }

        public static FlowLayoutPanel SwitchBar(string[] names, Action<int> onPick, string namePrefix, Size minButton, float fontPt)
        {
            var bar = WrapBar(new Padding(8, 6, 8, 6));
            bar.BackColor = Color.FromArgb(36, 48, 68);
            if (names == null) return bar;
            if (string.IsNullOrEmpty(namePrefix)) namePrefix = "shellSwitch";
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var b = new Button
                {
                    Name = namePrefix + i,
                    Text = names[i],
                    AutoSize = true,
                    MinimumSize = minButton.Width > 0 ? minButton : new Size(100, 32),
                    Margin = new Padding(0, 0, 8, 4),
                    Padding = new Padding(10, 6, 10, 6),
                    Tag = idx,
                    Font = new Font("Segoe UI", fontPt, FontStyle.Bold),
                    UseVisualStyleBackColor = false
                };
                b.Click += delegate { if (onPick != null) onPick(idx); };
                bar.Controls.Add(b);
            }
            return bar;
        }

        public static void MarkSwitch(FlowLayoutPanel bar, int selected, Font normal, Font bold)
        {
            if (bar == null) return;
            foreach (Control c in bar.Controls)
            {
                Button b = c as Button;
                if (b == null) continue;
                int idx = (b.Tag is int) ? (int)b.Tag : -1;
                bool on = idx == selected;
                b.Font = on && bold != null ? bold : (normal ?? b.Font);
                b.BackColor = on ? Color.White : Color.FromArgb(70, 90, 120);
                b.ForeColor = on ? Color.Black : Color.White;
            }
        }

        /// <summary>
        /// Dialog that wraps long paths instead of a MessageBox that paints "…OneDriv".
        /// </summary>
        public static void ShowReadable(IWin32Window owner, string title, string body)
        {
            using (var f = new Form())
            {
                f.Text = title ?? "";
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false;
                f.MaximizeBox = true;
                f.ShowInTaskbar = false;
                f.AutoScaleMode = AutoScaleMode.Font;
                f.Font = new Font("Segoe UI", 9.5f);
                f.MinimumSize = new Size(420, 240);
                f.Size = SizeForScreen(640, 420, 480, 300);
                var ok = new Button
                {
                    Text = "OK",
                    DialogResult = DialogResult.OK,
                    AutoSize = true,
                    Margin = new Padding(0, 4, 0, 0)
                };
                var bar = WrapBar(new Padding(12, 4, 12, 12));
                bar.Dock = DockStyle.Bottom;
                bar.FlowDirection = FlowDirection.RightToLeft;
                bar.Controls.Add(ok);
                var tb = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    Text = body ?? "",
                    BorderStyle = BorderStyle.None
                };
                f.Controls.Add(tb);
                f.Controls.Add(bar);
                f.AcceptButton = ok;
                f.Shown += delegate { FitToWorkingArea(f); };
                f.ShowDialog(owner);
            }
        }

        public static SplitContainer Split(Orientation orientation, int distance, int min1, int min2, Control a, Control b)
        {
            var sc = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = orientation,
                SplitterWidth = 6
            };
            sc.Panel1.Controls.Add(a);
            sc.Panel2.Controls.Add(b);
            a.Dock = DockStyle.Fill;
            b.Dock = DockStyle.Fill;
            bool placed = false;
            EventHandler apply = delegate
            {
                int span = orientation == Orientation.Horizontal ? sc.Height : sc.Width;
                int usable = span - sc.SplitterWidth;
                if (usable < 80) return;
                int m1 = min1;
                int m2 = min2;
                if (m1 + m2 + 10 > usable)
                {
                    m1 = Math.Max(40, usable / 3);
                    m2 = Math.Max(40, usable - m1 - 10);
                }
                try
                {
                    sc.Panel1MinSize = m1;
                    sc.Panel2MinSize = m2;
                    if (placed) return;
                    int d = distance;
                    if (d < m1) d = m1;
                    if (d > usable - m2) d = usable - m2;
                    if (d < 1) d = 1;
                    sc.SplitterDistance = d;
                    // Default 150×100 SplitContainer/TabPage bounds clamp 430-class
                    // distances; lock only once the requested distance actually fits.
                    if (distance >= m1 && distance <= usable - m2)
                        placed = true;
                }
                catch { }
            };
            sc.HandleCreated += delegate { apply(null, EventArgs.Empty); };
            sc.SizeChanged += delegate { apply(null, EventArgs.Empty); };
            return sc;
        }
    }
}
