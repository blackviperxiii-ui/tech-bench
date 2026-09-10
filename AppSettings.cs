using System;
using System.IO;
using System.Web.Script.Serialization;

namespace TechBench
{
    /// <summary>
    /// Tiny LocalAppData settings: last job (model/serial) and window size.
    /// Missing or corrupt file is a no-op — the app still launches.
    /// </summary>
    internal sealed class AppSettings
    {
        public string Model = "";
        public string Serial = "";
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

        public static AppSettings Load()
        {
            return LoadFrom(PathName());
        }

        public static AppSettings LoadFrom(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new AppSettings();
                var ser = new JavaScriptSerializer();
                var s = ser.Deserialize<AppSettings>(File.ReadAllText(path));
                return s ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save()
        {
            SaveTo(PathName());
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
            catch { }
        }
    }
}
