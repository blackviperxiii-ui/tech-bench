using System;

namespace TechBench
{
    /// <summary>
    /// Blocks settings.json / adapter-prefs.json writes while startup (or any load) is still
    /// filling controls. TextChanged / SelectedIndexChanged fire during that fill and used to
    /// overwrite the technician's saved job, adapter, or page with empty defaults.
    /// </summary>
    internal static class SettingsWriteGate
    {
        static int _loadDepth;

        /// <summary>Tests reset depth between cases so a leaked BeginLoad cannot poison later checks.</summary>
        internal static void ResetForTests()
        {
            _loadDepth = 0;
        }

        public static void BeginLoad()
        {
            _loadDepth++;
        }

        public static void EndLoad()
        {
            if (_loadDepth > 0) _loadDepth--;
        }

        public static bool IsLoading
        {
            get { return _loadDepth > 0; }
        }

        /// <summary>True when a write is allowed. Callers skip the file write when false.</summary>
        public static bool AllowWrite()
        {
            return _loadDepth <= 0;
        }
    }
}
