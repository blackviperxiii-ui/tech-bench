// Offline checks for the parts of Tech Bench that can be verified without an adapter or the real KB:
// J1939 decoding, BAM reassembly, and knowledge-base loading/search. Run with test.bat.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using J1939Reader;
using TechBench;

static class SelfTest
{
    static int _fail;

    static void Check(string what, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what + (ok ? "" : "   -> " + detail));
        if (!ok) _fail++;
    }

    static void Eq(string what, object got, object want)
    {
        string g = got == null ? "(null)" : got.ToString();
        string w = want == null ? "(null)" : want.ToString();
        Check(what, g == w, "got [" + g + "] want [" + w + "]");
    }

    static int Main()
    {
        Console.WriteLine("== DM1 lamp decode (byte0: MIL 7-6, red 5-4, amber 3-2, protect 1-0) ==");
        LampCase("all off", 0x00, false, false, false, false);
        LampCase("amber only (0b00_00_01_00)", 0x04, false, true, false, false);
        LampCase("red only (0b00_01_00_00)", 0x10, true, false, false, false);
        LampCase("protect only (0b00_00_00_01)", 0x01, false, false, true, false);
        LampCase("MIL only (0b01_00_00_00)", 0x40, false, false, false, true);
        LampCase("red+amber", 0x14, true, true, false, false);
        LampCase("all four on", 0x55, true, true, true, true);

        Console.WriteLine();
        Console.WriteLine("== DTC unpack ==");
        // SPN 5246 FMI 0, occ 1 -> low=0x7E, mid=0x14 (5246 = 0x147E), high bits 0, fmi 0
        var dtcs = new List<Dtc>();
        bool r, a;
        byte[] dm1 = { 0x10, 0xFF, 0x7E, 0x14, 0x00, 0x01, 0xFF, 0xFF };
        J1939Decode.ParseDm(dm1, dtcs, out r, out a);
        Eq("one DTC parsed", dtcs.Count, 1);
        if (dtcs.Count == 1)
        {
            Eq("SPN", dtcs[0].Spn, 5246);
            Eq("FMI", dtcs[0].Fmi, 0);
            Eq("occ", dtcs[0].Occ, 1);
            Eq("name", dtcs[0].Name, "SCR operator inducement severity");
        }
        Eq("red stop set", r, true);

        // SPN 524287-ish high bits: SPN 1761 = 0x6E1 -> low 0xE1, mid 0x06, fmi 9
        dtcs.Clear();
        byte[] dm1b = { 0x04, 0xFF, 0xE1, 0x06, 0x09, 0x02, 0xE1, 0x06, 0x09, 0x03 };
        J1939Decode.ParseDm(dm1b, dtcs, out r, out a);
        Eq("duplicate SPN/FMI collapsed", dtcs.Count, 1);
        if (dtcs.Count == 1) { Eq("SPN 1761", dtcs[0].Spn, 1761); Eq("FMI 9", dtcs[0].Fmi, 9); }
        Eq("amber set", a, true);

        Console.WriteLine();
        Console.WriteLine("== scalar decodes ==");
        // EEC1: RPM at bytes 4-5, 0.125 rpm/bit. 800 rpm -> raw 6400 -> 0x1900
        Eq("RPM 800", J1939Decode.Rpm(new byte[] { 0, 0, 0, 0x00, 0x19, 0, 0, 0 }).ToString("0"), "800");
        Eq("RPM not available", J1939Decode.Rpm(new byte[] { 0, 0, 0, 0xFF, 0xFF, 0, 0, 0 }), -1d);
        Eq("RPM short frame", J1939Decode.Rpm(new byte[] { 0, 0 }), -1d);
        Eq("coolant 90C", J1939Decode.TempC(130), "90 C");
        Eq("coolant n/a", J1939Decode.TempC(0xFF), "n/a");
        // battery: bytes 5-6, 0.05 V/bit. 27.60 V -> raw 552 -> 0x0228
        Eq("battery 27.60V", J1939Decode.BatteryV(new byte[] { 0, 0, 0, 0, 0x28, 0x02, 0, 0 }), "27.60 V");
        Eq("battery short frame", J1939Decode.BatteryV(new byte[] { 0, 0 }), "n/a");
        // DEF tank: byte0 level 0.4%/bit, byte1 temp -40 offset. 50% -> 125, 20C -> 60
        Eq("DEF 50% 20C", J1939Decode.DefText(new byte[] { 125, 60 }), "50.0%  temp 20 C");
        Eq("DEF header silent", J1939Decode.DefText(new byte[] { 0xFF, 0xFF }), "n/a (not talking)");
        // hours: 0.05 h/bit over 4 bytes. 1234.5 h -> raw 24690 -> 0x6072
        Eq("engine hours", J1939Decode.Hours(new byte[] { 0x72, 0x60, 0x00, 0x00 }), "1234.5 h");
        Eq("hours not available", J1939Decode.Hours(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }), null);
        Eq("hours short frame", J1939Decode.Hours(new byte[] { 0x01 }), null);

        Console.WriteLine();
        Console.WriteLine("== ASCII / component ID ==");
        Eq("VIN strips star", J1939Decode.Ascii(Bytes("1ABC23456789*")), "1ABC23456789");
        Eq("component keeps fields", J1939Decode.Ascii(Bytes("CUMMINS*QSB6.7*12345678*A1*"), true),
            "CUMMINS  ·  QSB6.7  ·  12345678  ·  A1");
        Eq("empty", J1939Decode.Ascii(new byte[0]), "");

        Console.WriteLine();
        Console.WriteLine("== BAM reassembly ==");
        BamTests();

        Console.WriteLine();
        Console.WriteLine("== KB load resilience ==");
        KbTests();

        Console.WriteLine();
        Console.WriteLine("== INI parsing ==");
        IniTests();

        Console.WriteLine();
        Console.WriteLine("== RP1210 adapter discovery ==");
        AdapterTests();

        Console.WriteLine();
        Console.WriteLine("== name overrides ==");
        NameTests();

        Console.WriteLine();
        Console.WriteLine("== trend log ==");
        TrendTests();

        Console.WriteLine();
        Console.WriteLine("== fault timeline ==");
        TimelineTests();

        Console.WriteLine();
        Console.WriteLine("== bus monitor routing ==");
        MonitorTests();

        Console.WriteLine();
        Console.WriteLine("== disconnected snapshot ==");
        SnapshotBlankTests();

        Console.WriteLine();
        Console.WriteLine("== TSC1 destination ==");
        Tsc1DestTests();

        Console.WriteLine();
        Console.WriteLine("== unit history ==");
        HistoryTests();

        Console.WriteLine();
        Console.WriteLine("== user codes round-trip ==");
        UserCodeTests();

        Console.WriteLine();
        Console.WriteLine("== snapshot diff ==");
        DiffTests();

        Console.WriteLine();
        Console.WriteLine("== job report ==");
        ReportTests();

        Console.WriteLine();
        Console.WriteLine("== settings ==");
        SettingsTests();

        Console.WriteLine();
        Console.WriteLine("== updater ==");
        UpdaterTests();

        Console.WriteLine();
        Console.WriteLine("== shop two-way sync ==");
        ShopSyncTests();

        Console.WriteLine();
        Console.WriteLine(_fail == 0 ? "ALL PASS" : (_fail + " FAILURES"));
        return _fail == 0 ? 0 : 1;
    }

    // ---------------- INI ----------------

    const string VendorIni = @"
[VendorInformation]
Name=Cummins Inc. INLINE 7
Devices=1,2
Protocols=1,2
; a comment line
[DeviceInformation1]
DeviceID=1
DeviceName=INLINE7-USB
DeviceDescription=""INLINE 7 over USB""
[DeviceInformation2]
DeviceID=2
DeviceName=INLINE7-BT
DeviceDescription=INLINE 7 Bluetooth
[ProtocolInformation1]
ProtocolString=J1939
ProtocolDescription=SAE J1939
ProtocolSpeed=250,500
[ProtocolInformation2]
ProtocolString=ISO15765
ProtocolDescription=ISO 15765
";

    static void IniTests()
    {
        Ini ini = Ini.Parse(VendorIni);
        Eq("section value", ini.Get("VendorInformation", "Name"), "Cummins Inc. INLINE 7");
        Eq("case-insensitive section", ini.Get("vendorinformation", "name"), "Cummins Inc. INLINE 7");
        Eq("quotes stripped", ini.Get("DeviceInformation1", "DeviceDescription"), "INLINE 7 over USB");
        Eq("missing key is empty", ini.Get("VendorInformation", "Nope"), "");
        Eq("missing section is empty", ini.Get("Nope", "Nope"), "");
        Eq("int list", string.Join("|", ini.GetIntList("VendorInformation", "Devices").ConvertAll(i => i.ToString()).ToArray()), "1|2");
        Eq("GetInt fallback", ini.GetInt("DeviceInformation1", "Missing", 7), 7);
        Check("comments ignored", !ini.HasSection("; a comment line"), "comment became a section");
        Eq("empty text does not throw", Ini.Parse("").Get("a", "b"), "");
        Eq("null text does not throw", Ini.Parse(null).Get("a", "b"), "");
        Eq("bad line ignored", Ini.Parse("[S]\r\ngarbage\r\nk=v").Get("S", "k"), "v");
        Eq("crlf and lf both split", Ini.Parse("[S]\nk=v\r\nj=w").Get("S", "j"), "w");
    }

    // ---------------- adapter discovery ----------------

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
        Check("ISO15765 detected for UDS clear", api.SupportsIso15765, "not detected");
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

    static void TrendTests()
    {
        var log = new TrendLog(4);
        DateTime t = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 4; i++) log.Add(Sample(t.AddSeconds(i), 100 + i));
        Eq("fills to capacity", log.Count, 4);
        Eq("oldest first", log[0].Rpm, 100d);
        log.Add(Sample(t.AddSeconds(4), 104));
        Eq("ring buffer stays at capacity", log.Count, 4);
        Eq("oldest sample rolled off", log[0].Rpm, 101d);
        Eq("newest sample kept", log[3].Rpm, 104d);

        var rate = new TrendLog(100);
        Check("first offer accepted", rate.Offer(Sample(t, 800)), "rejected");
        Check("second offer within the same second rejected", !rate.Offer(Sample(t.AddMilliseconds(300), 810)), "accepted");
        Check("offer a second later accepted", rate.Offer(Sample(t.AddSeconds(1), 820)), "rejected");
        Eq("only two samples taken", rate.Count, 2);

        List<TrendSample> copy = rate.Copy();
        Eq("copy length", copy.Count, 2);
        rate.Clear();
        Eq("clear empties", rate.Count, 0);
        Eq("copy is detached from the log", copy.Count, 2);

        var mixed = new List<TrendSample>();
        mixed.Add(Sample(t, 500));
        mixed.Add(Sample(t.AddSeconds(1), double.NaN));
        mixed.Add(Sample(t.AddSeconds(2), 1500));
        double min, max;
        Check("range ignores missing values", TrendLog.Range(mixed, TrendChannel.Rpm, out min, out max)
            && min == 500 && max == 1500, "min=" + min + " max=" + max);
        Check("range reports nothing for a never-published channel",
            !TrendLog.Range(mixed, TrendChannel.Coolant, out min, out max), "claimed a range");

        string csv = TrendLog.Csv(mixed);
        string[] lines = csv.Trim().Replace("\r\n", "\n").Split('\n');
        Eq("csv header + one row per sample", lines.Length, 4);
        Check("csv header is stable", lines[0].StartsWith("time,rpm,coolant_c"), lines[0]);
        Check("missing value is an empty cell, not NaN", lines[2].Contains(",,"), lines[2]);
        Eq("channel label", TrendLog.Label(TrendChannel.Oil), "Oil kPa");
    }

    // ---------------- timeline ----------------

    static List<Dtc> Dtcs(params int[] spnFmi)
    {
        var list = new List<Dtc>();
        for (int i = 0; i + 1 < spnFmi.Length; i += 2)
            list.Add(new Dtc { Spn = spnFmi[i], Fmi = spnFmi[i + 1], Occ = 1, Name = Names.Spn(spnFmi[i]) });
        return list;
    }

    static void TimelineTests()
    {
        var tl = new DtcTimeline();
        DateTime t = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

        tl.Update(Dtcs(1761, 9), t, 0);
        Eq("first code recorded", tl.Count, 1);
        Eq("rpm at first captured", tl.InOrder()[0].RpmAtFirst, 0d);

        tl.Update(Dtcs(1761, 9, 5246, 0), t.AddSeconds(4), 180);
        Eq("second code added", tl.Count, 2);
        DtcEvent induce = tl.InOrder()[1];
        Eq("inducement rpm at first", induce.RpmAtFirst, 180d);
        Eq("inducement first seen 4s in", (induce.FirstSeen - t).TotalSeconds, 4d);

        // Dropping out marks cleared but must not delete the finding.
        tl.Update(Dtcs(1761, 9), t.AddSeconds(10), 800);
        Eq("cleared code retained", tl.Count, 2);
        Check("inducement marked cleared", !tl.InOrder()[1].Active, "still active");
        Check("tank fault still active", tl.InOrder()[0].Active, "marked cleared");

        // Coming back counts as another cycle rather than a new row.
        tl.Update(Dtcs(1761, 9, 5246, 0), t.AddSeconds(20), 1200);
        Eq("no duplicate row on return", tl.Count, 2);
        Eq("cycle counted", tl.InOrder()[1].Cycles, 2);
        Eq("rpm at return recorded", tl.InOrder()[1].RpmAtFirst, 1200d);

        tl.Update(null, t.AddSeconds(30), 0);
        Check("null list clears everything without throwing",
            !tl.InOrder()[0].Active && !tl.InOrder()[1].Active, "still active");

        Eq("recent is newest-first", tl.Recent()[0].LastSeen >= tl.Recent()[1].LastSeen, true);

        string csv = tl.Csv("HP450 123");
        Check("csv has a header", csv.StartsWith("first_seen,last_seen,job,spn,fmi"), csv.Split('\n')[0]);
        Check("csv quotes the job", csv.Contains("\"HP450 123\""), "job not quoted");
        Eq("csv row count", csv.Trim().Replace("\r\n", "\n").Split('\n').Length, 3);

        var e = new DtcEvent { FirstSeen = t, LastSeen = t.AddSeconds(75) };
        Eq("duration formatting", e.Duration(), "1m15s");
        tl.Clear();
        Eq("clear empties the timeline", tl.Count, 0);
    }

    // ---------------- bus monitor ----------------

    static J1939Frame F(int pgn, int sa, params byte[] data)
    {
        return new J1939Frame { Pgn = pgn, Sa = sa, Da = 255, Data = data };
    }

    static void MonitorTests()
    {
        DateTime t = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var m = new BusMonitor();

        // Engine SA is discovered from whoever broadcasts EEC1, instead of assuming 0.
        m.Feed(F(0xF004, 17, 0, 0, 0, 0x00, 0x19, 0, 0, 0), t);
        Eq("engine SA auto-detected from EEC1", m.EngineSa, 17);
        Eq("rpm decoded for that SA", m.Rpm, 800d);

        m.Feed(F(0xFEEE, 17, 130, 0, 0, 0, 0, 0, 0, 0), t);
        Eq("coolant from the engine", m.CoolantC, 90d);
        m.Feed(F(0xFEEE, 99, 200, 0, 0, 0, 0, 0, 0, 0), t);
        Eq("coolant from another module ignored", m.CoolantC, 90d);

        m.Feed(F(0xFEEF, 17, 0, 0, 0, 100, 0, 0, 0, 0), t);
        Eq("oil pressure decoded", m.OilKpa, 400d);
        Check("oil text has psi", m.OilText().Contains("psi"), m.OilText());

        m.Feed(F(0xFE56, 17, 125, 60, 0, 0, 0, 0, 0, 0), t);
        Check("DEF talking", !m.DefSilent, "reported silent");
        Eq("DEF percent", m.DefPct, 50d);
        m.Feed(F(0xFE56, 17, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0), t);
        Check("DEF silence detected", m.DefSilent, "reported talking");
        Eq("DEF text when silent", m.DefText(), "n/a (not talking)");

        // DM1 from two different modules must both be kept.
        m.Feed(F(0xFECA, 17, 0x10, 0xFF, 0x7E, 0x14, 0x00, 0x01, 0xFF, 0xFF), t);
        m.Feed(F(0xFECA, 48, 0x04, 0xFF, 0xE1, 0x06, 0x09, 0x02, 0xFF, 0xFF), t);
        Eq("engine DM1 kept", m.Engine.Active.Count, 1);
        Eq("engine red stop", m.Engine.Red, true);
        Eq("controller DM1 kept separately", m.Module(48).Active.Count, 1);
        Eq("controller SPN", m.Module(48).Active[0].Spn, 1761);
        Eq("controller amber", m.Module(48).Amber, true);
        Eq("engine not credited with the controller fault", m.Engine.Active[0].Spn, 5246);
        Eq("both modules listed", string.Join("|", m.ModulesWithFaults().ConvertAll(i => i.ToString()).ToArray()), "17|48");
        Check("HasSpn sees the engine inducement", m.HasSpn(5246), "missed");
        Check("HasSpn does not leak across modules", !m.HasSpn(1761), "leaked");

        // Pinning the engine SA overrides auto-detection.
        var pinned = new BusMonitor();
        pinned.EngineSaSetting = 0;
        pinned.Feed(F(0xF004, 17, 0, 0, 0, 0x00, 0x19, 0, 0, 0), t);
        Eq("pinned engine SA respected", pinned.EngineSa, 0);
        Check("rpm from the wrong SA ignored when pinned", double.IsNaN(pinned.Rpm), "took it anyway");

        // Identity PGNs the app requests must actually land.
        var id = new BusMonitor();
        id.Feed(F(0xF004, 0, 0, 0, 0, 0x00, 0x19, 0, 0, 0), t);
        id.Feed(F(0xFEEC, 0, Bytes("1ABC23456789*")), t);
        id.Feed(F(0xFEDA, 0, Bytes("SW1234*")), t);
        id.Feed(F(0xFEEB, 0, Bytes("CUMMINS*QSB6.7*555*")), t);
        id.Feed(F(0xFEE5, 0, 0x72, 0x60, 0x00, 0x00, 0, 0, 0, 0), t);
        Eq("VIN landed", id.Vin, "1ABC23456789");
        Eq("software landed", id.Sw, "SW1234");
        Eq("component id landed", id.CompId, "CUMMINS  ·  QSB6.7  ·  555");
        Eq("hours landed", id.HoursText, "1234.5 h");
        id.Feed(F(0xFEE5, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0), t);
        Eq("hours NA clears last reading", id.HoursText, "");

        // J1939 NA/error sentinels must blank the HUD, not keep the last good sample.
        m.Feed(F(0xFEF7, 17, 0, 0, 0, 0, 0x28, 0x02, 0, 0), t);
        Eq("battery decoded", m.BatteryV, 27.6d);
        m.Feed(F(0xFEF2, 17, 0x46, 0x00, 0, 0, 0, 0, 0, 0), t);
        Eq("fuel decoded", m.FuelLph, 3.5d);
        m.Feed(F(0xFEEE, 17, 0xFF, 0, 0, 0, 0, 0, 0, 0), t);
        Check("coolant NA clears last reading", double.IsNaN(m.CoolantC), "held " + m.CoolantC);
        m.Feed(F(0xFEEF, 17, 0, 0, 0, 0xFF, 0, 0, 0, 0), t);
        Check("oil NA clears last reading", double.IsNaN(m.OilKpa), "held " + m.OilKpa);
        m.Feed(F(0xFEF7, 17, 0, 0, 0, 0, 0xFF, 0xFF, 0, 0), t);
        Check("battery NA clears last reading", double.IsNaN(m.BatteryV), "held " + m.BatteryV);
        m.Feed(F(0xFEF2, 17, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0), t);
        Check("fuel NA clears last reading", double.IsNaN(m.FuelLph), "held " + m.FuelLph);

        // Trend sampling is rate limited and follows the engine.
        var tr = new BusMonitor();
        tr.Feed(F(0xF004, 0, 0, 0, 0, 0x00, 0x19, 0, 0, 0), t);
        Check("first trend sample taken", tr.SampleTrend(t), "skipped");
        Check("second within the same second skipped", !tr.SampleTrend(t.AddMilliseconds(200)), "taken");
        Check("sample a second later taken", tr.SampleTrend(t.AddSeconds(1)), "skipped");
        Eq("two samples stored", tr.Trend.Count, 2);
        Eq("sampled rpm", tr.Trend[0].Rpm, 800d);

        // Bus stats and clears.
        Eq("pgn rows tracked", m.PgnRows().Count > 0, true);
        Check("pgn rows sorted ascending", m.PgnRows()[0].Pgn <= m.PgnRows()[m.PgnRows().Count - 1].Pgn, "unsorted");
        m.ClearBusStats();
        Eq("bus stats cleared", m.PgnRows().Count, 0);
        m.ClearLive();
        Check("live values cleared", double.IsNaN(m.Rpm), "rpm survived");
        Eq("modules cleared", m.ModulesWithFaults().Count, 0);

        // Long DM1 over BAM must route to the right module.
        var bamMon = new BusMonitor();
        bamMon.Feed(F(0xF004, 0, 0, 0, 0, 0x00, 0x19, 0, 0, 0), t);
        bamMon.Feed(Cm(0, 12, 2, 0xFECA), t);
        // The second DTC deliberately straddles the packet boundary: byte 6 is in packet 1 and
        // bytes 7-9 are in packet 2.
        bamMon.Feed(Dt(0, 1, 0x10, 0xFF, 0x7E, 0x14, 0x00, 0x01, 0xE1), t);
        bool routed = bamMon.Feed(Dt(0, 2, 0x06, 0x09, 0x02, 0xFF, 0xFF), t);
        Check("multi-packet DM1 routed after reassembly", routed, "not routed");
        Eq("BAM DM1 produced two codes", bamMon.Engine.Active.Count, 2);
        Eq("first code from packet 1", bamMon.Engine.Active[0].Spn, 5246);
        Eq("second code spanning the packet boundary", bamMon.Engine.Active[1].Spn, 1761);
        Eq("boundary-spanning FMI", bamMon.Engine.Active[1].Fmi, 9);

        Check("null frame ignored", !bamMon.Feed(null, t), "accepted");
    }

    static void SnapshotBlankTests()
    {
        var s = new BusSnapshot
        {
            Rpm = 800, CoolantC = 90, OilKpa = 400, BatteryV = 27.6, FuelLph = 3.5,
            DefText = "50.0%  temp 20 C", OilText = "400 kPa",
            Vin = "1ABC", Sw = "SW", CompId = "CUMMINS", Hours = "10 h",
            Red = true, Amber = true, Protect = true, Mil = true,
            HasSpn5246 = true, HasTankFmi9 = true, Has1569 = true
        };
        s.Active.Add(new Dtc { Spn = 5246, Fmi = 0 });
        s.Prev.Add(new Dtc { Spn = 100, Fmi = 1 });
        s.FaultModules.Add(0);
        s.BlankDisconnectedReadouts();
        Check("disconnected snapshot blanks rpm", double.IsNaN(s.Rpm), "held " + s.Rpm);
        Check("disconnected snapshot blanks coolant", double.IsNaN(s.CoolantC), "held");
        Eq("disconnected snapshot blanks DEF", s.DefText, "—");
        Eq("disconnected snapshot blanks VIN", s.Vin, "");
        Eq("disconnected snapshot clears lamps", s.Red, false);
        Eq("disconnected snapshot clears active list", s.Active.Count, 0);
        Eq("disconnected snapshot clears modules", s.FaultModules.Count, 0);
    }

    static void Tsc1DestTests()
    {
        Eq("TSC1 dest follows engine SA 17", Rp1210.Tsc1Dest(17), (byte)17);
        Eq("TSC1 dest SA 0 stays 0", Rp1210.Tsc1Dest(0), (byte)0);
        Eq("TSC1 dest auto-unknown falls back to 0", Rp1210.Tsc1Dest(-1), (byte)0);
        Eq("TSC1 dest broadcast SA rejected", Rp1210.Tsc1Dest(255), (byte)0);
    }

    // ---------------- history ----------------

    static void HistoryTests()
    {
        Eq("csv split handles quoted commas",
            string.Join("|", History.SplitCsv("a,\"b,c\",d").ToArray()), "a|b,c|d");
        Eq("csv split handles escaped quotes",
            string.Join("|", History.SplitCsv("\"say \"\"hi\"\"\",x").ToArray()), "say \"hi\"|x");
        Eq("csv split handles trailing empty", History.SplitCsv("a,").Count, 2);

        Check("job matches on serial token", History.JobMatches("HP450  12345", "12345"), "no match");
        Check("job matches on both tokens", History.JobMatches("HP450  12345", "hp450 12345"), "no match");
        Check("job rejects a different serial", !History.JobMatches("HP450  12345", "99999"), "matched");
        Check("short tokens ignored", History.JobMatches("HP450  12345", "hp450 ab"), "no match");
        Check("empty query matches", History.JobMatches("anything", ""), "no match");

        string dir = Path.Combine(Path.GetTempPath(), "hist_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string header = "time,job,state,spn,fmi,occurrences,name,fmi_text";
        File.WriteAllText(Path.Combine(dir, "HP450_12345_session_20260101_090000_dtcs.csv"),
            header + "\n"
          + "2026-01-01T09:00:00,\"HP450 12345\",active,1761,9,3,\"DEF tank level\",\"abnormal update rate\"\n"
          + "2026-01-01T09:00:00,\"HP450 12345\",active,5246,0,1,\"SCR inducement\",\"above normal\"\n");
        File.WriteAllText(Path.Combine(dir, "HP450_12345_session_20260401_090000_dtcs.csv"),
            header + "\n2026-04-01T09:00:00,\"HP450 12345\",active,1761,9,7,\"DEF tank level\",\"abnormal update rate\"\n");
        File.WriteAllText(Path.Combine(dir, "OTHER_999_session_20260401_100000_dtcs.csv"),
            header + "\n2026-04-01T10:00:00,\"OTHER 999\",active,100,1,1,\"Oil pressure\",\"below normal\"\n");
        File.WriteAllText(Path.Combine(dir, "broken_dtcs.csv"), "not a csv at all");

        History h = History.Load(dir);
        Eq("all dtc files read", h.FilesRead, 4);
        Eq("entries parsed", h.Entries.Count, 4);

        List<HistoryFault> faults = h.ForJob("HP450 12345");
        Eq("faults for this unit only", faults.Count, 2);
        Eq("repeat offender first", faults[0].Spn, 1761);
        Eq("counted across two visits", faults[0].Visits, 2);
        Eq("one-off counted once", faults[1].Visits, 1);
        Check("first seen is the earlier visit", faults[0].First < faults[0].Last, "dates wrong");

        Eq("other unit is separate", h.ForJob("OTHER 999").Count, 1);
        Eq("unknown unit has nothing", h.ForJob("NOPE 000").Count, 0);
        Eq("jobs listed", h.Jobs().Count, 2);

        string summary = h.Summary("HP450 12345");
        Check("summary flags the repeat", summary.Contains("repeat offender"), summary);
        Check("summary says it has been here before", summary.Contains("been here before"), summary);
        Check("unknown unit summary is explicit", h.Summary("NOPE 000").Contains("Nothing on file"), "wrong text");
        Check("blank job asks for one", h.Summary("").Contains("job strip"), "wrong text");

        History none = History.Load(Path.Combine(dir, "nope"));
        Eq("missing folder reads nothing", none.FilesRead, 0);
        Check("missing folder explains itself", none.Summary("x").Contains("No saved sessions"), "wrong text");

        try { Directory.Delete(dir, true); } catch { }
    }

    // ---------------- user codes ----------------

    static void UserCodeTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "uc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "data"));

        var c = new UserCode
        {
            Brand = "DOOSAN", Code = "F99", Title = "Airend \"hot\" trip",
            Description = "Line one\nLine two", Severity = "high", Controller = "ifix",
            Reset = "Cycle key", Safety = "Blow down first"
        };
        c.Causes.Add("Cooler blocked");
        c.Causes.Add("Low oil");
        c.Checks.Add("Check fan");

        List<UserCode> all = UserCodes.Upsert(UserCodes.Load(root), c);
        Eq("one entry after first upsert", all.Count, 1);
        UserCodes.Save(root, all);
        Check("file written", File.Exists(UserCodes.PathFor(root)), "missing");

        List<UserCode> back = UserCodes.Load(root);
        Eq("round-trip count", back.Count, 1);
        Eq("round-trip code", back[0].Code, "F99");
        Eq("round-trip title with quotes", back[0].Title, "Airend \"hot\" trip");
        Eq("round-trip causes", back[0].Causes.Count, 2);
        Eq("round-trip checks", back[0].Checks.Count, 1);
        Eq("round-trip reset", back[0].Reset, "Cycle key");
        Check("newline preserved", back[0].Description.Contains("Line two"), back[0].Description);

        // Editing the same brand+code replaces rather than duplicating.
        var edited = new UserCode { Brand = "DOOSAN", Code = "F99", Title = "Airend discharge high" };
        List<UserCode> upserted = UserCodes.Upsert(back, edited);
        Eq("upsert replaces, not appends", upserted.Count, 1);
        Eq("upsert kept the new title", upserted[0].Title, "Airend discharge high");

        var other = new UserCode { Brand = "IR", Code = "F99", Title = "Different brand, same number" };
        Eq("different brand is a new entry", UserCodes.Upsert(upserted, other).Count, 2);

        var junk = new UserCode();
        Eq("empty entry rejected", UserCodes.Upsert(upserted, junk).Count, 2);
        Check("empty entry is not usable", !junk.IsUsable(), "claimed usable");

        UserCodes.Save(root, upserted);
        Check("backup kept on overwrite", File.Exists(UserCodes.PathFor(root) + ".bak"), "no .bak");

        string corrupt = Path.Combine(root, "data", "user-codes.json");
        File.WriteAllText(corrupt, "{ this is not json ");
        bool threw = false;
        try { UserCodes.Load(root); }
        catch { threw = true; }
        Check("corrupt user-codes.json throws instead of looking empty", threw, "returned empty list");
        Check("corrupt file left in place so a failed load cannot wipe the shop file",
            File.ReadAllText(corrupt).Contains("not json"), "was rewritten");
        UserCodes.Save(root, upserted);

        // The KB loader has to be able to read what the editor writes.
        var kb = new KbIndex();
        File.WriteAllText(Path.Combine(root, "data", "kb.json"), "{\"codes\":[]}");
        kb.Load(root);
        Eq("editor output loads as KB codes", kb.All.Count, 2);
        Eq("no load errors from generated json", kb.Errors.Count, 0);
        int total;
        var hits = kb.Search("F99", "CODE", 10, out total);
        Check("saved code is searchable", total >= 1 && hits[0].Title.Contains("F99"),
            "total=" + total);

        Eq("SplitLines trims bullets",
            string.Join("|", UserCodes.SplitLines("• one\n- two\n\n  three  ").ToArray()), "one|two|three");
        Eq("quote escapes", UserCodes.Quote("a\"b\\c\nd"), "\"a\\\"b\\\\c\\nd\"");

        try { Directory.Delete(root, true); } catch { }
    }

    // ---------------- snapshot diff ----------------

    static void DiffTests()
    {
        Check("diff without snapshots explains itself",
            SessionIo.Diff(null, null).Contains("Need Snapshot"), "wrong text");

        DateTime t = new DateTime(2026, 5, 1, 10, 0, 0);
        var a = new Snap
        {
            Time = t, Label = "A before", Rpm = 180, Red = true, Amber = true,
            Def = "n/a (not talking)", Coolant = "20 C", Oil = "0 kPa", Batt = "24.0 V", Fuel = "0.00 L/h"
        };
        a.Active.Add("SPN 1761  FMI 9");
        a.Active.Add("SPN 5246  FMI 0");
        a.Prev.Add("SPN 100  FMI 1");

        var b = new Snap
        {
            Time = t.AddMinutes(20), Label = "B after", Rpm = 800, Red = false, Amber = false,
            Def = "50.0%  temp 20 C", Coolant = "90 C", Oil = "400 kPa", Batt = "27.6 V", Fuel = "3.50 L/h"
        };
        b.Active.Add("SPN 5246  FMI 0");
        b.Prev.Add("SPN 100  FMI 1");
        b.Prev.Add("SPN 1761  FMI 9");

        string d = SessionIo.Diff(a, b);
        Check("before/after labels", d.Contains("A before") && d.Contains("B after"), d);
        Check("rpm transition shown", d.Contains("180") && d.Contains("800"), "rpm missing");
        Check("red lamp transition shown", d.Contains("Red     ON  →  off"), "lamp line missing");
        Check("fixed fault listed as gone", Section(d, "Active codes gone").Contains("SPN 1761"), d);
        Check("still-active fault not listed as gone", !Section(d, "Active codes gone").Contains("SPN 5246"), d);
        Check("still-active fault listed as unchanged", Section(d, "Unchanged active").Contains("SPN 5246"), d);
        Check("no new active faults", Section(d, "Active codes new").Contains("(none)"), d);
        // The whole point of the Prev fix: the repaired fault shows up as newly previously-active.
        Check("repaired fault appears in previously-active new",
            Section(d, "Previously-active new").Contains("SPN 1761"), d);
        Check("carried-over previous fault not reported as new",
            !Section(d, "Previously-active new").Contains("SPN 100"), d);
    }

    /// <summary>Text of one labelled block of the diff, up to the next blank-line-separated header.</summary>
    static string Section(string text, string header)
    {
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        var sb = new System.Text.StringBuilder();
        bool inSection = false;
        foreach (string line in lines)
        {
            if (line.StartsWith(header)) { inSection = true; continue; }
            if (!inSection) continue;
            if (!line.StartsWith("  ")) break;
            sb.AppendLine(line);
        }
        return sb.ToString();
    }

    // ---------------- report ----------------

    static void ReportTests()
    {
        var d = new ReportData
        {
            When = new DateTime(2026, 5, 1, 14, 30, 0),
            JobTag = "HP450 12345",
            Adapter = "Cummins Inc. INLINE 7  (CMNSI7)",
            Vin = "1ABC", Sw = "SW9", CompId = "CUMMINS", Hours = "1234.5 h",
            Rpm = 800, Red = true, Amber = true,
            Def = "n/a (not talking)", Coolant = "90 C", Oil = "400 kPa", Batt = "27.6 V", Fuel = "3.5 L/h",
            HistoryNote = "This unit has been here before"
        };
        d.Active.AddRange(Dtcs(5246, 0));
        d.Prev.AddRange(Dtcs(1761, 9));
        d.Markers.Add("09:00:01  CRANK  RPM=180");

        List<string> lines = JobReport.Lines(d);
        string text = string.Join("\n", lines.ToArray());
        Check("has a title", lines[0].Contains("DIAGNOSTIC REPORT"), lines[0]);
        Check("job on the report", text.Contains("HP450 12345"), "job missing");
        Check("hours on the report", text.Contains("1234.5 h"), "hours missing");
        Check("active fault listed", text.Contains("SPN 5246"), "active fault missing");
        Check("previous fault listed", text.Contains("SPN 1761"), "previous fault missing");
        Check("lamp states spelled out", text.Contains("Red Stop ON"), "lamps missing");
        Check("marker carried through", text.Contains("CRANK"), "marker missing");
        Check("history note carried through", text.Contains("been here before"), "history missing");
        Check("signature line present", text.Contains("Signature"), "no signature line");
        Check("states no emissions function was disabled", text.Contains("disabled to produce this report"), "missing disclaimer");
        Check("unset snapshot diff omitted", !text.Contains("Need Snapshot"), "leaked placeholder text");

        var empty = new ReportData();
        string emptyText = JobReport.Text(empty);
        Check("empty report still renders", emptyText.Contains("DIAGNOSTIC REPORT"), "no title");
        Check("empty report says no job", emptyText.Contains("(not entered)"), "no job placeholder");
        Check("empty report marks faults as none", emptyText.Contains("(none)"), "no none marker");
        Check("empty report shows em dash for rpm", emptyText.Contains("RPM:      —"), emptyText);
    }

    static void LampCase(string what, byte b0, bool red, bool amber, bool protect, bool mil)
    {
        var list = new List<Dtc>();
        bool r, a, p, m;
        J1939Decode.ParseDm(new byte[] { b0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, list, out r, out a, out p, out m);
        Check("lamps " + what,
            r == red && a == amber && p == protect && m == mil,
            "red=" + r + " amber=" + a + " protect=" + p + " mil=" + m);
    }

    static byte[] Bytes(string s)
    {
        var b = new byte[s.Length];
        for (int i = 0; i < s.Length; i++) b[i] = (byte)s[i];
        return b;
    }

    static J1939Frame Cm(int sa, int size, int pkts, int pgn)
    {
        return new J1939Frame
        {
            Pgn = 0xEC00, Sa = sa, Da = 255,
            Data = new byte[] { 0x20, (byte)(size & 0xFF), (byte)(size >> 8), (byte)pkts, 0xFF,
                                (byte)(pgn & 0xFF), (byte)((pgn >> 8) & 0xFF), (byte)((pgn >> 16) & 0xFF) }
        };
    }

    static J1939Frame Dt(int sa, int seq, params byte[] payload)
    {
        var d = new byte[8];
        for (int i = 0; i < 8; i++) d[i] = 0xFF;
        d[0] = (byte)seq;
        for (int i = 0; i < payload.Length && i < 7; i++) d[i + 1] = payload[i];
        return new J1939Frame { Pgn = 0xEB00, Sa = sa, Da = 255, Data = d };
    }

    static void BamTests()
    {
        // Happy path: 2-packet BAM of 12 bytes.
        var bam = new BamAssembler();
        J1939Frame outF;
        Check("TP.CM alone is not a message", !bam.Feed(Cm(0, 12, 2, 0xFECA), out outF), "returned true");
        Check("first TP.DT incomplete", !bam.Feed(Dt(0, 1, 1, 2, 3, 4, 5, 6, 7), out outF), "returned true");
        bool done = bam.Feed(Dt(0, 2, 8, 9, 10, 11, 12), out outF);
        Check("second TP.DT completes", done, "returned false");
        if (done)
        {
            Eq("assembled PGN", outF.Pgn.ToString("X4"), "FECA");
            Eq("assembled length", outF.Data.Length, 12);
            Eq("last byte", outF.Data[11], (byte)12);
        }

        // Regression: a repeated sequence number must not count toward completion.
        var bam2 = new BamAssembler();
        bam2.Feed(Cm(0, 12, 2, 0xFECA), out outF);
        bam2.Feed(Dt(0, 1, 1, 2, 3, 4, 5, 6, 7), out outF);
        Check("duplicate TP.DT does not complete a partial BAM",
            !bam2.Feed(Dt(0, 1, 1, 2, 3, 4, 5, 6, 7), out outF), "emitted a truncated message");
        Check("real second packet still completes it",
            bam2.Feed(Dt(0, 2, 8, 9, 10, 11, 12), out outF), "never completed");

        // Out-of-range sequence numbers are ignored rather than counted.
        var bam3 = new BamAssembler();
        bam3.Feed(Cm(0, 12, 2, 0xFECA), out outF);
        Check("seq 0 ignored", !bam3.Feed(Dt(0, 0, 1, 2, 3), out outF), "accepted seq 0");
        Check("seq past packet count ignored", !bam3.Feed(Dt(0, 9, 1, 2, 3), out outF), "accepted seq 9");

        // A single-frame PGN passes straight through.
        var bam4 = new BamAssembler();
        var plain = new J1939Frame { Pgn = 0xF004, Sa = 0, Da = 255, Data = new byte[8] };
        Check("normal frame passes through", bam4.Feed(plain, out outF) && outF == plain, "was swallowed");

        // TP.DT with no TP.CM must not throw or emit.
        var bam5 = new BamAssembler();
        Check("orphan TP.DT ignored", !bam5.Feed(Dt(0, 1, 1, 2, 3), out outF), "emitted something");
    }

    static void KbTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "kbtest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "data"));
        File.WriteAllText(Path.Combine(root, "data", "kb.json"), @"{""codes"":[
          {""brand_id"":""DOOSAN"",""code"":""F68"",""title"":""Discharge temperature high"",
           ""description"":""Airend discharge over limit"",""likely_causes"":[""Low oil""],
           ""checks"":[""Check cooler""],""severity"":""high"",""controller_id"":""ifix"",""tags"":[""heat""]},
          {""brand_id"":""DOOSAN"",""code"":""F12"",""title"":""Sensor fault, see also F68 for heat"",
           ""description"":""Mentions F68 in the text"",""likely_causes"":[],""checks"":[],
           ""severity"":""low"",""controller_id"":""ifix"",""tags"":[]},
          {""brand_id"":""IR"",""code"":""F680"",""title"":""Unrelated longer code"",
           ""description"":"""",""likely_causes"":[],""checks"":[],""severity"":""low"",
           ""controller_id"":""xe"",""tags"":[]}]}");
        // Deliberately corrupt: this file used to take the whole index down with it.
        File.WriteAllText(Path.Combine(root, "data", "rental-equipment-info.json"), "{ this is not json ");
        File.WriteAllText(Path.Combine(root, "data", "usb-manuals.json"),
            @"{""documents"":[{""document_name"":""HP450 service manual"",""manufacturer"":""Doosan"",
              ""local_path"":""E:\\manuals\\hp450.pdf"",""filename"":""hp450.pdf"",""category"":""service""}]}");

        var kb = new KbIndex();
        kb.Load(root);
        Console.WriteLine("  status: " + kb.Status);
        Eq("codes + manual still loaded despite one bad file", kb.All.Count, 4);
        Check("bad file is reported", kb.Errors.Count == 1 && kb.Errors[0].StartsWith("rental-equipment-info.json"),
            "errors=" + string.Join(" | ", kb.Errors.ToArray()));

        int total;
        var hits = kb.Search("F68", "ALL", KbIndex.MaxResults, out total);
        Eq("F68 matches three entries", total, 3);
        Check("exact code F68 ranks first", hits.Count > 0 && hits[0].Title.Contains("F68  —  Discharge"),
            "first was: " + (hits.Count > 0 ? hits[0].Title : "(nothing)"));
        Check("prefix match F680 outranks a mere mention",
            hits.Count > 2 && hits[1].Title.Contains("F680"),
            "order: " + string.Join(" / ", Titles(hits)));

        hits = kb.Search("doosan discharge", "ALL", KbIndex.MaxResults, out total);
        Check("multi-token AND search works", total == 1 && hits[0].Kind == "CODE",
            "total=" + total);

        hits = kb.Search("hp450", "MANUAL", KbIndex.MaxResults, out total);
        Eq("kind filter works", total, 1);

        hits = kb.Search("F68", "CODE", 1, out total);
        Eq("total counts all matches even when capped", total, 3);
        Eq("cap respected", hits.Count, 1);

        hits = kb.Search("", "ALL", KbIndex.MaxResults, out total);
        Eq("empty query lists everything", total, 4);

        hits = kb.Search("nothingmatchesthis", "ALL", KbIndex.MaxResults, out total);
        Eq("no hits", total, 0);

        // Worst case: the primary codes file is the corrupt one. The old loader aborted the whole Load
        // on the first exception, so everything after it was lost.
        File.WriteAllText(Path.Combine(root, "data", "kb.json"), "{ broken");
        var partial = new KbIndex();
        partial.Load(root);
        Eq("manuals survive a corrupt kb.json", partial.All.Count, 1);
        Eq("both bad files reported", partial.Errors.Count, 2);
        Check("status names the failures", partial.Status.Contains("kb.json"), partial.Status);

        var empty = new KbIndex();
        empty.Load(Path.Combine(root, "does-not-exist"));
        Eq("missing root loads nothing without throwing", empty.All.Count, 0);
        Check("missing root explains itself", empty.Status.Contains("nothing loaded"), empty.Status);

        try { Directory.Delete(root, true); } catch { }
    }

    static string[] Titles(List<Hit> hits)
    {
        var s = new string[hits.Count];
        for (int i = 0; i < hits.Count; i++) s[i] = hits[i].Title;
        return s;
    }

    static void SettingsTests()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tb-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = AppSettings.PathName(dir);
        var s = new AppSettings();
        s.Model = "HP450WCU";
        s.Serial = "123456";
        s.X = 40;
        s.Y = 50;
        s.Width = 1280;
        s.Height = 800;
        s.Maximized = true;
        s.SaveTo(path);
        var loaded = AppSettings.LoadFrom(path);
        Eq("settings model", loaded.Model, "HP450WCU");
        Eq("settings serial", loaded.Serial, "123456");
        Eq("settings width", loaded.Width, 1280);
        Eq("settings height", loaded.Height, 800);
        Eq("settings maximized", loaded.Maximized, true);
        Eq("missing settings file is empty", AppSettings.LoadFrom(Path.Combine(dir, "nope.json")).Model, "");
        File.WriteAllText(path, "{ this is not json");
        Eq("corrupt settings file does not throw", AppSettings.LoadFrom(path).Serial, "");
        try { Directory.Delete(dir, true); } catch { }
    }

    static void UpdaterTests()
    {
        Eq("stamped version", AppVersion.Number, "1.1.0");
        Version parsed;
        Check("current version parses", Updater.TryParseVersion(AppVersion.Number, out parsed), "parse failed");
        Check("1.2.0 is newer", Updater.IsNewer("1.2.0", "1.1.0"), "1.2.0 vs 1.1.0");
        Check("v1.1.1 is newer", Updater.IsNewer("v1.1.1", "1.1.0"), "v prefix");
        Check("same version is not newer", !Updater.IsNewer("1.1.0", "1.1.0"), "same");
        Check("older is not newer", !Updater.IsNewer("1.0.9", "1.1.0"), "older");
        Check("garbage version is not newer", !Updater.IsNewer("nope", "1.1.0"), "garbage");

        var m = Updater.ParseManifest(
            @"{""version"":""1.2.0"",""sha256"":""0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"",""url"":""https://example.com/TechBench.exe"",""notes"":""fix""}");
        Check("manifest parses", m != null, "null");
        if (m != null)
        {
            Eq("manifest version", m.Version, "1.2.0");
            Eq("manifest url", m.Url, "https://example.com/TechBench.exe");
            Eq("manifest notes", m.Notes, "fix");
            Eq("manifest hash length", m.Sha256.Length, 64);
        }
        Check("spaced hash normalizes",
            Updater.NormalizeHash("AB CD") == "abcd", Updater.NormalizeHash("AB CD"));
        Check("short hash rejected",
            Updater.ParseManifest(@"{""version"":""1"",""sha256"":""abc"",""url"":""http://x""}") == null, "accepted short hash");
        Check("missing url rejected",
            Updater.ParseManifest(@"{""version"":""1.2.0"",""sha256"":""0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef""}") == null,
            "accepted");

        byte[] payload = Encoding.UTF8.GetBytes("tech-bench-update-bytes");
        string hash = Updater.Sha256Bytes(payload);
        Eq("sha256 hex length", hash.Length, 64);
        string dir = Path.Combine(Path.GetTempPath(), "tb-upd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "blob.bin");
        File.WriteAllBytes(file, payload);
        Check("file hash matches bytes", Updater.HashMatches(file, hash), Updater.Sha256File(file));
        Check("wrong hash fails", !Updater.HashMatches(file, "ff" + hash.Substring(2)), "matched");

        Updater.Stage(dir, payload, hash);
        Check("staged pending verifies", Updater.HasVerifiedPending(dir), "missing sidecar or mismatch");
        File.WriteAllText(Path.Combine(dir, Updater.HashSidecar), "0");
        Check("tampered sidecar fails", !Updater.HasVerifiedPending(dir), "still verified");
        Check("cannot apply mid-session", !Updater.CanApplyNow(true), "allowed");
        Check("can apply when idle", Updater.CanApplyNow(false), "blocked");
        string script = Updater.ApplyScript();
        Check("cmd waits for process", script.Contains("TechBench.exe") && script.Contains(":wait"), "no wait");
        Check("cmd swaps hashed exe", script.Contains("TechBench.exe.new") && script.Contains("move /Y"), "no swap");
        Check("cmd never mentions a token",
            script.IndexOf("ghp_", StringComparison.OrdinalIgnoreCase) < 0
            && script.IndexOf("Authorization", StringComparison.OrdinalIgnoreCase) < 0, "token");
        Check("default feed is public HTTPS",
            Updater.DefaultManifestUrl.StartsWith("https://"), Updater.DefaultManifestUrl);
        try { Directory.Delete(dir, true); } catch { }
    }

    static string MkRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "tb-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "data"));
        File.WriteAllText(Path.Combine(root, "data", "kb.json"), "{\"codes\":[]}");
        return root;
    }

    static UserCode Code(string brand, string code, string title)
    {
        return new UserCode { Brand = brand, Code = code, Title = title };
    }

    static void ShopSyncTests()
    {
        string prev = ShopSync.SettingsFolderOverride;
        string settings = Path.Combine(Path.GetTempPath(), "tb-sync-set-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(settings);
        ShopSync.SettingsFolderOverride = settings;
        try
        {
            Eq("sanitize drops junk", ShopSync.SanitizeTechId("Jeremy S."), "Jeremy-S");
            Eq("sanitize empty", ShopSync.SanitizeTechId("@@@"), "tech");

            string kbA = MkRoot();
            string kbB = MkRoot();
            string usb = Path.Combine(Path.GetTempPath(), "tb-usb-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(usb);

            var alice = new ShopSyncSettings { TechId = "alice", SyncFolder = usb };
            var bob = new ShopSyncSettings { TechId = "bob", SyncFolder = usb };

            ShopSync.SaveUserCode(kbA, "alice", Code("DOOSAN", "F99", "Alice cooler note"));
            ShopSync.SaveNote(kbA, "alice", "Tank header", "Check the 4-pin first.");
            string pic = Path.Combine(kbA, "alice.png");
            File.WriteAllText(pic, "png-bytes");
            ShopSync.AddFile(kbA, "alice", pic);

            ShopSyncResult a1 = ShopSync.Run(kbA, alice);
            Check("alice pushes new shop files", a1.Pushed > 0, "pushed=" + a1.Pushed);
            Eq("alice has no conflict on first push", a1.Conflicts, 0);

            ShopSync.SaveUserCode(kbB, "bob", Code("IR", "F12", "Bob sensor note"));
            ShopSyncResult b1 = ShopSync.Run(kbB, bob);
            Check("bob pulls alice and pushes his", b1.Pulled > 0 && b1.Pushed > 0,
                "pulled=" + b1.Pulled + " pushed=" + b1.Pushed);
            Eq("bob first pass no file conflict", b1.Conflicts, 0);

            ShopSyncResult a2 = ShopSync.Run(kbA, alice);
            Check("alice pulls bob's shard", a2.Pulled > 0, "pulled=" + a2.Pulled);

            var kb = new KbIndex();
            kb.Load(kbA);
            int total;
            Check("alice KB has bob's code after pull",
                kb.Search("F12", "CODE", 10, out total).Count >= 1, "total=" + total);
            Check("alice KB has her own code",
                kb.Search("F99", "CODE", 10, out total).Count >= 1, "total=" + total);
            Check("note is searchable",
                kb.Search("Tank header", "NOTE", 10, out total).Count >= 1, kb.Status);
            Check("note also matches its file name",
                kb.Search("Tank-header", "NOTE", 10, out total).Count >= 1, "total=" + total);
            Check("file is searchable",
                kb.Search("alice.png", "FILE", 10, out total).Count >= 1, kb.Status);

            // Same file, both sides edited after a clean sync → conflict, not last-write-wins.
            string noteA = Path.Combine(ShopSync.ShardDir(kbA, "alice"), "notes");
            string[] notes = Directory.GetFiles(noteA, "*.txt");
            Check("alice has a note file", notes.Length > 0, "none");
            string relNote = Path.GetFileName(notes[0]);
            ShopSync.Run(kbA, alice);
            ShopSync.Run(kbB, bob);
            string aliceNote = notes[0];
            string usbNote = Path.Combine(usb, "data", "shop", "alice", "notes", relNote);
            Check("usb has alice's note", File.Exists(usbNote), usbNote);
            File.WriteAllText(aliceNote, "Alice rewrite");
            File.WriteAllText(usbNote, "Bob rewrite of alice note");
            ShopSyncResult clash = ShopSync.Run(kbA, alice);
            Check("both-changed note is a conflict", clash.Conflicts >= 1, "conflicts=" + clash.Conflicts);
            Check("alice's text not silently overwritten",
                File.ReadAllText(aliceNote).Contains("Alice rewrite"), File.ReadAllText(aliceNote));
            Check("sync-folder text not silently overwritten",
                File.ReadAllText(usbNote).Contains("Bob rewrite"), File.ReadAllText(usbNote));

            ShopConflict noteClash = null;
            foreach (ShopConflict c in clash.ConflictList)
                if (c.Kind == "note" || (c.RelativePath ?? "").IndexOf(relNote, StringComparison.OrdinalIgnoreCase) >= 0)
                    noteClash = c;
            Check("conflict names the note", noteClash != null, "missing");
            if (noteClash != null)
            {
                ShopSync.Resolve(kbA, noteClash, "local");
                ShopSyncResult after = ShopSync.Run(kbA, alice);
                bool still = false;
                foreach (ShopConflict c in after.ConflictList)
                    if ((c.RelativePath ?? "").IndexOf(relNote, StringComparison.OrdinalIgnoreCase) >= 0) still = true;
                Check("keep mine clears that file conflict", !still, "still listed");
            }

            // Record-level: same brand+code, different text in two shards.
            string kbC = MkRoot();
            ShopSync.SaveUserCode(kbC, "alice", Code("DOOSAN", "F50", "Alice title"));
            ShopSync.SaveUserCode(kbC, "bob", Code("DOOSAN", "F50", "Bob title"));
            var rec = new ShopSyncResult();
            ShopSync.CollectCodeConflicts(kbC, rec);
            Eq("same code different text is a conflict", rec.Conflicts, 1);
            ShopSync.Resolve(kbC, rec.ConflictList[0], "local");
            var rec2 = new ShopSyncResult();
            ShopSync.CollectCodeConflicts(kbC, rec2);
            Eq("keep mine resolves the code", rec2.Conflicts, 0);
            var kbResolved = new KbIndex();
            kbResolved.Load(kbC);
            var f50 = kbResolved.Search("F50", "CODE", 10, out total);
            Check("resolved title is Alice's",
                f50.Count == 1 && f50[0].Title.Contains("Alice title"),
                "count=" + total);

            ShopSync.SaveUserCode(kbC, "alice", Code("DOOSAN", "F51", "A"));
            ShopSync.SaveUserCode(kbC, "bob", Code("DOOSAN", "F51", "B"));
            var rec3 = new ShopSyncResult();
            ShopSync.CollectCodeConflicts(kbC, rec3);
            Eq("F51 is a conflict", rec3.Conflicts, 1);
            ShopSync.Resolve(kbC, rec3.ConflictList[0], "both");
            var rec4 = new ShopSyncResult();
            ShopSync.CollectCodeConflicts(kbC, rec4);
            Eq("keep both stops nagging", rec4.Conflicts, 0);
            var kbBoth = new KbIndex();
            kbBoth.Load(kbC);
            Eq("keep both still shows two F51 hits", kbBoth.Search("F51", "CODE", 10, out total).Count, 2);

            // Shared tree (no USB): does not copy out of the KB.
            string kbShare = MkRoot();
            ShopSync.SaveUserCode(kbShare, "alice", Code("DOOSAN", "F1", "shared"));
            ShopSyncResult share = ShopSync.Run(kbShare, new ShopSyncSettings { TechId = "alice", SyncFolder = "" });
            Check("shared-tree mode", share.SharedTree, "not shared");
            Eq("shared tree does not push to a folder", share.Pushed, 0);

            // Legacy user-codes.json is imported into the shard once.
            string kbLeg = MkRoot();
            UserCodes.Save(kbLeg, new List<UserCode> { Code("DOOSAN", "LEG", "Old file") });
            ShopSync.Run(kbLeg, new ShopSyncSettings { TechId = "pat", SyncFolder = "" });
            Check("legacy copied into shard", File.Exists(ShopSync.ShardCodesPath(kbLeg, "pat")), "missing shard");
            var kbLegIdx = new KbIndex();
            kbLegIdx.Load(kbLeg);
            Eq("legacy code not duplicated in search", kbLegIdx.Search("LEG", "CODE", 10, out total).Count, 1);

            // History publish / import: new csv copies, same name different bytes is left alone.
            string histSrc = Path.Combine(kbA, "job_dtcs.csv");
            File.WriteAllText(histSrc, "time,job,state,spn,fmi,occurrences,name,fmi_text\n2026,HP,active,1,0,1,x,x\n");
            ShopSync.PublishHistory(kbA, "alice", histSrc);
            string sessions = Path.Combine(Path.GetTempPath(), "tb-sess-" + Guid.NewGuid().ToString("N"));
            Eq("import copies missing history", ShopSync.ImportHistory(kbA, sessions), 1);
            Eq("import is idempotent", ShopSync.ImportHistory(kbA, sessions), 0);
            File.WriteAllText(Path.Combine(sessions, "job_dtcs.csv"), "local-only");
            File.WriteAllText(Path.Combine(ShopSync.ShardDir(kbA, "alice"), "history", "job_dtcs.csv"), "shop-other");
            Eq("import does not last-write-wins session csv", ShopSync.ImportHistory(kbA, sessions), 0);
            Eq("local session csv kept", File.ReadAllText(Path.Combine(sessions, "job_dtcs.csv")), "local-only");

            try { Directory.Delete(kbA, true); } catch { }
            try { Directory.Delete(kbB, true); } catch { }
            try { Directory.Delete(kbC, true); } catch { }
            try { Directory.Delete(kbShare, true); } catch { }
            try { Directory.Delete(kbLeg, true); } catch { }
            try { Directory.Delete(usb, true); } catch { }
            try { Directory.Delete(sessions, true); } catch { }
        }
        finally
        {
            ShopSync.SettingsFolderOverride = prev;
            try { Directory.Delete(settings, true); } catch { }
        }
    }
}
