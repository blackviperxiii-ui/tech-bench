using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace TechBench
{
    /// <summary>
    /// Tiny LocalAppData settings: last job (model/serial/customer/WO key) and window size.
    /// Missing or corrupt file is a no-op — the app still launches.
    /// </summary>
    internal sealed class AppSettings
    {
        internal static string PathOverride;

        public string Model = "";
        public string Serial = "";
        public string Customer = "";
        public string LastWo = "";
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public bool Maximized;
        public string ManifestUrl = "";

        public static string Folder()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root))
                root = Path.GetTempPath();
            string d = Path.Combine(root, "TechBench");
            Directory.CreateDirectory(d);
            return d;
        }

        public static string PathName()
        {
            return Path.Combine(Folder(), "settings.json");
        }

        public static string PathName(string folder)
        {
            return Path.Combine(folder, "settings.json");
        }

        /// <summary>Match a saved work-order key against a loaded list; empty if absent.</summary>
        public static string MatchWorkOrderKey(IList<WorkOrder> orders, string key)
        {
            if (orders == null || string.IsNullOrWhiteSpace(key)) return "";
            string want = key.Trim();
            for (int i = 0; i < orders.Count; i++)
            {
                if (orders[i] == null) continue;
                if (string.Equals(orders[i].Key(), want, StringComparison.OrdinalIgnoreCase))
                    return orders[i].Key();
            }
            return "";
        }

        public static AppSettings Load()
        {
            if (!string.IsNullOrEmpty(PathOverride))
                return LoadFrom(PathOverride);
            return LoadFrom(PathName());
        }

        public static AppSettings LoadFrom(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new AppSettings();
                var ser = new JavaScriptSerializer();
                var s = ser.Deserialize<AppSettings>(File.ReadAllText(path));
                if (s == null) return new AppSettings();
                if (s.Customer == null) s.Customer = "";
                if (s.LastWo == null) s.LastWo = "";
                return s;
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save()
        {
            string path = !string.IsNullOrEmpty(PathOverride) ? PathOverride : PathName();
            try
            {
                if (File.Exists(path))
                {
                    AppSettings onDisk = LoadFrom(path);
                    if (string.IsNullOrEmpty(Customer) && !string.IsNullOrEmpty(onDisk.Customer))
                        Customer = onDisk.Customer;
                    if (string.IsNullOrEmpty(LastWo) && !string.IsNullOrEmpty(onDisk.LastWo))
                        LastWo = onDisk.LastWo;
                }
            }
            catch { }
            SaveTo(path);
        }

        public void SaveTo(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var ser = new JavaScriptSerializer();
                File.WriteAllText(path, ser.Serialize(this));
            }
            catch (Exception ex)
            {
                SessionData.LogError("AppSettings.SaveTo", ex);
            }
        }
    }
}
