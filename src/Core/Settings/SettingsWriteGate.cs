namespace TechBench
{
    /// <summary>
    /// Compatibility facade over <see cref="SettingsStore"/> load guard.
    /// Prefer SettingsStore in new code; ShellForm / INLINE 7 still call this name.
    /// </summary>
    internal static class SettingsWriteGate
    {
        internal static void ResetForTests()
        {
            SettingsStore.ResetForTests();
        }

        public static void BeginLoad()
        {
            SettingsStore.BeginLoad();
        }

        public static void EndLoad()
        {
            SettingsStore.EndLoad();
        }

        public static bool IsLoading
        {
            get { return SettingsStore.IsLoading; }
        }

        public static bool AllowWrite()
        {
            return SettingsStore.AllowWrite();
        }
    }
}
