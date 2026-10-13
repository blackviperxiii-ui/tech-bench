// Offline checks for the parts of Tech Bench that can be verified without an adapter.
// Includes the shipped field database under kb\. Run with test.bat.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using J1939Reader;
using TechBench;

static partial class SelfTest
{
    static void AdapterTests()
    {
        Rp1210Api api = Rp1210Api.FromIni("CMNSI7", Ini.Parse(VendorIni), @"C:\Windows\SysWOW64\CMNSI7.DLL");
        Check("api parsed", api != null, "null");
        Eq("vendor name", api.Name, "Cummins Inc. INLINE 7");
        Eq("device count", api.Devices.Count, 2);
        Eq("device ids from INI, not hardcoded", string.Join("|", api.DeviceIds().ConvertAll(i => i.ToString()).ToArray()), "1|2");
        Eq("device label", api.Devices[0].ToString(), "1 — INLINE 7 over USB");
        Check("250 baud offered first", api.J1939Protocols[0] == "J1939:Baud=250",
            "first was " + api.J1939Protocols[0]);
        Check("500 baud also offered", api.J1939Protocols.Contains("J1939:Baud=500"),
            string.Join(",", api.J1939Protocols.ToArray()));
        Check("ISO15765 detected", api.SupportsIso15765, "not detected");
        Check("recognised as INLINE 7", api.LooksLikeInline7(), "not recognised");

        // A vendor INI with no protocol section still has to produce something usable.
        Rp1210Api bare = Rp1210Api.FromIni("OTHER", Ini.Parse("[VendorInformation]\nName=Some Adapter\n"), "");
        Eq("bare vendor falls back to J1939:Baud=250", bare.J1939Protocols[0], "J1939:Baud=250");
        Eq("bare vendor falls back to known device ids",
            string.Join("|", bare.DeviceIds().ConvertAll(i => i.ToString()).ToArray()), "1|2|111");
        Check("bare vendor not mistaken for INLINE 7", !bare.LooksLikeInline7(), "misidentified");
        Check("no ISO15765 claimed when not advertised", !bare.SupportsIso15765, "claimed");

        var apis = new List<Rp1210Api> { bare, api };
        Check("INLINE 7 preferred over another adapter", Rp1210Api.Preferred(apis) == api, "picked the wrong one");
        Check("Describe mentions the DLL", api.Describe().Contains("CMNSI7.DLL"), api.Describe());
        Check("Preferred on empty list is null", Rp1210Api.Preferred(new List<Rp1210Api>()) == null, "not null");
        Check("Installed() does not throw off Windows", Rp1210Api.Installed() != null, "threw or null");

        AdapterPrefsTests();
    }

    static void AdapterPrefsTests()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tb-adapter-prefs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string prev = AdapterPrefs.FolderOverride;
        AdapterPrefs.FolderOverride = dir;
        try
        {
            Eq("default shell page", AdapterPrefs.ClampShell(-3), 0);
            Eq("default inner page", AdapterPrefs.ClampInner(99), 6);
            Check("fresh load is empty", AdapterPrefs.Load().AdapterId == "", "had id");

            var data = new AdapterPrefsData { AdapterId = "CMNSI7", ShellPage = 2, InnerPage = 4 };
            AdapterPrefs.Save(data);
            string path = AdapterPrefs.PathName();
            Check("prefs file name is version-neutral", path.EndsWith("adapter-prefs.json", StringComparison.Ordinal), path);

            AdapterPrefsData round = AdapterPrefs.Load();
            Eq("round-trip adapter id", round.AdapterId, "CMNSI7");
            Eq("round-trip shell page", round.ShellPage, 2);
            Eq("round-trip inner page", round.InnerPage, 4);

            AdapterPrefs.Merge("OTHER", 3, 1);
            AdapterPrefsData merged = AdapterPrefs.Load();
            Eq("merge adapter", merged.AdapterId, "OTHER");
            Eq("merge shell", merged.ShellPage, 3);
            Eq("merge inner", merged.InnerPage, 1);

            Check("exact adapter match", AdapterPrefs.SavedAdapterMatches("CMNSI7", "CMNSI7"), "no");
            Check("case-sensitive adapter match", !AdapterPrefs.SavedAdapterMatches("cmnsi7", "CMNSI7"), "matched");
            Check("empty saved id does not match", !AdapterPrefs.SavedAdapterMatches("", "CMNSI7"), "matched");

            File.WriteAllText(path, "{not json");
            Check("corrupt file loads defaults", AdapterPrefs.Load().AdapterId == "", "had id");

            AdapterPrefs.Save(new AdapterPrefsData { AdapterId = "CMNSI7", ShellPage = 2, InnerPage = 4 });
            string frozen = File.ReadAllText(path);
            Check("prefs json has no app version stamp",
                frozen.IndexOf(AppVersion.Number, StringComparison.Ordinal) < 0, frozen);
            Eq("prefs reload after simulated upgrade", AdapterPrefs.LoadFrom(path).InnerPage, 4);
        }
        finally
        {
            AdapterPrefs.FolderOverride = prev;
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    // ---------------- names ----------------

    static void NameTests()
    {
        Names.Reset();
        Eq("built-in SPN", Names.Spn(1761), "DEF tank level");
        Eq("built-in SA", Names.Sa(48), "Compressor controller");
        Eq("built-in PGN", Names.Pgn(0xFECA), "DM1 active DTCs");
        Eq("unknown SPN is blank", Names.Spn(999999), "");
        Eq("unknown SA falls back to number", Names.Sa(77), "SA 77");

        var spn = new Dictionary<int, string> { { 1761, "DEF level (shop wording)" }, { 4001, "Shop-added SPN" } };
        var sa = new Dictionary<int, string> { { 77, "Doosan aux module" } };
        Names.Load(spn, null, sa);
        Eq("override replaces built-in", Names.Spn(1761), "DEF level (shop wording)");
        Eq("override adds a new SPN without a rebuild", Names.Spn(4001), "Shop-added SPN");
        Eq("override adds a new SA", Names.Sa(77), "Doosan aux module");
        Eq("un-overridden built-in still works", Names.Spn(5246), "SCR operator inducement severity");
        Eq("SpnName delegates to Names", J1939Decode.SpnName(4001), "Shop-added SPN");
        Names.Reset();
        Eq("reset restores built-in", Names.Spn(1761), "DEF tank level");
        Eq("reset drops added SPN", Names.Spn(4001), "");
    }

    // ---------------- trend ----------------

    static TrendSample Sample(DateTime t, double rpm)
    {
        return new TrendSample
        {
            Time = t, Rpm = rpm, CoolantC = double.NaN, OilKpa = double.NaN,
            BatteryV = double.NaN, FuelLph = double.NaN
        };
    }

}
