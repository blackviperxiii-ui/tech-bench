using System;
using System.Drawing;
using System.Windows.Forms;

namespace TechBench
{
    /// <summary>
    /// Shared WinForms layout helpers so shop-laptop sizes (and DPI-scaled working areas)
    /// keep primary buttons on screen instead of clipped under a docked strip.
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
            EventHandler fit = delegate
            {
                int w = flow.ClientSize.Width;
                if (w < 1) w = Math.Max(1, flow.Width);
                Size pref = flow.GetPreferredSize(new Size(w, 0));
                int h = pref.Height;
                if (h < 28) h = 28;
                if (flow.Height != h) flow.Height = h;
            };
            flow.Layout += delegate { fit(null, EventArgs.Empty); };
            flow.SizeChanged += delegate { fit(null, EventArgs.Empty); };
            return flow;
        }

        /// <summary>
        /// Large wrapping buttons for Search / Work orders / INLINE 7 / Adapters.
        /// Native TabControl headers vanish under a fixed job strip at 192 DPI and do not look like switches.
        /// </summary>
        public static FlowLayoutPanel SwitchBar(string[] names, Action<int> onPick)
        {
            var bar = WrapBar(new Padding(8, 6, 8, 4));
            bar.BackColor = Color.FromArgb(36, 48, 68);
            if (names == null) return bar;
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var b = new Button
                {
                    Name = "shellSwitch" + i,
                    Text = names[i],
                    AutoSize = true,
                    MinimumSize = new Size(128, 36),
                    Margin = new Padding(0, 0, 8, 4),
                    Tag = idx,
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

    /// <summary>
    /// Tab pages without the native header row. The shell switch bar is the only primary nav;
    /// SysTabControl headers were a thin gray label strip that shop techs could not click.
    /// </summary>
    internal sealed class HiddenHeaderTabControl : TabControl
    {
        const int TcmAdjustRect = 0x1328;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == TcmAdjustRect && !DesignMode)
            {
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }
    }
}
