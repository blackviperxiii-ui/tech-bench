using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace J1939Reader
{
    internal sealed class Rp1210Device
    {
        public int Id;
        public string Name = "";
        public string Description = "";
        public override string ToString()
        {
            string label = Description.Length > 0 ? Description : Name;
            return label.Length > 0 ? (Id + " — " + label) : ("device " + Id);
        }
    }

    /// <summary>One installed RP1210 vendor DLL, as described by its own INI.</summary>
    internal sealed class Rp1210Api
    {
        public string Id = "";          // e.g. CMNSI7 — also the DLL and INI base name
        public string Name = "";        // vendor-supplied display name
        public string DllPath = "";
        public readonly List<Rp1210Device> Devices = new List<Rp1210Device>();
        public readonly List<string> J1939Protocols = new List<string>();
        public bool SupportsIso15765;

        public override string ToString()
        {
            return Name.Length > 0 ? (Name + "  (" + Id + ")") : Id;
        }

        /// <summary>Windows directory, where RP121032.INI and the vendor INIs live.</summary>
        public static string WindowsDir()
        {
            try
            {
                string d = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (!string.IsNullOrEmpty(d)) return d;
            }
            catch { }
            return @"C:\Windows";
        }

        /// <summary>
        /// A 32-bit process on 64-bit Windows gets SysWOW64 redirected from System32, so plain
        /// System32 is the right probe; SysWOW64 is listed explicitly for the odd installer that
        /// writes there directly.
        /// </summary>
        public static List<string> DllSearchDirs()
        {
            string win = WindowsDir();
            var dirs = new List<string>();
            dirs.Add(Path.Combine(win, "System32"));
            dirs.Add(Path.Combine(win, "SysWOW64"));
            dirs.Add(win);
            return dirs;
        }

        public static string FindDll(string apiId)
        {
            foreach (string dir in DllSearchDirs())
            {
                try
                {
                    string p = Path.Combine(dir, apiId + ".DLL");
                    if (File.Exists(p)) return p;
                }
                catch { }
            }
            return "";
        }

        /// <summary>
        /// Enumerate installed adapters from RP121032.INI. Replaces the old hardcoded
        /// {device 2, 1, 111} guesswork, which silently failed on any adapter that enumerated
        /// somewhere else.
        /// </summary>
        public static List<Rp1210Api> Installed()
        {
            var found = new List<Rp1210Api>();
            string win = WindowsDir();
            Ini master = Ini.Load(Path.Combine(win, "RP121032.INI"));
            if (master == null) return found;
            foreach (string id in master.GetList("RP1210Support", "APIImplementations"))
            {
                Rp1210Api api = FromVendorIni(win, id);
                if (api != null) found.Add(api);
            }
            return found;
        }

        public static Rp1210Api FromVendorIni(string dir, string apiId)
        {
            Ini ini = Ini.Load(Path.Combine(dir, apiId + ".INI"));
            if (ini == null) return null;
            return FromIni(apiId, ini, FindDll(apiId));
        }

        public static Rp1210Api FromIni(string apiId, Ini ini, string dllPath)
        {
            if (ini == null) return null;
            var api = new Rp1210Api { Id = apiId, DllPath = dllPath ?? "" };
            api.Name = ini.Get("VendorInformation", "Name");

            foreach (int devId in ini.GetIntList("VendorInformation", "Devices"))
            {
                string sec = "DeviceInformation" + devId;
                var dev = new Rp1210Device
                {
                    Id = ini.GetInt(sec, "DeviceID", devId),
                    Name = ini.Get(sec, "DeviceName"),
                    Description = ini.Get(sec, "DeviceDescription")
                };
                api.Devices.Add(dev);
            }

            foreach (int protoId in ini.GetIntList("VendorInformation", "Protocols"))
            {
                string sec = "ProtocolInformation" + protoId;
                string str = ini.Get(sec, "ProtocolString");
                if (str.Length == 0) continue;
                if (str.IndexOf("ISO15765", StringComparison.OrdinalIgnoreCase) >= 0)
                    api.SupportsIso15765 = true;
                if (str.IndexOf("J1939", StringComparison.OrdinalIgnoreCase) < 0) continue;
                // Speeds are advertised separately; build one connect string per speed, fastest-
                // to-slowest is wrong for an industrial compressor so 250 goes first when offered.
                List<string> speeds = ini.GetList(sec, "ProtocolSpeed");
                if (speeds.Count == 0) { AddProto(api, str); continue; }
                if (speeds.Contains("250")) AddProto(api, str + ":Baud=250");
                foreach (string sp in speeds)
                    if (sp != "250") AddProto(api, str + ":Baud=" + sp);
                AddProto(api, str);
            }
            if (api.J1939Protocols.Count == 0)
            {
                AddProto(api, "J1939:Baud=250");
                AddProto(api, "J1939");
            }
            return api;
        }

        static void AddProto(Rp1210Api api, string proto)
        {
            if (!api.J1939Protocols.Contains(proto)) api.J1939Protocols.Add(proto);
        }

        public List<int> DeviceIds()
        {
            var ids = new List<int>();
            foreach (Rp1210Device d in Devices) if (!ids.Contains(d.Id)) ids.Add(d.Id);
            // Nothing declared: fall back to the IDs this shop's INLINE 7 has actually answered on.
            if (ids.Count == 0) { ids.Add(1); ids.Add(2); ids.Add(111); }
            return ids;
        }

        public bool LooksLikeInline7()
        {
            string hay = (Id + " " + Name).ToUpperInvariant();
            return hay.Contains("CIL7") || hay.Contains("INLINE") || hay.Contains("CMNSI")
                || hay.Contains("CUMMINS");
        }

        /// <summary>Cummins INLINE 7 first — that is what this bench runs.</summary>
        public static Rp1210Api Preferred(List<Rp1210Api> apis)
        {
            if (apis == null || apis.Count == 0) return null;
            foreach (Rp1210Api a in apis)
                if (a.LooksLikeInline7() && a.DllPath.Length > 0) return a;
            foreach (Rp1210Api a in apis)
                if (a.DllPath.Length > 0) return a;
            return apis[0];
        }

        /// <summary>
        /// Legacy single-DLL fallback for a machine whose RP121032.INI is missing or unreadable but
        /// which still has the Cummins DLL in place.
        /// </summary>
        public static Rp1210Api LegacyInline7()
        {
            string dll = FindDll("CIL7R32");
            var api = new Rp1210Api
            {
                Id = "CIL7R32",
                Name = "Cummins INLINE 7 (no INI found)",
                DllPath = dll
            };
            AddProto(api, "J1939:Baud=250");
            AddProto(api, "J1939");
            api.SupportsIso15765 = true;
            return api;
        }

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.AppendLine(ToString());
            sb.AppendLine("DLL: " + (DllPath.Length > 0 ? DllPath : "(not found)"));
            sb.Append("Devices: ");
            if (Devices.Count == 0) sb.AppendLine("(none declared)");
            else
            {
                for (int i = 0; i < Devices.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(Devices[i]);
                }
                sb.AppendLine();
            }
            sb.AppendLine("J1939: " + string.Join(", ", J1939Protocols.ToArray()));
            sb.AppendLine("ISO15765 (for UDS clear): " + (SupportsIso15765 ? "yes" : "not advertised"));
            return sb.ToString();
        }
    }
}
