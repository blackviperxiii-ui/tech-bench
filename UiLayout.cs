using System;
using System.Drawing;
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
                    if (w < 1) w = Math.Max(1, flow.Width);
                    Size pref = flow.GetPreferredSize(new Size(w, 0));
                    int bottom = flow.Padding.Top;
                    foreach (Control ch in flow.Controls)
                    {
                        if (!ch.Visible) continue;
                        int b = ch.Bottom + ch.Margin.Bottom;
                        if (b > bottom) bottom = b;
                    }
                    int h = Math.Max(pref.Height, bottom + flow.Padding.Bottom);
                    int minH = 28;
                    if (flow.MinimumSize.Height > minH) minH = flow.MinimumSize.Height;
                    if (h < minH) h = minH;
                    if (Math.Abs(flow.Height - h) > 1) flow.Height = h;
                }
                finally { fitting = false; }
            };
            flow.Layout += delegate { fit(null, EventArgs.Empty); };
            flow.SizeChanged += delegate { fit(null, EventArgs.Empty); };
            return flow;
        }

        /// <summary>
        /// Wrapping label that grows with parent width instead of painting "C:\Users\jerem\OneDriv…".
        /// </summary>
        public static Label WrapText(string text)
        {
            var l = new Label
            {
                Text = text ?? "",
                AutoSize = true,
                UseMnemonic = false
            };
            EventHandler fit = delegate
            {
                Control p = l.Parent;
                if (p == null) return;
                int w = p.ClientSize.Width - l.Margin.Horizontal;
                if (l.Dock == DockStyle.None) w -= l.Left;
                if (w < 40) w = 40;
                Size want = new Size(w, 0);
                if (l.MaximumSize.Width != w)
                    l.MaximumSize = want;
            };
            l.ParentChanged += delegate
            {
                Control p = l.Parent;
                if (p == null) return;
                p.SizeChanged += delegate { fit(null, EventArgs.Empty); };
                p.Layout += delegate { fit(null, EventArgs.Empty); };
                fit(null, EventArgs.Empty);
            };
            return l;
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
