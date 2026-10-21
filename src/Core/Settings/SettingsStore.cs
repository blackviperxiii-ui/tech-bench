using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using J1939Reader;

namespace TechBench
{
    /// <summary>
    /// Single front door for settings.json / adapter-prefs.json / job-strip persistence.
    /// BeginLoad/EndLoad refuse writes while startup fills controls (generalises the old
    /// per-control _filling guards). Disk writes go through WorkOrderStore's atomic
    /// temp→replace→.bak helper (v1.2.7).
    /// </summary>
    internal static class SettingsStore
    {
        static int _loadDepth;
        static readonly JavaScriptSerializer Ser = new JavaScriptSerializer();
        static readonly Encoding Utf8 = new UTF8Encoding(false);

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

        public static bool AllowWrite()
        {
            return _loadDepth <= 0;
        }

        /// <summary>Atomic JSON/text write. No-op while loading. Keeps path.bak on replace. Rethrows after log so callers do not assume success.</summary>
        public static void WriteText(string path, string text)
        {
            if (!AllowWrite()) return;
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                WorkOrderStore.AtomicWriteText(path, text ?? "", Utf8);
            }
            catch (Exception ex)
            {
                SessionIo.LogError("SettingsStore.WriteText", ex);
                throw;
            }
        }

        public static void SaveAppSettings(AppSettings settings)
        {
            if (settings == null) return;
            settings.Save();
        }

        /// <summary>Job-strip customer field → settings.json (merge-then-write).</summary>
        public static string SaveJobCustomer(string customer)
        {
            if (!AllowWrite())
            {
                AppSettings cur = AppSettings.Load();
                return cur.Customer ?? "";
            }
            AppSettings disk = AppSettings.Load();
            string next = (customer ?? "").Trim();
            // The job strip pushes the header on every keystroke in Model and Serial too; only a
            // changed customer is worth a temp, replace and .bak cycle.
            if (string.Equals(disk.Customer ?? "", next, StringComparison.Ordinal)) return next;
            disk.Customer = next;
            string path = AppSettings.PathOverride ?? AppSettings.PathName();
            disk.SaveTo(path);
            return disk.Customer;
        }

        /// <summary>Job-strip last work-order key → settings.json.</summary>
        public static string SaveJobWorkOrder(string lastWoKey)
        {
            if (!AllowWrite())
            {
                AppSettings cur = AppSettings.Load();
                return cur.LastWo ?? "";
            }
            AppSettings disk = AppSettings.Load();
            disk.LastWo = lastWoKey == null ? "" : lastWoKey.Trim();
            string path = AppSettings.PathOverride ?? AppSettings.PathName();
            disk.SaveTo(path);
            return disk.LastWo;
        }

        /// <summary>Clear saved job customer and last WO (explicit wipe; uses SaveTo).</summary>
        public static void ClearJobStrip()
        {
            if (!AllowWrite()) return;
            string path = AppSettings.PathOverride ?? AppSettings.PathName();
            AppSettings disk = AppSettings.Load();
            disk.Customer = "";
            disk.LastWo = "";
            disk.SaveTo(path);
        }

        /// <summary>Merge adapter prefs fields and persist (null or empty adapterId means leave unchanged).</summary>
        public static void MergeAdapterPrefs(string adapterId, int? shellPage, int? innerPage)
        {
            AdapterPrefs.Merge(adapterId, shellPage, innerPage);
        }

        public static string SerializeAppSettings(AppSettings settings)
        {
            return Ser.Serialize(settings ?? new AppSettings());
        }

        public static string SerializeAdapterPrefs(AdapterPrefsData data)
        {
            if (data == null) data = new AdapterPrefsData();
            return Ser.Serialize(data);
        }
    }
}
