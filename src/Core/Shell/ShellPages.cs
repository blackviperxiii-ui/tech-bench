namespace TechBench
{
    /// <summary>
    /// Shell switch-bar pages. The int value is what adapter-prefs.json stores as ShellPage,
    /// so never renumber or reorder: add new pages at the end.
    /// </summary>
    internal enum ShellPageId
    {
        Search = 0,
        WorkOrders = 1,
        Inline7 = 2,
        Adapters = 3
    }

    internal sealed class ShellPageInfo
    {
        public readonly ShellPageId Id;
        public readonly string Title;
        public readonly string PanelName;

        public ShellPageInfo(ShellPageId id, string title)
        {
            Id = id;
            Title = title;
            PanelName = "shellPage" + (int)id;
        }
    }

    /// <summary>
    /// Rearch stage 7: the one list of shell pages. Switch-bar order, titles, panel names and
    /// the saved-page clamp all read from here instead of parallel arrays and a page count.
    /// </summary>
    internal static class ShellPages
    {
        static readonly ShellPageInfo[] All =
        {
            new ShellPageInfo(ShellPageId.Search, "Search"),
            new ShellPageInfo(ShellPageId.WorkOrders, "Work orders"),
            new ShellPageInfo(ShellPageId.Inline7, "INLINE 7"),
            new ShellPageInfo(ShellPageId.Adapters, "Adapters")
        };

        public static int Count
        {
            get { return All.Length; }
        }

        public static ShellPageInfo At(int index)
        {
            return All[Clamp(index)];
        }

        public static string[] Titles()
        {
            var titles = new string[All.Length];
            for (int i = 0; i < All.Length; i++) titles[i] = All[i].Title;
            return titles;
        }

        /// <summary>Out-of-range saved pages fall back to the nearest real page.</summary>
        public static int Clamp(int index)
        {
            if (index < 0) return 0;
            if (index >= All.Length) return All.Length - 1;
            return index;
        }
    }
}
