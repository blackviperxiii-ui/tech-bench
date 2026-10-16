using System.Collections.Generic;
using System.Drawing;

namespace TechBench
{
    /// <summary>
    /// Main-window size and position saved in settings.json. No UI: the shell passes in its screens.
    /// </summary>
    internal static class WindowPlacement
    {
        /// <summary>Saved bounds to restore, or null when they are below the minimum size or on no screen.</summary>
        public static Rectangle? SavedBounds(AppSettings s, Size minimum, IEnumerable<Rectangle> workingAreas)
        {
            if (s == null || s.Width < minimum.Width || s.Height < minimum.Height) return null;
            var bounds = new Rectangle(s.X, s.Y, s.Width, s.Height);
            if (workingAreas == null) return null;
            foreach (Rectangle area in workingAreas)
            {
                if (area.IntersectsWith(bounds)) return bounds;
            }
            return null;
        }

        /// <summary>Record the normal (restored) bounds and whether the window is maximized.</summary>
        public static void Capture(AppSettings s, bool maximized, Rectangle normalBounds)
        {
            if (s == null) return;
            s.Maximized = maximized;
            s.X = normalBounds.X;
            s.Y = normalBounds.Y;
            s.Width = normalBounds.Width;
            s.Height = normalBounds.Height;
        }
    }
}
