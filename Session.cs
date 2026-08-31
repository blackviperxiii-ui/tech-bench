using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace J1939Reader
{
    /// <summary>Screen capture — the only part of SessionIo that needs a desktop.</summary>
    internal static partial class SessionIo
    {
        /// <summary>
        /// CopyFromScreen rather than DrawToBitmap: DrawToBitmap only renders the active tab page and
        /// leaves some controls black, which is useless as a shop record.
        /// </summary>
        public static string Screenshot(Control c)
        {
            if (c == null) return null;
            string path = Path.Combine(Folder(), "shot_" + Stamp() + ".png");
            int w = Math.Max(1, c.Width);
            int h = Math.Max(1, c.Height);
            using (var bmp = new Bitmap(w, h))
            {
                bool copied = false;
                try
                {
                    Point origin = c.PointToScreen(Point.Empty);
                    using (var g = Graphics.FromImage(bmp))
                        g.CopyFromScreen(origin, Point.Empty, new Size(w, h));
                    copied = true;
                }
                catch { }
                if (!copied) c.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
                bmp.Save(path, ImageFormat.Png);
            }
            return path;
        }
    }
}
