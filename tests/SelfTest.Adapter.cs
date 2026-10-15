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
    [SelfTestCase("RP1210 adapter discovery", Order = 90)]
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
            Check("case-insensitive adapter match", AdapterPrefs.SavedAdapterMatches("cmnsi7", "CMNSI7"), "no");
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

    [SelfTestCase("shell page registry", Order = 95)]
    static void ShellPageRegistryTests()
    {
        Eq("shell page count", ShellPages.Count, 4);
        Eq("shell page titles in switch-bar order", string.Join("|", ShellPages.Titles()),
            "Search|Work orders|INLINE 7|Adapters");
        var panels = new List<string>();
        var ids = new List<string>();
        for (int i = 0; i < ShellPages.Count; i++)
        {
            panels.Add(ShellPages.At(i).PanelName);
            ids.Add(((int)ShellPages.At(i).Id).ToString());
        }
        Eq("shell panel names unchanged", string.Join("|", panels.ToArray()),
            "shellPage0|shellPage1|shellPage2|shellPage3");
        Eq("saved shell page ints map to registry order", string.Join("|", ids.ToArray()), "0|1|2|3");
        Eq("INLINE 7 is saved as page 2", (int)ShellPageId.Inline7, 2);
        Eq("clamp below range", ShellPages.Clamp(-1), 0);
        Eq("clamp in range", ShellPages.Clamp(2), 2);
        Eq("clamp past end", ShellPages.Clamp(4), 3);
        Eq("At falls back to first page", ShellPages.At(-5).Title, "Search");
        Eq("At falls back to last page", ShellPages.At(99).Title, "Adapters");
        Eq("AdapterPrefs page count follows registry", AdapterPrefs.ShellPageCount, 4);
        Eq("AdapterPrefs clamp follows registry", AdapterPrefs.ClampShell(7), 3);

        string dir = Path.Combine(Path.GetTempPath(), "tb-shell-pages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string prev = AdapterPrefs.FolderOverride;
        AdapterPrefs.FolderOverride = dir;
        try
        {
            AdapterPrefs.Save(new AdapterPrefsData { AdapterId = "CMNSI7", ShellPage = 1, InnerPage = 0 });
            Eq("saved page 1 restores Work orders",
                ShellPages.At(AdapterPrefs.ClampShell(AdapterPrefs.Load().ShellPage)).Title, "Work orders");
            File.WriteAllText(AdapterPrefs.PathName(), "{\"AdapterId\":\"CMNSI7\",\"ShellPage\":9,\"InnerPage\":0}");
            Eq("out-of-range saved page restores Adapters",
                ShellPages.At(AdapterPrefs.Load().ShellPage).Title, "Adapters");
        }
        finally
        {
            AdapterPrefs.FolderOverride = prev;
            try { Directory.Delete(dir, true); }
            catch { }
        }

        // One list: the shell builds its switch bar from the registry, and the layout audit's
        // independent page list still agrees with it.
        string root = FindRepoRoot();
        string shellSrc = File.ReadAllText(Path.Combine(root, "src", "Shell", "ShellForm.cs"));
        Check("shell switch bar reads the registry",
            shellSrc.IndexOf("UiLayout.SwitchBar(ShellPages.Titles()", StringComparison.Ordinal) >= 0
            && shellSrc.IndexOf("\"Work orders\"", StringComparison.Ordinal) < 0,
            "ShellForm still hardcodes shell page titles");
        string audit = File.ReadAllText(Path.Combine(root, "tools", "LayoutAudit.cs"));
        Check("layout audit page list matches registry",
            audit.IndexOf("{ \"Search\", \"Work orders\", \"INLINE 7\", \"Adapters\" }", StringComparison.Ordinal) >= 0,
            "tools/LayoutAudit.cs page names drifted from ShellPages");
    }

    [SelfTestCase("adapters page summary", Order = 96)]
    static void AdapterSummaryTests()
    {
        string nl = Environment.NewLine;
        string none = AdapterSummary.Text(@"C:\Windows\RP121032.INI", true, new List<Rp1210Api>());
        Eq("no adapters text", none, string.Join(nl, new[]
        {
            "RP1210 adapters installed on this PC",
            "(read from C:\\Windows\\RP121032.INI)",
            "",
            "Process: 32-bit — correct for RP1210",
            "",
            "None found.",
            "",
            "Install the adapter vendor's RP1210 drivers (for the INLINE 7 that is the",
            "Cummins CIL7 package), then press Rescan.",
            ""
        }));
        Eq("null adapter list reads as none",
            AdapterSummary.Text(@"C:\Windows\RP121032.INI", true, null), none);
        Check("64-bit host warns drivers will not load",
            AdapterSummary.Text("x", false, null).Contains("Process: 64-bit — RP1210 drivers will NOT load; rebuild with build.bat" + nl),
            "missing 64-bit warning");

        Rp1210Api api = Rp1210Api.FromIni("CMNSI7", Ini.Parse(VendorIni), @"C:\Windows\SysWOW64\CMNSI7.DLL");
        Eq("one INLINE 7 text", AdapterSummary.Text(@"C:\Windows\RP121032.INI", true, new List<Rp1210Api> { api }),
            string.Join(nl, new[]
            {
                "RP1210 adapters installed on this PC",
                "(read from C:\\Windows\\RP121032.INI)",
                "",
                "Process: 32-bit — correct for RP1210",
                "",
                "Cummins Inc. INLINE 7  (CMNSI7)",
                "DLL: C:\\Windows\\SysWOW64\\CMNSI7.DLL",
                "Devices: 1 — INLINE 7 over USB, 2 — INLINE 7 Bluetooth",
                "J1939: J1939:Baud=250, J1939:Baud=500, J1939",
                "ISO15765: yes",
                "",
                "",
                "The INLINE 7 tab connects with whichever adapter is picked in its Adapter box.",
                "Sullair / IR Xe datalinks appear here automatically once their drivers are installed.",
                ""
            }));

        string shellSrc = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Shell", "ShellForm.cs"));
        Check("shell hosts AdaptersPage for the Adapters slot",
            shellSrc.IndexOf("{ ShellPageId.Adapters, new AdaptersPage() }", StringComparison.Ordinal) >= 0
            && shellSrc.IndexOf("DescribeAdapters", StringComparison.Ordinal) < 0,
            "ShellForm still builds the adapters page itself");
    }

    // ---------------- names ----------------

    [SelfTestCase("name overrides", Order = 100)]
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
