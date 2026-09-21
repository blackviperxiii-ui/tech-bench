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
            new Size(1381, 877),
            new Size(1093, 614), // 1366x768 at 125% DPI, logical
            new Size(1024, 600),
            new Size(900, 560),
            new Size(960, 540) // 192 DPI (200%) shop laptop
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

                    clipped += SwitchReport(f);
                    if (FindTab(f) != null)
                    {
                        Console.WriteLine("  CLIP  shell  still has a TabControl (use wrapping switches)");
                        clipped++;
                    }

                    string[] names = { "Search", "Work orders", "INLINE 7", "Adapters" };
                    string[] shotKey = { "layout-search", "layout-workorders", "layout-inline7", "layout-adapters" };
                    for (int i = 0; i < names.Length; i++)
                    {
                    Button sw = FindNamed(f, "shellSwitch" + i) as Button;
                    if (sw != null) sw.PerformClick();
                    Application.DoEvents();
                    clipped += Report(f, names[i]);
                    clipped += SplitReport(f, names[i]);
                    if (shotDir != null)
                    {
                        Shot(f, shotDir, outer.Width + "x" + outer.Height + "-" + Safe(names[i]));
                        if (outer.Width == 1366 && i < shotKey.Length)
                            Shot(f, shotDir, shotKey[i]);
                    }

                    if (i == 2)
                    {
                        string[] inner = { "Codes", "Advanced", "Bus", "Trend", "Timeline", "History", "Guidanz" };
                        for (int k = 0; k < inner.Length; k++)
                        {
                            Button inn = FindNamed(f, "inlineSwitch" + k) as Button;
                            if (inn == null)
                            {
                                Console.WriteLine("  CLIP  INLINE 7  missing inner switch \"" + inner[k] + "\"");
                                clipped++;
                                continue;
                            }
                            inn.PerformClick();
                            Application.DoEvents();
                            clipped += Report(f, "INLINE 7 / " + inner[k]);
                            clipped += SplitReport(f, "INLINE 7 / " + inner[k]);
                            if (shotDir != null)
                                Shot(f, shotDir, outer.Width + "x" + outer.Height + "-INLINE-7-" + Safe(inner[k]));
                        }
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

    static int SwitchReport(Form f)
    {
        string[] need = { "Search", "Work orders", "INLINE 7", "Adapters" };
        int hits = 0;
        for (int i = 0; i < need.Length; i++)
        {
            Button b = FindNamed(f, "shellSwitch" + i) as Button;
            if (b == null)
            {
                Console.WriteLine("  CLIP  shell  missing switch \"" + need[i] + "\"");
                hits++;
                continue;
            }
            if (!string.Equals(b.Text, need[i], StringComparison.Ordinal))
            {
                Console.WriteLine("  CLIP  shell  switch " + i + " text=\"" + b.Text + "\" want=\"" + need[i] + "\"");
                hits++;
            }
            string why = ClipReason(f, b);
            if (why != null)
            {
                Console.WriteLine("  CLIP  shell  switch \"" + need[i] + "\"  " + why);
                hits++;
            }
            else if (b.Height < 40 || b.Width < 80)
            {
                Console.WriteLine("  CLIP  shell  switch \"" + need[i] + "\"  tiny " + b.Size);
                hits++;
            }
            else
                Console.WriteLine("  OK    shell  switch \"" + need[i] + "\"  " + b.Width + "x" + b.Height + " @ " + b.Left + "," + b.Top);
        }
        return hits;
    }

    static Control FindNamed(Control c, string name)
    {
        if (c.Name == name) return c;
        foreach (Control child in c.Controls)
        {
            Control hit = FindNamed(child, name);
            if (hit != null) return hit;
        }
        return null;
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
        string cut = CaptionCut(c);
        if (cut != null) hits.Add(where + "  " + cut);
        Label lab = c as Label;
        if (lab != null && lab.Visible && !lab.AutoSize && lab.Height > 0 && lab.Height <= 26
            && !string.IsNullOrEmpty(lab.Text) && lab.Text.Length > 18)
        {
            Size need = TextRenderer.MeasureText(lab.Text, lab.Font,
                new Size(Math.Max(40, lab.ClientSize.Width), 0),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            if (need.Height > lab.Height + 3)
                hits.Add(where + "  unread Label h=" + lab.Height + " need=" + need.Height + "  " + Describe(lab));
        }
        TextBox tb = c as TextBox;
        if (tb != null && tb.Visible && tb.ReadOnly && tb.Multiline && tb.BorderStyle == BorderStyle.None
            && !string.IsNullOrEmpty(tb.Text) && tb.Height > 0)
        {
            string one = tb.Text.Replace("\r", " ").Replace("\n", " ");
            if (one.Length > 24)
            {
                Size line = TextRenderer.MeasureText(one, tb.Font, new Size(int.MaxValue, 0),
                    TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);
                bool multi = tb.Text.IndexOf('\n') >= 0 || tb.Text.IndexOf('\r') >= 0;
                if (!multi && line.Width > tb.ClientSize.Width + 12 && tb.Height <= tb.Font.Height + 12)
                    hits.Add(where + "  unread WrapText clipped  " + Describe(tb));
            }
            if (one.IndexOf('…') >= 0 || one.IndexOf("...") >= 0)
                hits.Add(where + "  unread ellipsis  " + Describe(tb));
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
            if (c.Name != null && c.Name.StartsWith("shellPage") && !c.Visible) return false;
            if (c.Name != null && c.Name.StartsWith("inlinePage") && !c.Visible) return false;
            c = c.Parent;
        }
        return true;
    }

    static bool HasAutoScrollAncestor(Control c)
    {
        Control p = c.Parent;
        int hops = 0;
        while (p != null && hops < 12)
        {
            ScrollableControl s = p as ScrollableControl;
            if (s != null && s.AutoScroll) return true;
            p = p.Parent;
            hops++;
        }
        return false;
    }

    // Full caption must fit. AutoSize does not skip this. An empty form intersection still fails in ClipReason.
    static string CaptionCut(Control c)
    {
        if (!c.Visible) return null;
        if (!(c is Button) && !(c is Label) && !(c is CheckBox)) return null;
        string text = c.Text ?? "";
        if (text.Length == 0) return null;
        if (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0) return null;
        Size need = TextRenderer.MeasureText(text, c.Font, new Size(int.MaxValue, 0),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        int avail = c.ClientSize.Width;
        if (c is CheckBox) avail -= 18;
        if (need.Width > avail + 8)
            return "caption cut \"" + (text.Length > 42 ? text.Substring(0, 42) + "…" : text)
                + "\" need=" + need.Width + " clientW=" + c.ClientSize.Width + " " + Box(c);
        return null;
    }

    static string ClipReason(Control root, Control c)
    {
        if (!c.Visible) return "not visible";
        if (c.Width < 8 || c.Height < 8) return "degenerate " + c.Size;
        Rectangle r = c.RectangleToScreen(new Rectangle(0, 0, c.Width, c.Height));
        Rectangle host = root.RectangleToScreen(root.ClientRectangle);
        Rectangle vis = Rectangle.Intersect(host, r);
        if (vis.Width < 16 || vis.Height < 12)
        {
            if (vis.Width > 0 && vis.Height > 0 && HasAutoScrollAncestor(c)) return null;
            return "off form vis=" + vis.Size + " " + Box(c) + " form=" + root.ClientSize;
        }

        Control p = c.Parent;
        int hops = 0;
        while (p != null && hops < 8)
        {
            if (p is ScrollableControl && ((ScrollableControl)p).AutoScroll)
            {
                p = p.Parent;
                hops++;
                continue;
            }
            Rectangle local = p.RectangleToClient(r);
            if (local.Bottom > p.ClientSize.Height + 4 || local.Right > p.ClientSize.Width + 4)
            {
                if (HasAutoScrollAncestor(c))
                {
                    p = p.Parent;
                    hops++;
                    continue;
                }
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
