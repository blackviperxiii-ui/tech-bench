// Reports buttons that are on the selected tab but clipped, and writes shop-laptop screenshots.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TechBench;

static class LayoutAudit
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string shotDir = args.Length > 0 ? args[0] : null;
        if (!string.IsNullOrEmpty(shotDir)) Directory.CreateDirectory(shotDir);

        var cases = new[]
        {
            new Size(1366, 768),
            new Size(1093, 614), // 1366x768 at 125% DPI, logical
            new Size(1024, 600),
            new Size(900, 560)
        };

        int clipped = 0;
        foreach (Size outer in cases)
        {
            using (var f = new ShellForm(new KbIndex()))
            {
                f.StartPosition = FormStartPosition.Manual;
                f.WindowState = FormWindowState.Normal;
                f.MinimumSize = new Size(1, 1);
                f.Location = new Point(20, 40);
                f.Show();
                // First layout pass at the default SplitContainer/TabPage size, then the
                // real shop-laptop size — the lock bug only shows up after this grow.
                f.Size = new Size(200, 180);
                Application.DoEvents();
                f.Size = outer;
                Application.DoEvents();

                Console.WriteLine();
                Console.WriteLine("== " + f.Width + "x" + f.Height + " client " + f.ClientSize.Width + "x" + f.ClientSize.Height + " ==");

                TabControl tabs = FindTab(f);
                foreach (TabPage page in tabs.TabPages)
                {
                    tabs.SelectedTab = page;
                    Application.DoEvents();
                    clipped += Report(f, page.Text);
                    clipped += SplitReport(f, page.Text);
                    if (shotDir != null)
                        Shot(f, shotDir, outer.Width + "x" + outer.Height + "-" + Safe(page.Text));

                    TabControl nested = FindTab(page);
                    if (nested == null) continue;
                    foreach (TabPage inner in nested.TabPages)
                    {
                        nested.SelectedTab = inner;
                        Application.DoEvents();
                        clipped += Report(f, page.Text + " / " + inner.Text);
                        clipped += SplitReport(f, page.Text + " / " + inner.Text);
                        if (shotDir != null)
                            Shot(f, shotDir, outer.Width + "x" + outer.Height + "-" + Safe(page.Text) + "-" + Safe(inner.Text));
                    }
                }

                using (var dlg = new CodeEditForm(new UserCode()))
                {
                    dlg.StartPosition = FormStartPosition.Manual;
                    dlg.MinimumSize = new Size(1, 1);
                    dlg.Location = new Point(40, 40);
                    dlg.Show();
                    dlg.Size = new Size(Math.Min(680, outer.Width - 40), Math.Min(620, outer.Height - 40));
                    Application.DoEvents();
                    clipped += Report(dlg, "CodeEdit " + dlg.Width + "x" + dlg.Height);
                    if (shotDir != null)
                        Shot(dlg, shotDir, outer.Width + "x" + outer.Height + "-code-edit");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine(clipped == 0 ? "No clipped primary controls." : ("Clipped hits: " + clipped));
        return clipped == 0 ? 0 : 1;
    }

    static void Shot(Form f, string dir, string name)
    {
        try
        {
            using (var bmp = new Bitmap(Math.Max(1, f.Width), Math.Max(1, f.Height)))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(Path.Combine(dir, name + ".png"));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("  shot failed " + name + ": " + ex.Message);
        }
    }

    static int Report(Form f, string where)
    {
        var hits = new List<string>();
        Walk(f, f, where, hits);
        foreach (string h in hits) Console.WriteLine("  CLIP  " + h);
        return hits.Count;
    }

    // Catches UiLayout.Split locking on the default 150×100 handle: a 430/470
    // request gets clamped to a sliver and never reapplies after the real size lands.
    static int SplitReport(Form f, string where)
    {
        int hits = 0;
        foreach (SplitContainer sc in Splits(f))
        {
            if (!OnSelectedPath(sc) || !sc.Visible) continue;
            bool vert = sc.Orientation == Orientation.Vertical;
            int span = vert ? sc.Width : sc.Height;
            int d = sc.SplitterDistance;
            if (vert && span >= 500 && d < 140)
            {
                Console.WriteLine("  CLIP  " + where + "  vertical splitter locked d=" + d + " w=" + sc.Width);
                hits++;
            }
            if (!vert && span >= 280 && d < 70)
            {
                Console.WriteLine("  CLIP  " + where + "  horizontal splitter locked d=" + d + " h=" + sc.Height);
                hits++;
            }
        }
        return hits;
    }

    static List<SplitContainer> Splits(Control c)
    {
        var list = new List<SplitContainer>();
        if (c is SplitContainer) list.Add((SplitContainer)c);
        foreach (Control child in c.Controls) list.AddRange(Splits(child));
        return list;
    }

    static void Walk(Control root, Control c, string where, List<string> hits)
    {
        if (!OnSelectedPath(c)) return;

        if ((c is Button || c is ComboBox) && ShouldBeClickable(c))
        {
            string why = ClipReason(root, c);
            if (why != null) hits.Add(where + "  " + Describe(c) + "  " + why);
        }
        if (c is FlowLayoutPanel)
        {
            var flow = (FlowLayoutPanel)c;
            if (flow.Visible && flow.WrapContents && flow.Controls.Count > 0 && OnSelectedPath(c))
            {
                int maxBottom = 0;
                foreach (Control ch in flow.Controls)
                    if (ch.Visible && ch.Bottom > maxBottom) maxBottom = ch.Bottom;
                if (maxBottom > flow.ClientSize.Height + 3)
                    hits.Add(where + "  Flow wrap clipped childrenBottom=" + maxBottom + " h=" + flow.ClientSize.Height);
                if (!flow.WrapContents)
                {
                    foreach (Control ch in flow.Controls)
                    {
                        if (ch.Visible && ch.Right > flow.ClientSize.Width + 3)
                            hits.Add(where + "  Flow nowrap overflow " + Describe(ch));
                    }
                }
            }
            if (flow.Visible && !flow.WrapContents && OnSelectedPath(c))
            {
                foreach (Control ch in flow.Controls)
                {
                    if ((ch is Button || ch is ComboBox) && ch.Right > flow.ClientSize.Width + 3)
                        hits.Add(where + "  nowrap overflow " + Describe(ch) + " parentW=" + flow.ClientSize.Width);
                }
            }
        }
        foreach (Control child in c.Controls)
            Walk(root, child, where, hits);
    }

    static bool ShouldBeClickable(Control c)
    {
        return true;
    }

    static bool OnSelectedPath(Control c)
    {
        while (c != null)
        {
            TabPage page = c as TabPage;
            if (page != null)
            {
                TabControl tabs = page.Parent as TabControl;
                if (tabs != null && tabs.SelectedTab != page) return false;
            }
            c = c.Parent;
        }
        return true;
    }

    static string ClipReason(Control root, Control c)
    {
        if (!c.Visible) return "not visible";
        if (c.Width < 8 || c.Height < 8) return "degenerate " + c.Size;
        Rectangle r = c.RectangleToScreen(new Rectangle(0, 0, c.Width, c.Height));
        Rectangle host = root.RectangleToScreen(root.ClientRectangle);
        Rectangle vis = Rectangle.Intersect(host, r);
        if (vis.Width < 16 || vis.Height < 12)
            return "off form vis=" + vis.Size + " " + Box(c) + " form=" + root.ClientSize;

        Control p = c.Parent;
        int hops = 0;
        while (p != null && hops < 8)
        {
            Rectangle local = p.RectangleToClient(r);
            if (local.Bottom > p.ClientSize.Height + 4 || local.Right > p.ClientSize.Width + 4)
            {
                if (local.Top >= p.ClientSize.Height || local.Left >= p.ClientSize.Width)
                    return "outside " + p.GetType().Name + " " + Box(c) + " parent=" + p.ClientSize;
                if (local.Bottom - p.ClientSize.Height > 10 || local.Right - p.ClientSize.Width > 10)
                    return "clipped by " + p.GetType().Name + " " + Box(c) + " parent=" + p.ClientSize;
            }
            p = p.Parent;
            hops++;
        }
        return null;
    }

    static string Describe(Control c)
    {
        string t = (c.Text ?? "").Replace("\r", " ").Replace("\n", " ");
        if (t.Length > 42) t = t.Substring(0, 42) + "…";
        return c.GetType().Name + " \"" + t + "\" " + Box(c);
    }

    static string Box(Control c)
    {
        return c.Left + "," + c.Top + " " + c.Width + "x" + c.Height;
    }

    static string Safe(string s)
    {
        foreach (char ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '-');
        return s.Replace(' ', '-');
    }

    static TabControl FindTab(Control c)
    {
        if (c is TabControl) return (TabControl)c;
        foreach (Control child in c.Controls)
        {
            TabControl t = FindTab(child);
            if (t != null) return t;
        }
        return null;
    }
}
