namespace TechBench
{
    /// <summary>
    /// Text the shell shows around an update check: the Help menu item and the dialogs. No UI, no network.
    /// The update rules themselves (feed, hashes, when a swap may run) stay in Updater.
    /// </summary>
    internal static class UpdatePrompt
    {
        public const string PendingMenuText = "&Install pending update";
        public const string Title = "Updates";

        public static string InstallMenuText(string version)
        {
            return "&Install update " + version;
        }

        /// <summary>The check result, with the feed source appended unless it already names it.</summary>
        public static string StatusWithFeed(string status, string feed)
        {
            string s = status ?? "";
            if (!string.IsNullOrEmpty(feed) && s.IndexOf(feed, System.StringComparison.Ordinal) < 0)
                s = s + "\n\n" + feed;
            return s;
        }

        public static string AvailableQuestion(string version, string current, string notes)
        {
            string extra = string.IsNullOrEmpty(notes) ? "" : ("\n\n" + notes);
            return "Version " + version + " is available (you have " + current + ")."
                + extra + "\n\nDownload and install after this window closes?";
        }
    }
}
