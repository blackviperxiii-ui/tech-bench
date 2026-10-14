using System;
using System.IO;
using System.Web.Script.Serialization;
using J1939Reader;

namespace TechBench
{
    /// <summary>TB1b: remember last RP1210 adapter and INLINE 7 page.</summary>
    internal sealed class AdapterPrefsData
    {
        public string AdapterId = "";
        public int ShellPage;
        public int InnerPage;
    }

    internal static class AdapterPrefs
    {
        public const int ShellPageCount = 4;
        public const int InnerPageCount = 7;

        /// <summary>Tests point this at a temp folder so we do not touch LocalAppData.</summary>
        public static string FolderOverride;

        public static string Folder()
        {
            if (!string.IsNullOrEmpty(FolderOverride))
            {
                Directory.CreateDirectory(FolderOverride);
                return FolderOverride;
            }
            return AppSettings.Folder();
        }

        public static string PathName()
        {
            return Path.Combine(Folder(), "adapter-prefs.json");
        }

        public static string PathName(string folder)
        {
            return Path.Combine(folder, "adapter-prefs.json");
        }

        public static AdapterPrefsData Load()
        {
            return LoadFrom(PathName());
        }

        public static AdapterPrefsData LoadFrom(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new AdapterPrefsData();
                var ser = new JavaScriptSerializer();
                var d = ser.Deserialize<AdapterPrefsData>(File.ReadAllText(path));
                if (d == null) return new AdapterPrefsData();
                d.ShellPage = ClampShell(d.ShellPage);
                d.InnerPage = ClampInner(d.InnerPage);
                if (d.AdapterId == null) d.AdapterId = "";
                return d;
            }
            catch
            {
                return new AdapterPrefsData();
            }
        }

        public static void Save(AdapterPrefsData data)
        {
            if (data == null) data = new AdapterPrefsData();
            data.ShellPage = ClampShell(data.ShellPage);
            data.InnerPage = ClampInner(data.InnerPage);
            if (data.AdapterId == null) data.AdapterId = "";
            SaveTo(PathName(), data);
        }

        public static void SaveTo(string path, AdapterPrefsData data)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var ser = new JavaScriptSerializer();
                File.WriteAllText(path, ser.Serialize(data));
            }
            catch (Exception ex)
            {
                SessionIo.LogError("AdapterPrefs.SaveTo", ex);
            }
        }

        /// <summary>Merges non-null fields into the on-disk prefs file.</summary>
        public static void Merge(string adapterId, int? shellPage, int? innerPage)
        {
            AdapterPrefsData d = Load();
            if (adapterId != null) d.AdapterId = adapterId;
            if (shellPage.HasValue) d.ShellPage = ClampShell(shellPage.Value);
            if (innerPage.HasValue) d.InnerPage = ClampInner(innerPage.Value);
            Save(d);
        }

        public static int ClampShell(int page)
        {
            if (page < 0) return 0;
            if (page >= ShellPageCount) return ShellPageCount - 1;
            return page;
        }

        public static int ClampInner(int page)
        {
            if (page < 0) return 0;
            if (page >= InnerPageCount) return InnerPageCount - 1;
            return page;
        }

        /// <summary>True when <paramref name="adapterId"/> matches a saved id (case-insensitive).</summary>
        public static bool SavedAdapterMatches(string savedAdapterId, string adapterId)
        {
            return savedAdapterId != null && adapterId != null
                && savedAdapterId.Length > 0
                && string.Equals(savedAdapterId, adapterId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
