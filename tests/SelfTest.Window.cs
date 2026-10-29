using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using TechBench;

static partial class SelfTest
{
    [SelfTestCase("window placement", Order = 97)]
    static void WindowPlacementTests()
    {
        var min = new Size(760, 520);
        var laptop = new List<Rectangle> { new Rectangle(0, 0, 1366, 728) };
        var twoScreens = new List<Rectangle> { new Rectangle(0, 0, 1920, 1040), new Rectangle(1920, 0, 1920, 1040) };

        var saved = new AppSettings { X = 100, Y = 50, Width = 1180, Height = 720 };
        Eq("saved bounds on screen are restored", WindowPlacement.SavedBounds(saved, min, laptop).ToString(),
            new Rectangle(100, 50, 1180, 720).ToString());

        var right = new AppSettings { X = 2000, Y = 10, Width = 900, Height = 600 };
        Eq("bounds on the second screen are restored", WindowPlacement.SavedBounds(right, min, twoScreens).ToString(),
            "{X=2000,Y=10,Width=900,Height=600}");
        Eq("bounds off every screen are dropped", WindowPlacement.SavedBounds(right, min, laptop).HasValue, false);

        Eq("too narrow is dropped", WindowPlacement.SavedBounds(new AppSettings { Width = 759, Height = 600 }, min, laptop).HasValue, false);
        Eq("too short is dropped", WindowPlacement.SavedBounds(new AppSettings { Width = 800, Height = 519 }, min, laptop).HasValue, false);
        Eq("exactly minimum is kept", WindowPlacement.SavedBounds(new AppSettings { Width = 760, Height = 520 }, min, laptop).HasValue, true);
        Eq("first run (zero size) is dropped", WindowPlacement.SavedBounds(new AppSettings(), min, laptop).HasValue, false);
        Eq("null settings", WindowPlacement.SavedBounds(null, min, laptop).HasValue, false);
        Eq("no screens", WindowPlacement.SavedBounds(saved, min, null).HasValue, false);

        var cap = new AppSettings();
        WindowPlacement.Capture(cap, true, new Rectangle(10, 20, 1000, 700));
        Eq("capture maximized", cap.Maximized, true);
        Eq("capture bounds", cap.X + "," + cap.Y + "," + cap.Width + "," + cap.Height, "10,20,1000,700");
        WindowPlacement.Capture(cap, false, new Rectangle(-5, 0, 800, 600));
        Eq("capture normal", cap.Maximized, false);
        Eq("capture negative x", cap.X, -5);

        string shell = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Shell", "ShellForm.cs"));
        Check("shell uses WindowPlacement",
            shell.IndexOf("WindowPlacement.SavedBounds(", StringComparison.Ordinal) >= 0
            && shell.IndexOf("WindowPlacement.Capture(", StringComparison.Ordinal) >= 0
            && shell.IndexOf("OnAnyScreen", StringComparison.Ordinal) < 0, "ShellForm");
    }
}
