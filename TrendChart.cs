using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace J1939Reader
{
    /// <summary>
    /// Strip chart of the rolling trend log. Bands mark where the red or amber lamp was on, which is
    /// the whole point: you want to see what the readings did *before* the lamp came on.
    /// </summary>
    internal sealed class TrendChart : Control
    {
        const int PadLeft = 58;
        const int PadRight = 12;
        const int PadTop = 10;
        const int PadBottom = 24;

        List<TrendSample> _samples = new List<TrendSample>();
        TrendChannel _channel = TrendChannel.Rpm;

        public TrendChart()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint
                   | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
        }

        public TrendChannel Channel
        {
            get { return _channel; }
            set { _channel = value; Invalidate(); }
        }

        public void SetData(List<TrendSample> samples)
        {
            _samples = samples ?? new List<TrendSample>();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Rectangle plot = new Rectangle(
                PadLeft, PadTop,
                Math.Max(1, Width - PadLeft - PadRight),
                Math.Max(1, Height - PadTop - PadBottom));

            using (var frame = new Pen(Color.FromArgb(200, 200, 200)))
                g.DrawRectangle(frame, plot);

            if (_samples.Count < 2)
            {
                TextRenderer.DrawText(g,
                    "Connect and let it run — one sample a second.",
                    Font, plot, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            double min, max;
            if (!TrendLog.Range(_samples, _channel, out min, out max))
            {
                TextRenderer.DrawText(g,
                    TrendLog.Label(_channel) + " was never published on this hookup.",
                    Font, plot, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            if (max - min < 1e-9) { max = min + 1; min -= 1; }
            double pad = (max - min) * 0.1;
            min -= pad;
            max += pad;

            DrawLampBands(g, plot);
            DrawGrid(g, plot, min, max);
            DrawSeries(g, plot, min, max);
            DrawTimeAxis(g, plot);
        }

        void DrawLampBands(Graphics g, Rectangle plot)
        {
            using (var red = new SolidBrush(Color.FromArgb(40, 200, 40, 40)))
            using (var amber = new SolidBrush(Color.FromArgb(36, 230, 170, 30)))
            {
                for (int i = 0; i < _samples.Count; i++)
                {
                    if (!_samples[i].Red && !_samples[i].Amber) continue;
                    int x0 = X(plot, i);
                    int x1 = X(plot, Math.Min(i + 1, _samples.Count - 1));
                    int w = Math.Max(1, x1 - x0);
                    g.FillRectangle(_samples[i].Red ? red : amber, x0, plot.Top + 1, w, plot.Height - 1);
                }
            }
        }

        void DrawGrid(Graphics g, Rectangle plot, double min, double max)
        {
            using (var grid = new Pen(Color.FromArgb(232, 232, 232)))
            {
                for (int i = 1; i < 4; i++)
                {
                    int y = plot.Top + plot.Height * i / 4;
                    g.DrawLine(grid, plot.Left + 1, y, plot.Right - 1, y);
                }
            }
            for (int i = 0; i <= 4; i++)
            {
                double v = max - (max - min) * i / 4.0;
                int y = plot.Top + plot.Height * i / 4;
                string label = Math.Abs(v) >= 100 ? v.ToString("0") : v.ToString("0.0");
                TextRenderer.DrawText(g, label, Font,
                    new Rectangle(0, y - 9, PadLeft - 6, 18), Color.DimGray,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
            TextRenderer.DrawText(g, TrendLog.Label(_channel), Font,
                new Rectangle(plot.Left + 4, plot.Top + 2, plot.Width - 8, 18), Color.Gray,
                TextFormatFlags.Left);
        }

        void DrawSeries(Graphics g, Rectangle plot, double min, double max)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.FromArgb(20, 90, 170), 1.6f))
            {
                Point? prev = null;
                for (int i = 0; i < _samples.Count; i++)
                {
                    double v = TrendLog.Value(_samples[i], _channel);
                    if (!TrendSample.Has(v)) { prev = null; continue; }
                    int x = X(plot, i);
                    int y = plot.Bottom - (int)Math.Round((v - min) / (max - min) * plot.Height);
                    y = Math.Max(plot.Top, Math.Min(plot.Bottom, y));
                    var pt = new Point(x, y);
                    // A gap means the ECM stopped publishing; joining across it would invent data.
                    if (prev.HasValue) g.DrawLine(pen, prev.Value, pt);
                    else g.FillRectangle(pen.Brush, x, y - 1, 2, 2);
                    prev = pt;
                }
            }
            g.SmoothingMode = SmoothingMode.Default;
        }

        void DrawTimeAxis(Graphics g, Rectangle plot)
        {
            DateTime t0 = _samples[0].Time.ToLocalTime();
            DateTime t1 = _samples[_samples.Count - 1].Time.ToLocalTime();
            TextRenderer.DrawText(g, t0.ToString("HH:mm:ss"), Font,
                new Rectangle(plot.Left, plot.Bottom + 3, 120, 18), Color.DimGray, TextFormatFlags.Left);
            TextRenderer.DrawText(g, t1.ToString("HH:mm:ss"), Font,
                new Rectangle(plot.Right - 120, plot.Bottom + 3, 120, 18), Color.DimGray, TextFormatFlags.Right);
            string span = ((int)Math.Round((t1 - t0).TotalSeconds)) + "s  ·  " + _samples.Count + " samples";
            TextRenderer.DrawText(g, span, Font,
                new Rectangle(plot.Left, plot.Bottom + 3, plot.Width, 18), Color.Gray,
                TextFormatFlags.HorizontalCenter);
        }

        int X(Rectangle plot, int i)
        {
            if (_samples.Count <= 1) return plot.Left;
            return plot.Left + (int)Math.Round((double)i / (_samples.Count - 1) * (plot.Width - 1));
        }
    }
}
