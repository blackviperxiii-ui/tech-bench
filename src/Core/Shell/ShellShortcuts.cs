using System.Windows.Forms;

namespace TechBench
{
    internal enum ShellShortcut
    {
        None,
        FocusSearch,
        AddCode,
        CheatSheet
    }

    /// <summary>
    /// Main-window keys: Ctrl+F or F3 search, Ctrl+N add code, F1 cheat sheet. No UI.
    /// </summary>
    internal static class ShellShortcuts
    {
        public static ShellShortcut For(bool control, Keys keyCode)
        {
            if ((control && keyCode == Keys.F) || keyCode == Keys.F3) return ShellShortcut.FocusSearch;
            if (control && keyCode == Keys.N) return ShellShortcut.AddCode;
            if (keyCode == Keys.F1) return ShellShortcut.CheatSheet;
            return ShellShortcut.None;
        }
    }
}
