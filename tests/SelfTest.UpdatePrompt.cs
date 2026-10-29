using System;
using System.IO;
using TechBench;

static partial class SelfTest
{
    [SelfTestCase("update prompt text", Order = 100)]
    static void UpdatePromptTests()
    {
        Eq("install menu names the version", UpdatePrompt.InstallMenuText("1.2.40"), "&Install update 1.2.40");
        Eq("pending menu text", UpdatePrompt.PendingMenuText, "&Install pending update");
        Eq("dialog title", UpdatePrompt.Title, "Updates");

        Eq("feed appended", UpdatePrompt.StatusWithFeed("Tech Bench 1.2.36 is current.", "Feed: built-in URL"),
            "Tech Bench 1.2.36 is current.\n\nFeed: built-in URL");
        Eq("feed already named is not repeated",
            UpdatePrompt.StatusWithFeed("Update check failed. Feed: built-in URL", "Feed: built-in URL"),
            "Update check failed. Feed: built-in URL");
        Eq("no feed leaves status alone", UpdatePrompt.StatusWithFeed("Tech Bench is current.", ""), "Tech Bench is current.");
        Eq("null feed leaves status alone", UpdatePrompt.StatusWithFeed("ok", null), "ok");

        Eq("available question with notes", UpdatePrompt.AvailableQuestion("1.2.40", "1.2.36", "Fixes the clock."),
            "Version 1.2.40 is available (you have 1.2.36).\n\nFixes the clock.\n\nDownload and install after this window closes?");
        Eq("available question without notes", UpdatePrompt.AvailableQuestion("1.2.40", "1.2.36", ""),
            "Version 1.2.40 is available (you have 1.2.36).\n\nDownload and install after this window closes?");

        string shell = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Shell", "ShellForm.cs"));
        Check("shell uses UpdatePrompt",
            shell.IndexOf("UpdatePrompt.AvailableQuestion(", StringComparison.Ordinal) >= 0
            && shell.IndexOf("UpdatePrompt.StatusWithFeed(", StringComparison.Ordinal) >= 0
            && shell.IndexOf("UpdatePrompt.InstallMenuText(", StringComparison.Ordinal) >= 0
            && shell.IndexOf("Download and install after", StringComparison.Ordinal) < 0, "ShellForm");
    }
}
