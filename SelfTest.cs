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
        AftertreatmentTests();
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
        Console.WriteLine("== shipped field database ==");
        BundledKbTests();

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
        Console.WriteLine("== J1939 DM11/DM3 code clear ==");
        ClearCodesTests();

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

        PersistStartupTests();

        Console.WriteLine();
        Console.WriteLine("== updater ==");
        UpdaterTests();

        Console.WriteLine();
        Console.WriteLine("== windows installer ==");
        InstallerTests();

        Console.WriteLine();
        Console.WriteLine("== shop two-way sync ==");
        ShopSyncTests();

        Console.WriteLine();
        Console.WriteLine("== work orders / IntelliDealer gateway ==");
        WorkOrderTests();

        Console.WriteLine();
        Console.WriteLine("== crash log (TB1d) ==");
        CrashFormTests();

        Console.WriteLine();
        Console.WriteLine("== data safety ==");
        DataSafetyTests();

        Console.WriteLine();
        Console.WriteLine("== open / launch policy ==");
        OpenTests();

        Console.WriteLine();
        Console.WriteLine("== source list ==");
        SourceListTests();

        Console.WriteLine();
        Console.WriteLine("== install smoke ==");
        SmokeTests();

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
        Check("silent tank leaves level as not available",
            m.AftText().IndexOf("DEF level SPN 1761: not available", StringComparison.Ordinal) >= 0, m.AftText());

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
        Check("disconnected snapshot restores no-data aftertreatment",
            s.AftText.IndexOf("no data", StringComparison.Ordinal) >= 0, s.AftText);
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

    static void AftertreatmentTests()
    {
        AftState empty = J1939Decode.ParseAftertreatment(null);
        Eq("no frame is no data", empty.Level, AftState.NoData);
        Eq("no frame temp is no data", empty.Temp, AftState.NoData);
        Eq("no frame inducement is no data", empty.Severity, AftState.NoData);
        Eq("no frame low-level is no data", empty.LowLamp, AftState.NoData);
        Check("panel names SPN 1761", empty.Text().IndexOf("SPN 1761", StringComparison.Ordinal) >= 0, empty.Text());
        Check("panel names SPN 5246", empty.Text().IndexOf("SPN 5246", StringComparison.Ordinal) >= 0, empty.Text());

        AftState shortMsg = J1939Decode.ParseAftertreatment(new byte[] { 125, 60 });
        Eq("short tank level", shortMsg.Level, "50.0%");
        Eq("short tank temp", shortMsg.Temp, "20 C");
        Eq("short tank leaves inducement no data", shortMsg.Severity, AftState.NoData);
        Eq("short tank leaves low-level no data", shortMsg.LowLamp, AftState.NoData);

        byte low = (byte)(1 << 5);
        byte severe = (byte)(5 << 5);
        AftState full = J1939Decode.ParseAftertreatment(new byte[] { 125, 60, 0xFF, 0xFF, low, severe });
        Eq("full tank level", full.Level, "50.0%");
        Eq("full tank temp", full.Temp, "20 C");
        Eq("SPN 5245 low lamp", full.LowLamp, "on solid — DEF low");
        Eq("SPN 5246 final inducement", full.Severity, "level 5 — final inducement");

        AftState na = J1939Decode.ParseAftertreatment(new byte[] { 0xFF, 0xFF });
        Eq("NA level slot", na.Level, "not available");
        Eq("NA temp slot", na.Temp, "not available");
        Eq("NA short message still has no inducement byte", na.Severity, AftState.NoData);

        Eq("full tank SeverityRaw", full.SeverityRaw, 5);
        Eq("full tank LowLampRaw", full.LowLampRaw, 1);
        Eq("band severe severity 5", AftState.AttentionBand(5, 0), 2);
        Eq("band severe severity 4", AftState.AttentionBand(4, -1), 2);
        Eq("band severe lamp 4", AftState.AttentionBand(0, 4), 2);
        Eq("band warn severity 1", AftState.AttentionBand(1, 0), 1);
        Eq("band warn severity 3", AftState.AttentionBand(3, -1), 1);
        Eq("band warn lamp 1", AftState.AttentionBand(0, 1), 1);
        Eq("band quiet zeros", AftState.AttentionBand(0, 0), 0);
        Eq("band quiet missing", AftState.AttentionBand(-1, -1), 0);
        Eq("band quiet not available", AftState.AttentionBand(7, 7), 0);

        Eq("5246 not active", J1939Decode.Severity5246(0), "not active");
        Eq("5246 not available code", J1939Decode.Severity5246(7), "not available");
        Eq("5245 fast blink", J1939Decode.LowLevel5245(4), "fast blink — DEF lower");

        var mon = new BusMonitor();
        Check("monitor starts at no data",
            mon.AftText().IndexOf("DEF level SPN 1761: no data", StringComparison.Ordinal) >= 0, mon.AftText());
        DateTime t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        mon.Feed(F(0xFE56, 0, 125, 60, 0xFF, 0xFF, low, severe, 0, 0), t);
        Check("monitor shows 50 percent",
            mon.AftText().IndexOf("DEF level SPN 1761: 50.0%", StringComparison.Ordinal) >= 0, mon.AftText());
        Check("monitor shows final inducement",
            mon.AftText().IndexOf("level 5 — final inducement", StringComparison.Ordinal) >= 0, mon.AftText());
        mon.ClearLive();
        Check("clear live returns no data",
            mon.AftText().IndexOf("SCR inducement SPN 5246: no data", StringComparison.Ordinal) >= 0, mon.AftText());
    }

    static string DestList(int engineSa, params int[] modules)
    {
        byte[] d = J1939Clear.Destinations(engineSa, modules);
        var parts = new string[d.Length];
        for (int i = 0; i < d.Length; i++) parts[i] = d[i].ToString();
        return string.Join(",", parts);
    }

    static J1939Frame AckFrame(int sa, int control, int pgn, int da, int address)
    {
        return new J1939Frame
        {
            Pgn = J1939Clear.Ack, Sa = sa, Da = da,
            Data = new byte[] { (byte)control, 0xFF, 0xFF, 0xFF, (byte)address,
                (byte)(pgn & 0xFF), (byte)((pgn >> 8) & 0xFF), (byte)((pgn >> 16) & 0xFF) }
        };
    }

    /// <summary>DM1/DM2 payload: lamps, reserved, then SPN/FMI/occ per code (or the empty pattern).</summary>
    static J1939Frame DmFrame(int pgn, int sa, byte lamps, List<int[]> codes)
    {
        var data = new List<byte>();
        data.Add(lamps);
        data.Add(0xFF);
        if (codes == null || codes.Count == 0)
            data.AddRange(new byte[] { 0, 0, 0, 0, 0xFF, 0xFF });
        else
            foreach (int[] c in codes)
            {
                data.Add((byte)(c[0] & 0xFF));
                data.Add((byte)((c[0] >> 8) & 0xFF));
                data.Add((byte)((((c[0] >> 16) & 0x07) << 5) | (c[1] & 0x1F)));
                data.Add(1);
            }
        while (data.Count < 8) data.Add(0xFF);
        return new J1939Frame { Pgn = pgn, Sa = sa, Da = 255, Data = data.ToArray() };
    }

    static List<int[]> Codes(params int[] spnFmi)
    {
        var list = new List<int[]>();
        for (int i = 0; i + 1 < spnFmi.Length; i += 2) list.Add(new[] { spnFmi[i], spnFmi[i + 1] });
        return list;
    }

    /// <summary>One ECM on the simulated bus: what it holds and how it answers a clear.</summary>
    sealed class SimModule
    {
        public int Sa;
        /// <summary>ACK control for a directed DM11 / DM3; -1 = stays silent.</summary>
        public int Dm11Control = J1939Clear.AckPositive;
        public int Dm3Control = J1939Clear.AckPositive;
        public List<int[]> Active = new List<int[]>();
        public List<int[]> Prev = new List<int[]>();
        /// <summary>Codes that are still true, so they re-latch right after a DM11.</summary>
        public List<int[]> StillTrue = new List<int[]>();
        public bool AnswersDm1 = true;
        public bool AnswersDm2 = true;
        /// <summary>FE56 payload this module sends when asked, or null.</summary>
        public byte[] Def;
        public byte[] DefAfterClear;
        /// <summary>Sends some frame every pump step, so it counts as on the bus.</summary>
        public bool Talks = true;

        public byte Lamps()
        {
            foreach (int[] c in Active) if (c[0] == 5246) return 0x10;
            return Active.Count > 0 ? (byte)0x04 : (byte)0x00;
        }
    }

    /// <summary>IClearLink over a capture-mode Rp1210 and scripted ECMs, on a fake clock.</summary>
    sealed class SimLink : IClearLink
    {
        public DateTime Clock = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);
        public readonly Rp1210 Rp = new Rp1210();
        public readonly BusMonitor Mon = new BusMonitor();
        public readonly List<SimModule> Modules = new List<SimModule>();
        public int DropAtMs = -1;
        public int PumpedMs;
        public string LastProgress = "";
        readonly List<J1939Frame> _pending = new List<J1939Frame>();

        public SimLink() { Rp.Capture = new List<J1939Tx>(); }

        public DateTime Now { get { return Clock; } }
        public string LastError { get { return "simulated reject"; } }
        public List<J1939Tx> Sent { get { return Rp.Capture; } }
        public void Progress(string what) { LastProgress = what; }

        public SimModule Add(int sa)
        {
            var m = new SimModule { Sa = sa };
            Modules.Add(m);
            return m;
        }

        /// <summary>Put what the modules hold onto the monitor, as a live hookup would have.</summary>
        public void Prime()
        {
            foreach (SimModule m in Modules)
            {
                if (m.Talks) Mon.Feed(new J1939Frame { Pgn = 0xFEEE, Sa = m.Sa, Da = 255, Data = new byte[8] }, Clock);
                if (m.AnswersDm1) Mon.Feed(DmFrame(J1939Clear.Dm1, m.Sa, m.Lamps(), m.Active), Clock);
                if (m.AnswersDm2) Mon.Feed(DmFrame(J1939Clear.Dm2, m.Sa, 0, m.Prev), Clock);
                if (m.Def != null) Mon.Feed(new J1939Frame { Pgn = J1939Clear.DefTank, Sa = m.Sa, Da = 255, Data = m.Def }, Clock);
            }
            Clock = Clock.AddSeconds(1);
        }

        public bool Send(J1939Tx tx)
        {
            if (!Rp.SendJ1939(tx)) return false;
            int req = tx.RequestedPgn;
            if (req < 0) return true;
            foreach (SimModule m in Modules)
            {
                bool direct = tx.Dest == m.Sa;
                if (!direct && tx.Dest != J1939Clear.Broadcast) continue;
                if (req == J1939Clear.Dm11 || req == J1939Clear.Dm3)
                {
                    int control = req == J1939Clear.Dm11 ? m.Dm11Control : m.Dm3Control;
                    if (control < 0) continue;
                    if (control == J1939Clear.AckPositive)
                    {
                        if (req == J1939Clear.Dm11)
                        {
                            m.Active = new List<int[]>(m.StillTrue);
                            if (m.DefAfterClear != null) m.Def = m.DefAfterClear;
                        }
                        else m.Prev.Clear();
                    }
                    // A global request is never acknowledged.
                    if (direct) _pending.Add(AckFrame(m.Sa, control, req, J1939Clear.Broadcast, J1939Clear.ToolSa));
                }
                else if (req == J1939Clear.Dm1 && m.AnswersDm1)
                    _pending.Add(DmFrame(J1939Clear.Dm1, m.Sa, m.Lamps(), m.Active));
                else if (req == J1939Clear.Dm2 && m.AnswersDm2)
                    _pending.Add(DmFrame(J1939Clear.Dm2, m.Sa, 0, m.Prev));
                else if (req == J1939Clear.DefTank && m.Def != null)
                    _pending.Add(new J1939Frame { Pgn = J1939Clear.DefTank, Sa = m.Sa, Da = 255, Data = m.Def });
            }
            return true;
        }

        public bool Pump(int ms, Func<bool> done)
        {
            const int step = 25;
            for (int t = 0; t < ms; t += step)
            {
                Clock = Clock.AddMilliseconds(step);
                PumpedMs += step;
                if (DropAtMs >= 0 && PumpedMs >= DropAtMs) return false;
                foreach (J1939Frame f in _pending) Mon.Feed(f, Clock);
                _pending.Clear();
                foreach (SimModule m in Modules)
                    if (m.Talks) Mon.Feed(new J1939Frame { Pgn = 0xFEEE, Sa = m.Sa, Da = 255, Data = new byte[8] }, Clock);
                if (done != null && done()) return true;
            }
            return true;
        }
    }

    static bool Has(string text, string what)
    {
        return text != null && text.IndexOf(what, StringComparison.Ordinal) >= 0;
    }

    static string Lf(string text)
    {
        return text == null ? "" : text.Replace("\r\n", "\n");
    }

    static void ClearCodesTests()
    {
        Eq("clear dests cover engine + compressor + broadcast", DestList(0), "0,48,255");
        Eq("clear dests include detected engine SA 17", DestList(17), "0,17,48,255");
        Eq("compressor as engine SA is not duplicated", DestList(48), "0,48,255");
        Eq("unknown engine SA still hits 0 and 48", DestList(-1), "0,48,255");
        Eq("broadcast engine SA rejected from dest list", DestList(255), "0,48,255");
        Eq("modules reporting faults get their own clear", DestList(0, 61, 48, 0), "0,48,61,255");
        Eq("tool, null, global, and junk SAs are not module dests", DestList(0, 249, 254, 255, 300, -4), "0,48,255");
        Eq("directed part drops global", string.Join(",",
            J1939Clear.Directed(J1939Clear.Destinations(17, new[] { 61 })).ConvertAll(b => b.ToString()).ToArray()), "0,17,48,61");

        // DM11 is a Request (PGN 59904) carrying the DM11 PGN, never DM11 sent as data.
        J1939Tx reqDm11 = J1939Clear.RequestTx(J1939Clear.Dm11, J1939Clear.CompressorSa);
        byte[] wire = reqDm11.Rp1210;
        Eq("request wire length (6 header + 3 PGN)", wire.Length, 9);
        Eq("request PGN LSB EA00", wire[0], (byte)0x00);
        Eq("request PGN PF", wire[1], (byte)0xEA);
        Eq("request PGN msb", wire[2], (byte)0x00);
        Eq("request priority 6", wire[3], (byte)6);
        Eq("request from tool SA F9", wire[4], (byte)0xF9);
        Eq("request dest SA 48", wire[5], (byte)48);
        Eq("request payload DM11 LSB", wire[6], (byte)0xD3);
        Eq("request payload DM11 mid", wire[7], (byte)0xFE);
        Eq("request payload DM11 msb", wire[8], (byte)0x00);
        Eq("requested PGN read back", reqDm11.RequestedPgn, J1939Clear.Dm11);

        Eq("PDU1 PGN with dest byte normalized (ACK)", J1939Decode.NormalizePgn(0xE8F9), 0xE800);
        Eq("PDU1 PGN with dest byte normalized (TP.CM)", J1939Decode.NormalizePgn(0xECFF), 0xEC00);
        Eq("PDU2 PGN kept (DM1)", J1939Decode.NormalizePgn(0xFECA), 0xFECA);
        Eq("data page kept", J1939Decode.NormalizePgn(0x1E8F9), 0x1E800);

        Eq("tool NAME function is off-board service tool (129)", Rp1210.ToolName[5], (byte)129);
        Check("tool NAME is arbitrary-address capable", (Rp1210.ToolName[7] & 0x80) != 0, "bit 63 clear");

        // Acknowledgment decode.
        AckReply ack;
        DateTime t0 = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);
        Check("ACK parsed", J1939Clear.TryParseAck(AckFrame(0, 0, J1939Clear.Dm11, 255, 0xF9), t0, out ack), "not parsed");
        Eq("ACK control positive", ack.Control, J1939Clear.AckPositive);
        Eq("ACK names DM11", ack.Pgn, J1939Clear.Dm11);
        Check("ACK to global with tool address is ours", ack.ForTool, "not ours");
        J1939Clear.TryParseAck(AckFrame(0, 1, J1939Clear.Dm3, 0xF9, 0xFF), t0, out ack);
        Check("NACK sent to the tool is ours", ack.ForTool, "not ours");
        Eq("NACK control", ack.Control, J1939Clear.AckNegative);
        J1939Clear.TryParseAck(AckFrame(0, 0, J1939Clear.Dm11, 255, 0xFA), t0, out ack);
        Check("ACK for another tool (SA FA) ignored", !ack.ForTool, "taken");
        J1939Clear.TryParseAck(AckFrame(0, 0, J1939Clear.Dm11, 255, 0xFF), t0, out ack);
        Check("pre-2006 ACK (address FF) accepted", ack.ForTool, "rejected");
        Check("short ACK rejected", !J1939Clear.TryParseAck(new J1939Frame { Pgn = 0xE800, Sa = 0, Da = 255, Data = new byte[3] }, t0, out ack), "parsed");

        var ackMon = new BusMonitor();
        J1939Frame rawAck = AckFrame(0, 0, J1939Clear.Dm11, 0xF9, 0xF9);
        rawAck.Pgn = 0xE8F9; // driver left the destination in the PGN
        ackMon.Feed(rawAck, t0);
        Check("monitor routes an un-normalized ACK", ackMon.AckFrom(0, J1939Clear.Dm11, t0) != null, "missed");
        Check("ACK lookup honors the since time", ackMon.AckFrom(0, J1939Clear.Dm11, t0.AddMilliseconds(1)) == null, "stale ACK used");
        Check("ACK lookup is per PGN", ackMon.AckFrom(0, J1939Clear.Dm3, t0) == null, "wrong PGN");
        Check("ACK lookup is per module", ackMon.AckFrom(48, J1939Clear.Dm11, t0) == null, "wrong SA");

        Check("aftertreatment SPN 5246 flagged", J1939Clear.IsAftertreatmentSpn(5246), "missed");
        Check("aftertreatment SPN 1761 flagged", J1939Clear.IsAftertreatmentSpn(1761), "missed");
        Check("coolant SPN 110 not aftertreatment", !J1939Clear.IsAftertreatmentSpn(110), "flagged");

        ClearRunTests();
        DefSourceTests();
    }

    static void ClearRunTests()
    {
        byte[] defInduced = { 125, 60, 0xFF, 0xFF, (byte)(1 << 5), (byte)(3 << 5), 0xFF, 0xFF };
        byte[] defOk = { 125, 60, 0xFF, 0xFF, 0, 0, 0xFF, 0xFF };

        // 1. Repaired machine: engine and controller both ACK, nothing comes back.
        var sim = new SimLink();
        SimModule eng = sim.Add(0);
        eng.Active = Codes(5246, 31, 1761, 9);
        eng.Prev = Codes(100, 1, 3364, 9);
        eng.Def = defInduced;
        eng.DefAfterClear = defOk;
        SimModule comp = sim.Add(48);
        comp.Active = Codes(520200, 3);
        sim.Prime();
        Eq("primed engine active codes", sim.Mon.Engine.Active.Count, 2);
        var run = new CodeClearRun(ClearKind.All, sim.Mon, null, sim);
        string report = run.Execute();
        List<J1939Tx> cap = sim.Sent;
        Check("good clear headline", Has(report, "RESULT: cleared — no active codes came back."), report);
        Eq("engine DM11 answer", run.Target(0).Dm11, ClearAnswer.Ack);
        Eq("engine DM3 answer", run.Target(0).Dm3, ClearAnswer.Ack);
        Eq("compressor DM11 answer", run.Target(48).Dm11, ClearAnswer.Ack);
        Eq("DM11 request to engine once (ACKed, no retry)", J1939Clear.CountRequest(cap, J1939Clear.Dm11, 0), 1);
        Eq("DM11 request to compressor SA 48 once", J1939Clear.CountRequest(cap, J1939Clear.Dm11, 48), 1);
        Eq("DM11 request to everyone once", J1939Clear.CountRequest(cap, J1939Clear.Dm11, 255), 1);
        Eq("DM3 request to engine once", J1939Clear.CountRequest(cap, J1939Clear.Dm3, 0), 1);
        Eq("DM3 request to compressor SA 48 once", J1939Clear.CountRequest(cap, J1939Clear.Dm3, 48), 1);
        Eq("DM3 request to everyone once", J1939Clear.CountRequest(cap, J1939Clear.Dm3, 255), 1);
        Eq("DM11 is never sent as data", J1939Clear.CountPgn(cap, J1939Clear.Dm11), 0);
        Eq("DM3 is never sent as data", J1939Clear.CountPgn(cap, J1939Clear.Dm3), 0);
        int firstDm3 = cap.FindIndex(x => x.RequestedPgn == J1939Clear.Dm3);
        int lastDm11 = cap.FindLastIndex(x => x.RequestedPgn == J1939Clear.Dm11);
        Check("DM11 finishes before DM3 starts", lastDm11 >= 0 && lastDm11 < firstDm3, lastDm11 + " / " + firstDm3);
        Eq("re-read DM1 from engine", J1939Clear.CountRequest(cap, J1939Clear.Dm1, 0), 1);
        Eq("re-read DM1 from compressor SA 48", J1939Clear.CountRequest(cap, J1939Clear.Dm1, 48), 1);
        Eq("re-read DM2 from engine", J1939Clear.CountRequest(cap, J1939Clear.Dm2, 0), 1);
        Eq("re-read DM1 from everyone", J1939Clear.CountRequest(cap, J1939Clear.Dm1, 255), 1);
        Eq("re-read DEF tank from engine", J1939Clear.CountRequest(cap, J1939Clear.DefTank, 0), 1);
        Eq("re-read DEF tank from everyone", J1939Clear.CountRequest(cap, J1939Clear.DefTank, 255), 1);
        bool wireMatches = cap.Count > 0;
        foreach (J1939Tx tx in cap)
            if (!J1939Clear.SameBytes(J1939Clear.Rp1210Message(tx.Pgn, tx.Dest, tx.Data, tx.Priority), tx.Rp1210)) wireMatches = false;
        Check("captured RP1210 bytes match constructor", wireMatches, "mismatch");
        Eq("engine list empty after the fresh DM1", sim.Mon.Engine.Active.Count, 0);
        Eq("engine previous list empty after the fresh DM2", sim.Mon.Engine.Prev.Count, 0);
        Eq("controller list empty after its fresh DM1", sim.Mon.Module(48).Active.Count, 0);
        Check("report shows engine went 2 → 0", Has(report, "Active: 2 before → 0 now"), report);
        Check("report shows previous 2 → 0", Has(report, "Previous: 2 before → 0 now"), report);
        Check("report reads engine lamps from the fresh DM1", Has(report, "Engine lamps now: Red Stop off"), report);
        Check("report shows the DEF/SCR side read after the clear", Has(report, "read after the clear"), report);
        Check("report shows inducement now", Has(report, "SCR inducement SPN 5246: not active"), report);
        Check("report shows inducement before", Has(report, "SCR inducement was: level 3"), report);
        Check("good clear has no fix-it advice", !Has(report, "What to do:"), report);
        Check("good clear finished quickly", sim.PumpedMs < 2500, sim.PumpedMs + " ms");

        // 2. Fault still true: ACKed, but SCR inducement re-latches in the next DM1.
        sim = new SimLink();
        eng = sim.Add(0);
        eng.Active = Codes(5246, 31, 1761, 9);
        eng.StillTrue = Codes(5246, 31);
        eng.Def = defInduced;
        sim.Prime();
        run = new CodeClearRun(ClearKind.All, sim.Mon, null, sim);
        report = run.Execute();
        Check("came-back headline", Has(report, "RESULT: cleared — 1 active code came straight back"), report);
        Check("returning code listed and tagged", Has(report, "still active: SPN 5246 FMI 31") && Has(report, "[DEF/SCR]"), report);
        Check("Red Stop read from the fresh DM1", Has(report, "Engine lamps now: Red Stop ON"), report);
        Check("came-back advice", Has(report, "The clear worked; the fault did not go away"), report);
        Check("inducement advice", Has(report, "a code clear does not end SCR inducement"), report);
        Check("inducement advice never offers a delete", Has(report, "never disables DEF/SCR"), report);
        Eq("monitor keeps the re-latched code", sim.Mon.Engine.Active.Count, 1);

        // 3. Engine refuses (engine running): access denied.
        sim = new SimLink();
        eng = sim.Add(0);
        eng.Active = Codes(100, 1);
        eng.Dm11Control = J1939Clear.AckDenied;
        eng.Dm3Control = J1939Clear.AckDenied;
        sim.Prime();
        run = new CodeClearRun(ClearKind.All, sim.Mon, null, sim);
        report = run.Execute();
        Eq("denied answer recorded", run.Target(0).Dm11, ClearAnswer.Denied);
        Check("refused headline", Has(report, "RESULT: REFUSED by SA 0"), report);
        Check("refused advice", Has(report, "key ON, engine OFF"), report);
        Check("a refused clear is not called a clear that worked", !Has(report, "The clear worked"), report);
        Eq("a refusal is final (no retry)", J1939Clear.CountRequest(sim.Sent, J1939Clear.Dm11, 0), 1);

        // 4. Controller on the bus but ignores J1939 clears: one retry, then reported.
        sim = new SimLink();
        eng = sim.Add(0);
        eng.Active = Codes(100, 1);
        comp = sim.Add(48);
        comp.Active = Codes(520200, 3);
        comp.StillTrue = Codes(520200, 3);
        comp.Dm11Control = -1;
        comp.Dm3Control = -1;
        sim.Prime();
        run = new CodeClearRun(ClearKind.All, sim.Mon, null, sim);
        report = run.Execute();
        Eq("silent controller retried once on DM11", J1939Clear.CountRequest(sim.Sent, J1939Clear.Dm11, 48), 2);
        Eq("ACKed engine not retried", J1939Clear.CountRequest(sim.Sent, J1939Clear.Dm11, 0), 1);
        Eq("silent controller answer", run.Target(48).Dm11, ClearAnswer.NoAnswer);
        Check("silent controller headline", Has(report, "RESULT: partly cleared; SA 48 did not answer and still shows codes."), report);
        Check("silent controller advice", Has(report, "did not answer, even after a retry") && Has(report, "own keypad"), report);
        Check("unanswered code is not called a code that came back", !Has(report, "came straight back"), report);
        Check("silent controller line", Has(Lf(report), "SA 48 Compressor controller\n  DM11 clear active: no answer"), report);

        // 5. Controller ACKs but stops sending DM1 once healthy: its stale list is emptied.
        sim = new SimLink();
        eng = sim.Add(0);
        comp = sim.Add(48);
        comp.Active = Codes(520200, 3);
        sim.Prime();
        comp.AnswersDm1 = false;
        run = new CodeClearRun(ClearKind.All, sim.Mon, null, sim);
        report = run.Execute();
        Check("controller list emptied on ACK", run.Target(48).EmptiedActive, report);
        Eq("monitor no longer shows the cleared controller code", sim.Mon.Module(48).Active.Count, 0);
        Check("emptied line explains why", Has(report, "ACKed, no DM1 since; the list was emptied"), report);

        // 6. Adapter refuses every frame: fail fast, nothing waited on.
        sim = new SimLink();
        sim.Add(0).Active = Codes(100, 1);
        sim.Prime();
        sim.Rp.FailSendsForTest = 1000;
        run = new CodeClearRun(ClearKind.All, sim.Mon, null, sim);
        report = run.Execute();
        Check("rejected headline", Has(report, "RESULT: FAILED — the adapter rejected every request (simulated reject)"), report);
        Eq("rejected run did not wait for ACKs", sim.PumpedMs, 0);
        Eq("rejected run stops before DM3", J1939Clear.CountRequest(sim.Sent, J1939Clear.Dm3, 0), 0);
        Eq("rejected answer", run.Target(0).Dm11, ClearAnswer.SendFailed);

        // 7. Disconnect / adapter drop in the middle.
        sim = new SimLink();
        eng = sim.Add(0);
        eng.Active = Codes(100, 1);
        eng.Dm11Control = -1;
        sim.Prime();
        sim.DropAtMs = 300;
        run = new CodeClearRun(ClearKind.All, sim.Mon, null, sim);
        report = run.Execute();
        Check("interrupted headline", Has(report, "RESULT: INTERRUPTED"), report);
        Check("interrupted run does not blame the engine for not answering", !Has(report, "did not answer"), report);
        Eq("interrupted run sends no DM3", J1939Clear.CountRequest(sim.Sent, J1939Clear.Dm3, 0), 0);
        Eq("interrupted run keeps the codes it could not confirm", sim.Mon.Engine.Active.Count, 1);

        // 8. Clear previous: DM3 only.
        sim = new SimLink();
        eng = sim.Add(0);
        eng.Active = Codes(100, 1);
        eng.StillTrue = Codes(100, 1);
        eng.Prev = Codes(110, 0, 3364, 9);
        sim.Prime();
        run = new CodeClearRun(ClearKind.Previous, sim.Mon, null, sim);
        report = run.Execute();
        Eq("clear previous sends no DM11", J1939Clear.CountRequest(sim.Sent, J1939Clear.Dm11, 0)
            + J1939Clear.CountRequest(sim.Sent, J1939Clear.Dm11, 255), 0);
        Eq("clear previous sends DM3 to the engine", J1939Clear.CountRequest(sim.Sent, J1939Clear.Dm3, 0), 1);
        Check("clear previous title", Has(report, "clear previously active codes (J1939 DM3)"), report);
        Check("clear previous headline", Has(report, "RESULT: previously active codes cleared."), report);
        Check("clear previous shows 2 → 0", Has(report, "Previous: 2 before → 0 now"), report);
        Eq("clear previous leaves active codes alone", sim.Mon.Engine.Active.Count, 1);

        // 9. Compressor controller not fitted: request still sent, nothing waited on it.
        sim = new SimLink();
        eng = sim.Add(0);
        eng.Active = Codes(100, 1);
        sim.Prime();
        run = new CodeClearRun(ClearKind.All, sim.Mon, null, sim);
        report = run.Execute();
        Eq("absent SA 48 still asked", J1939Clear.CountRequest(sim.Sent, J1939Clear.Dm11, 48), 1);
        Eq("absent SA 48 not retried", J1939Clear.CountRequest(sim.Sent, J1939Clear.Dm3, 48), 1);
        Eq("absent SA 48 answer", run.Target(48).Dm11, ClearAnswer.NotOnBus);
        Check("absent SA 48 is one line", Has(report, "SA 48 Compressor controller — not on the bus"), report);
        Check("absent SA 48 does not hold the clear up", sim.PumpedMs < 2500, sim.PumpedMs + " ms");

        // 10. Separate aftertreatment module at SA 61 with its own DM1: asked directly.
        sim = new SimLink();
        eng = sim.Add(0);
        SimModule acm = sim.Add(61);
        acm.Active = Codes(4364, 18);
        sim.Prime();
        run = new CodeClearRun(ClearKind.All, sim.Mon, null, sim);
        report = run.Execute();
        Eq("aftertreatment module asked directly", J1939Clear.CountRequest(sim.Sent, J1939Clear.Dm11, 61), 1);
        Eq("aftertreatment module ACK", run.Target(61).Dm11, ClearAnswer.Ack);
        Check("aftertreatment module in report", Has(Lf(report), "SA 61\n  DM11 clear active: ACK (cleared)"), report);
        Check("no FE56 on this bus is said plainly", Has(report, "no DEF tank message (PGN FE56) on this hookup"), report);
    }

    static void DefSourceTests()
    {
        DateTime t = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
        var m = new BusMonitor();
        m.Feed(F(0xF004, 0, 0, 0, 0, 0x00, 0x19, 0, 0, 0), t);
        // The tank header (here SA 163) sends FE56 itself; the ECM never does.
        m.Feed(F(0xFE56, 163, 125, 60, 0xFF, 0xFF, 0, 0, 0xFF, 0xFF), t);
        Eq("FE56 from a non-engine module used", m.DefPct, 50d);
        Eq("DEF source remembered", m.DefSa, 163);
        Check("panel says where FE56 came from", Has(m.AftText(), "FE56 from SA 163"), m.AftText());
        m.Feed(F(0xFE56, 0, 100, 60, 0xFF, 0xFF, 0, 0, 0xFF, 0xFF), t.AddSeconds(1));
        Eq("engine FE56 wins when it talks", m.DefPct, 40d);
        Eq("DEF source switches to the engine", m.DefSa, 0);
        m.Feed(F(0xFE56, 163, 250, 60, 0xFF, 0xFF, 0, 0, 0xFF, 0xFF), t.AddSeconds(2));
        Eq("other FE56 ignored while the engine is talking", m.DefPct, 40d);
        m.Feed(F(0xFE56, 163, 250, 60, 0xFF, 0xFF, 0, 0, 0xFF, 0xFF), t.AddSeconds(10));
        Eq("falls back once the engine goes quiet", m.DefPct, 100d);

        var two = new BusMonitor();
        two.Feed(F(0xFE56, 163, 125, 60, 0xFF, 0xFF, 0, 0, 0xFF, 0xFF), t);
        two.Feed(F(0xFE56, 164, 25, 60, 0xFF, 0xFF, 0, 0, 0xFF, 0xFF), t.AddMilliseconds(200));
        Eq("a second non-engine FE56 does not flicker the readout", two.DefPct, 50d);
        two.ClearLive();
        Eq("clear live forgets the DEF source", two.DefSa, -1);

        // Clear re-reads FE56 from the module that actually sends it.
        var sim = new SimLink();
        sim.Add(0);
        SimModule tank = sim.Add(163);
        tank.Def = new byte[] { 125, 60, 0xFF, 0xFF, 0, 0, 0xFF, 0xFF };
        sim.Prime();
        var run = new CodeClearRun(ClearKind.All, sim.Mon, null, sim);
        string report = run.Execute();
        Eq("clear re-reads FE56 from the tank header", J1939Clear.CountRequest(sim.Sent, J1939Clear.DefTank, 163), 1);
        Check("report names the FE56 source", Has(report, "PGN FE56 from SA 163, read after the clear"), report);
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

    static int CountKind(KbIndex kb, string kind)
    {
        int n = 0;
        foreach (Hit h in kb.All)
            if (h.Kind == kind) n++;
        return n;
    }

    static void BundledKbTests()
    {
        string disk = Path.Combine(FindRepoRoot(), "kb");
        Check("shipped kb.json is in the repo", File.Exists(Path.Combine(disk, "data", "kb.json")), disk);
        var fromDisk = new KbIndex();
        fromDisk.Load(disk);
        Eq("shipped fault codes", CountKind(fromDisk, "CODE"), 155);
        Eq("shipped service-access rows", CountKind(fromDisk, "PASSWORD"), 1424);
        Eq("shipped manual index", CountKind(fromDisk, "MANUAL"), 2147);
        Eq("shipped filter rows", CountKind(fromDisk, "FILTER"), 63);
        Eq("shipped equipment rows", CountKind(fromDisk, "EQUIP"), 37);
        Eq("shipped load had no file errors", fromDisk.Errors.Count, 0);
        int total;
        var hits = fromDisk.Search("1AVPT", "CODE", 5, out total);
        Check("real IR sensor code is searchable",
            total >= 1 && hits.Count > 0 && hits[0].Title.IndexOf("Sensor Failure", StringComparison.OrdinalIgnoreCase) >= 0,
            hits.Count == 0 ? "no hit" : hits[0].Title);
        hits = fromDisk.Search("F68", "FILTER", 5, out total);
        Check("real filter chart F68 is searchable",
            total >= 1 && hits.Count > 0 && hits[0].Title.IndexOf("F68", StringComparison.OrdinalIgnoreCase) >= 0,
            hits.Count == 0 ? "no hit" : hits[0].Title);
        hits = fromDisk.Search("XHP1170", "EQUIP", 5, out total);
        Check("real equipment XHP1170 is searchable",
            total >= 1 && hits.Count > 0 && hits[0].Title.IndexOf("XHP1170", StringComparison.OrdinalIgnoreCase) >= 0,
            hits.Count == 0 ? "no hit" : hits[0].Title);

        string dest = Path.Combine(Path.GetTempPath(), "tb-baked-" + Guid.NewGuid().ToString("N"));
        Check("embedded resources materialize", KbIndex.TryMaterializeBaked(dest), dest);
        var fromExe = new KbIndex();
        fromExe.Load(dest);
        Eq("embedded fault codes match the folder", CountKind(fromExe, "CODE"), 155);
        Eq("embedded passwords match the folder", CountKind(fromExe, "PASSWORD"), 1424);
        Eq("embedded manuals match the folder", CountKind(fromExe, "MANUAL"), 2147);
        Eq("embedded filters match the folder", CountKind(fromExe, "FILTER"), 63);
        Eq("embedded equipment matches the folder", CountKind(fromExe, "EQUIP"), 37);
        try { Directory.Delete(dest, true); } catch { }
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

    static void InstallerTests()
    {
        string root = FindRepoRoot();
        string iss = Path.Combine(root, "installer", "techbench.iss");
        string build = Path.Combine(root, "installer", "build.bat");
        string fetch = Path.Combine(root, "installer", "fetch-iscc.ps1");
        string release = Path.Combine(root, "release.bat");
        Check("installer script present", File.Exists(iss), iss);
        Check("installer build.bat present", File.Exists(build), build);
        Check("release.bat calls installer",
            File.Exists(release) && File.ReadAllText(release).IndexOf(@"installer\build.bat", StringComparison.OrdinalIgnoreCase) >= 0,
            "release.bat does not call installer\\build.bat");
        if (!File.Exists(iss)) return;
        string text = File.ReadAllText(iss);
        Check("per-user lowest privileges",
            text.IndexOf("PrivilegesRequired=lowest", StringComparison.OrdinalIgnoreCase) >= 0, "not lowest");
        Check("user-writable default dir",
            text.IndexOf("{localappdata}\\Programs\\TechBench", StringComparison.OrdinalIgnoreCase) >= 0, text);
        Check("installs TechBench.exe",
            text.IndexOf("TechBench.exe", StringComparison.OrdinalIgnoreCase) >= 0, "missing exe");
        Check("installs assets folder",
            text.IndexOf("assets", StringComparison.OrdinalIgnoreCase) >= 0, "no assets");
        Check("installs shipped knowledge base",
            text.IndexOf(@"air-compressor-kb", StringComparison.OrdinalIgnoreCase) >= 0
            && text.IndexOf(@"..\kb\*", StringComparison.OrdinalIgnoreCase) >= 0, "kb folder not in setup");
        string buildBat = Path.Combine(root, "build.bat");
        if (File.Exists(buildBat))
            Check("exe embeds the field database",
                File.ReadAllText(buildBat).IndexOf("TechBench.BundledKb.data.kb.json", StringComparison.Ordinal) >= 0,
                "build.bat missing /resource");
        Check("Start Menu shortcut",
            text.IndexOf("{group}", StringComparison.OrdinalIgnoreCase) >= 0, "no group icon");
        Check("Desktop shortcut task",
            text.IndexOf("{autodesktop}", StringComparison.OrdinalIgnoreCase) >= 0
            && text.IndexOf("desktopicon", StringComparison.OrdinalIgnoreCase) >= 0, "no desktop");
        Check("no GitHub PAT in installer script", !ContainsSecret(text), "secret");
        if (File.Exists(build))
            Check("no GitHub PAT in installer build.bat", !ContainsSecret(File.ReadAllText(build)), "secret");
        if (File.Exists(fetch))
            Check("no GitHub PAT in fetch-iscc.ps1", !ContainsSecret(File.ReadAllText(fetch)), "secret");
        if (File.Exists(release))
        {
            string rel = File.ReadAllText(release);
            Check("no GitHub PAT in release.bat", !ContainsSecret(rel), "secret");
            Check("release.bat points latest.json url at public dist",
                rel.IndexOf("blackviperxiii-ui/tech-bench-dist/releases/download/v", StringComparison.OrdinalIgnoreCase) >= 0,
                "still points at private tech-bench releases");
            Check("release.bat does not use private-repo download URL",
                rel.IndexOf("blackviperxiii-ui/tech-bench/releases/download", StringComparison.OrdinalIgnoreCase) < 0,
                "private-repo download URL");
            Check("release.bat asserts PE i386",
                rel.IndexOf("assert-pe-i386.ps1", StringComparison.OrdinalIgnoreCase) >= 0, "no PE assert");
            Check("release.bat mentions the private GitHub release",
                rel.IndexOf("private GitHub release", StringComparison.OrdinalIgnoreCase) >= 0, "private release");
            Check("release.bat mentions shop Setup",
                rel.IndexOf("shop Setup", StringComparison.OrdinalIgnoreCase) >= 0, "shop Setup");
            Check("release.bat has no public dist upload line",
                rel.IndexOf("Upload TechBench.exe and latest.json to the public dist repo", StringComparison.Ordinal) < 0,
                "upload line");
            Check("release.bat has no DIST_REPO_TOKEN",
                rel.IndexOf("DIST_REPO_TOKEN", StringComparison.Ordinal) < 0, "token");
        }

        string readmePath = Path.Combine(root, "README.md");
        string readme = File.Exists(readmePath) ? File.ReadAllText(readmePath) : "";
        Check("README has no DIST_REPO_TOKEN",
            readme.IndexOf("DIST_REPO_TOKEN", StringComparison.Ordinal) < 0, "token");
        Check("README has no public-dist upload instruction",
            readme.IndexOf("public dist repo", StringComparison.OrdinalIgnoreCase) < 0
            && readme.IndexOf("Upload TechBench.exe and latest.json", StringComparison.Ordinal) < 0
            && readme.IndexOf("publish-dist.ps1", StringComparison.OrdinalIgnoreCase) < 0,
            "upload instruction");
        Check("README documents update-feed-path.txt",
            readme.IndexOf("update-feed-path.txt", StringComparison.Ordinal) >= 0, "missing feed path");
        Check("README documents F1 cheat sheet",
            readme.IndexOf("| F1 |", StringComparison.Ordinal) >= 0
            || readme.IndexOf("F1 | shop cheat sheet", StringComparison.OrdinalIgnoreCase) >= 0,
            "missing F1");
        string rolloutPath = Path.Combine(root, "docs", "shop-rollout.md");
        string rollout = File.Exists(rolloutPath) ? File.ReadAllText(rolloutPath) : "";
        Check("shop-rollout documents update-feed-path.txt",
            rollout.IndexOf("update-feed-path.txt", StringComparison.Ordinal) >= 0, "missing feed path");
        Check("shop-rollout documents adapter-prefs.json",
            rollout.IndexOf("adapter-prefs.json", StringComparison.Ordinal) >= 0, "missing adapter prefs");

        string peAssert = Path.Combine(root, "tools", "assert-pe-i386.ps1");
        Check("PE i386 assert script present", File.Exists(peAssert), peAssert);
        if (File.Exists(peAssert))
        {
            string pe = File.ReadAllText(peAssert);
            Check("PE assert requires Machine 0x014C",
                pe.IndexOf("0x014C", StringComparison.OrdinalIgnoreCase) >= 0, "no 0x014C");
            Check("PE assert rejects AnyCPU",
                pe.IndexOf("AnyCPU", StringComparison.OrdinalIgnoreCase) >= 0
                && pe.IndexOf("32BITREQUIRED", StringComparison.OrdinalIgnoreCase) >= 0, "no AnyCPU / 32BITREQUIRED check");
            Check("PE assert native path still requires 0x014C",
                pe.IndexOf("[switch]$Native", StringComparison.Ordinal) >= 0
                && pe.IndexOf("OK native PE Machine=0x014C", StringComparison.Ordinal) >= 0, "no native fail-close");
        }

        string publish = Path.Combine(root, "tools", "publish-dist.ps1");
        bool publishGone = !File.Exists(publish);
        string pub = publishGone ? "" : File.ReadAllText(publish);
        Check("dist publish script removed", publishGone, publish);
        Check("dist publish does not target tech-bench-dist",
            publishGone || pub.IndexOf("blackviperxiii-ui/tech-bench-dist", StringComparison.OrdinalIgnoreCase) < 0,
            "still targets tech-bench-dist");
        Check("dist publish does not document DIST_REPO_TOKEN",
            publishGone || pub.IndexOf("DIST_REPO_TOKEN", StringComparison.Ordinal) < 0,
            "still documents DIST_REPO_TOKEN");
        Check("no GitHub PAT remains in publish-dist.ps1",
            publishGone || !ContainsSecret(pub), "secret");

        string workflow = Path.Combine(root, ".github", "workflows", "windows-installer.yml");
        Check("windows installer workflow present", File.Exists(workflow), workflow);
        if (File.Exists(workflow))
        {
            string yml = File.ReadAllText(workflow);
            Check("CI runs self-test",
                yml.IndexOf("test.bat", StringComparison.OrdinalIgnoreCase) >= 0, "test.bat");
            Check("CI runs release.bat",
                yml.IndexOf("release.bat", StringComparison.OrdinalIgnoreCase) >= 0, "release.bat");
            Check("CI asserts PE i386",
                yml.IndexOf("assert-pe-i386.ps1", StringComparison.OrdinalIgnoreCase) >= 0, "no PE assert step");
            Check("CI TechBench.exe assert keeps the CLR check",
                yml.IndexOf("assert-pe-i386.ps1 .\\TechBench.exe", StringComparison.OrdinalIgnoreCase) >= 0
                && yml.IndexOf("assert-pe-i386.ps1 -Native .\\TechBench.exe", StringComparison.OrdinalIgnoreCase) < 0,
                "TechBench.exe assert lost 32BITREQUIRED");
            Check("CI fail-closes a non-i386 setup exe",
                yml.IndexOf("assert-pe-i386.ps1 -Native", StringComparison.OrdinalIgnoreCase) >= 0
                && yml.IndexOf("TechBench-Setup-", StringComparison.OrdinalIgnoreCase) >= 0,
                "setup exe is not fail-closed");
            Check("CI uploads the installer artifact",
                yml.IndexOf("actions/upload-artifact@", StringComparison.Ordinal) >= 0, "upload-artifact");
            Check("CI has no public dist publish step",
                yml.IndexOf("Publish updater files to public dist repo", StringComparison.Ordinal) < 0
                && yml.IndexOf("publish-dist.ps1", StringComparison.OrdinalIgnoreCase) < 0,
                "publish step remains");
            Check("CI does not reference DIST_REPO_TOKEN",
                yml.IndexOf("DIST_REPO_TOKEN", StringComparison.Ordinal) < 0, "token");
            Check("CI runs first-install smoke",
                yml.IndexOf("install-smoke.ps1", StringComparison.OrdinalIgnoreCase) >= 0
                && yml.IndexOf("First-install smoke", StringComparison.Ordinal) >= 0,
                "smoke step");
            Check("CI does not invent a new installer workflow path",
                yml.IndexOf("runs-on: windows-latest", StringComparison.OrdinalIgnoreCase) >= 0, yml);
        }

        string smokePs1 = Path.Combine(root, "tools", "install-smoke.ps1");
        Check("install-smoke.ps1 present", File.Exists(smokePs1), smokePs1);
        if (File.Exists(smokePs1))
        {
            string smokeSrc = File.ReadAllText(smokePs1);
            Check("install-smoke.ps1 uses /VERYSILENT",
                smokeSrc.IndexOf("/VERYSILENT", StringComparison.Ordinal) >= 0, "silent");
            Check("install-smoke.ps1 runs --smoke",
                smokeSrc.IndexOf("--smoke", StringComparison.Ordinal) >= 0, "smoke");
            Check("install-smoke.ps1 uninstalls with unins000.exe",
                smokeSrc.IndexOf("unins000.exe", StringComparison.Ordinal) >= 0, "unins");
            Check("install-smoke.ps1 checks the report kb= path",
                smokeSrc.IndexOf("kb=", StringComparison.Ordinal) >= 0
                && smokeSrc.IndexOf("air-compressor-kb", StringComparison.OrdinalIgnoreCase) >= 0,
                "kb=");
            Check("install-smoke.ps1 does not use /CURRENTUSER",
                smokeSrc.IndexOf("/CURRENTUSER", StringComparison.OrdinalIgnoreCase) < 0,
                "/CURRENTUSER");
        }

        int runAt = text.IndexOf("[Run]", StringComparison.OrdinalIgnoreCase);
        int runNext = runAt < 0 ? -1 : text.IndexOf("\n[", runAt + 4, StringComparison.Ordinal);
        string runSec = "";
        if (runAt >= 0)
            runSec = runNext < 0 ? text.Substring(runAt) : text.Substring(runAt, runNext - runAt);
        Check("Inno [Run] line keeps skipifsilent",
            runSec.IndexOf("skipifsilent", StringComparison.OrdinalIgnoreCase) >= 0
            && runSec.IndexOf("postinstall", StringComparison.OrdinalIgnoreCase) >= 0,
            runSec);
    }

    static string FindRepoRoot()
    {
        string dir = Environment.CurrentDirectory;
        if (File.Exists(Path.Combine(dir, "installer", "techbench.iss"))) return dir;
        dir = AppDomain.CurrentDomain.BaseDirectory;
        if (File.Exists(Path.Combine(dir, "installer", "techbench.iss"))) return dir;
        return Environment.CurrentDirectory;
    }

    static bool ContainsSecret(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return text.IndexOf("ghp_", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("github_pat", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("GITHUB_TOKEN", StringComparison.OrdinalIgnoreCase) >= 0
            || text.IndexOf("Authorization:", StringComparison.OrdinalIgnoreCase) >= 0;
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
            Eq("sanitize drops junk", ShopSync.SanitizeTechId("Viper S."), "Viper-S");
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
            string fileDir = Path.Combine(ShopSync.ShardDir(kbA, "alice"), "files");
            string tempName = "partial.png.tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(Path.Combine(fileDir, tempName), "partial");
            kb.Load(kbA);
            Check("file dir skips atomic temp names",
                kb.Search(tempName, "FILE", 10, out total).Count == 0, "total=" + total);

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

            // History publish / import: new csv copies; same bytes are skipped; a same name
            // with different bytes stays local and is imported under a collision name.
            string histSrc = Path.Combine(kbA, "job_dtcs.csv");
            File.WriteAllText(histSrc, "time,job,state,spn,fmi,occurrences,name,fmi_text\n2026,HP,active,1,0,1,x,x\n");
            ShopSync.PublishHistory(kbA, "alice", histSrc);
            string sessions = Path.Combine(Path.GetTempPath(), "tb-sess-" + Guid.NewGuid().ToString("N"));
            Eq("import copies missing history", ShopSync.ImportHistory(kbA, sessions), 1);
            Eq("import is idempotent", ShopSync.ImportHistory(kbA, sessions), 0);
            File.WriteAllText(Path.Combine(sessions, "job_dtcs.csv"), "local-only");
            string shopHist = Path.Combine(ShopSync.ShardDir(kbA, "alice"), "history", "job_dtcs.csv");
            File.WriteAllText(shopHist, "shop-other");
            Eq("import brings in same-name different-content history under a collision name",
                ShopSync.ImportHistory(kbA, sessions), 1);
            Eq("local session csv kept", File.ReadAllText(Path.Combine(sessions, "job_dtcs.csv")), "local-only");
            string shopHash = ShopSync.HashFile(shopHist);
            string collisionName = "job.alice-" + shopHash.Substring(0, 8) + "_dtcs.csv";
            string collision = Path.Combine(sessions, collisionName);
            Eq("collision file exists with content shop-other",
                File.Exists(collision) ? File.ReadAllText(collision) : "(missing " + collisionName + ")",
                "shop-other");
            string[] sessionCsvs = Directory.GetFiles(sessions, "*_dtcs.csv");
            Eq("session has local plus collision", sessionCsvs.Length, 2);
            History importedHist = History.Load(sessions);
            Eq("history loading sees the collision file", importedHist.FilesRead, sessionCsvs.Length);
            Eq("second import returns 0", ShopSync.ImportHistory(kbA, sessions), 0);
            File.WriteAllText(Path.Combine(ShopSync.ShardDir(kbA, "alice"), "history", "dup_dtcs.csv"), "shop-other");
            Eq("same hash under another name returns 0", ShopSync.ImportHistory(kbA, sessions), 0);
            File.WriteAllText(Path.Combine(sessions, "same_dtcs.csv"), "identical-bytes");
            File.WriteAllText(Path.Combine(ShopSync.ShardDir(kbA, "alice"), "history", "same_dtcs.csv"), "identical-bytes");
            Eq("identical-content same-name file still returns 0", ShopSync.ImportHistory(kbA, sessions), 0);

            string clashShop = Path.Combine(ShopSync.ShardDir(kbA, "alice"), "history", "clash_dtcs.csv");
            File.WriteAllText(clashShop, "shop-body-v2");
            string clashHash = ShopSync.HashFile(clashShop);
            File.WriteAllText(Path.Combine(sessions, "clash_dtcs.csv"), "local-body");
            string clashShort = Path.Combine(sessions, "clash.alice-" + clashHash.Substring(0, 8) + "_dtcs.csv");
            File.WriteAllText(clashShort, "other-bytes");
            string clashFull = Path.Combine(sessions, "clash.alice-" + clashHash + "_dtcs.csv");
            int clashImported = ShopSync.ImportHistory(kbA, sessions);
            string clashLanded = File.Exists(clashFull) ? File.ReadAllText(clashFull) : "(missing)";
            Check("8-hex collision with a different hash still imports",
                clashImported >= 1 && clashLanded == "shop-body-v2",
                "n=" + clashImported + " body=" + clashLanded);
            Eq("import lands under the full-hash name", clashLanded, "shop-body-v2");
            Eq("8-hex collision file was not overwritten",
                File.Exists(clashShort) ? File.ReadAllText(clashShort) : "(missing)",
                "other-bytes");

            string localUnit = "time,job,state,spn,fmi,occurrences,name,fmi_text\n"
                + "2026-01-01T00:00:00,\"HP450 555\",active,100,1,1,\"Oil pressure\",\"x\"\n";
            string shopUnit = "time,job,state,spn,fmi,occurrences,name,fmi_text\n"
                + "2026-05-01T00:00:00,\"HP450 555\",active,1761,9,2,\"DEF tank level\",\"x\"\n";
            File.WriteAllText(Path.Combine(sessions, "HP450_555_session_dtcs.csv"), localUnit);
            string shopUnitPath = Path.Combine(ShopSync.ShardDir(kbA, "alice"), "history", "HP450_555_session_dtcs.csv");
            File.WriteAllText(shopUnitPath, shopUnit);
            Eq("unit history with a new body imports beside the local file", ShopSync.ImportHistory(kbA, sessions), 1);
            string unitHash = ShopSync.HashFile(shopUnitPath);
            string unitCollision = Path.Combine(sessions, "HP450_555_session.alice-" + unitHash.Substring(0, 8) + "_dtcs.csv");
            Check("collision keeps the job prefix", File.Exists(unitCollision), unitCollision);
            History unitHist = History.Load(sessions);
            bool sawShopFault = false;
            bool sawLocalFault = false;
            foreach (HistoryFault fault in unitHist.ForJob("HP450 555"))
            {
                if (fault.Spn == 1761) sawShopFault = true;
                if (fault.Spn == 100) sawLocalFault = true;
            }
            Check("history attributes the collision file to the unit", sawShopFault, "missing SPN 1761");
            Check("local unit history was kept", sawLocalFault, "missing SPN 100");

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

    static void DataSafetyTests()
    {
        SubscriptionKeyMaskTests();
        LaunchBlockTests();
        ManifestVersionTests();
        PacketSidecarSyncTests();
    }

    static void SourceListTests()
    {
        string root = FindRepoRoot();
        string corePath = Path.Combine(root, "sources", "core.rsp");
        string appPath = Path.Combine(root, "sources", "app.rsp");
        string selftestPath = Path.Combine(root, "sources", "selftest.rsp");
        string buildPath = Path.Combine(root, "build.bat");
        string testPath = Path.Combine(root, "test.bat");
        Check("sources\\core.rsp present", File.Exists(corePath), corePath);
        Check("sources\\app.rsp present", File.Exists(appPath), appPath);
        Check("sources\\selftest.rsp present", File.Exists(selftestPath), selftestPath);
        Check("build.bat present for source list", File.Exists(buildPath), buildPath);
        Check("test.bat present for source list", File.Exists(testPath), testPath);

        List<string> core = ReadRspNames(corePath);
        List<string> app = ReadRspNames(appPath);
        List<string> selftest = ReadRspNames(selftestPath);
        int dup = 0;
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int pass = 0; pass < 2; pass++)
        {
            List<string> list = pass == 0 ? core : app;
            string which = pass == 0 ? "core.rsp" : "app.rsp";
            for (int i = 0; i < list.Count; i++)
            {
                string name = list[i];
                string prior;
                if (seen.TryGetValue(name, out prior))
                {
                    Check("no duplicate source " + name, false, prior + " and " + which);
                    dup++;
                }
                else seen[name] = which;
            }
        }
        if (dup == 0)
            Check("response files have no duplicate sources", true, "");
        Check("SelfTest.cs is not in a response file", !seen.ContainsKey("SelfTest.cs"), "listed");

        int selfDup = 0;
        var selfSeen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < selftest.Count; i++)
        {
            string name = selftest[i];
            string prior;
            if (selfSeen.TryGetValue(name, out prior))
            {
                Check("no duplicate selftest source " + name, false, prior + " and selftest.rsp");
                selfDup++;
            }
            else selfSeen[name] = "selftest.rsp";
            if (seen.ContainsKey(name) || seen.ContainsKey(Path.GetFileName(name)))
                Check("selftest file is not in core or app rsp", false, name);
        }
        if (selfDup == 0 && selftest.Count > 0)
            Check("selftest.rsp has no duplicate sources", true, "");

        int badListed = 0;
        for (int i = 0; i < core.Count; i++) badListed += BadRspEntry(root, core[i]);
        for (int i = 0; i < app.Count; i++) badListed += BadRspEntry(root, app[i]);
        for (int i = 0; i < selftest.Count; i++) badListed += BadRspPathEntry(root, selftest[i]);
        if (badListed == 0)
            Check("every listed source file exists", true, "");

        string testsDir = Path.Combine(root, "tests");
        int missingTest = 0;
        if (Directory.Exists(testsDir))
        {
            string[] testsDirCs = Directory.GetFiles(testsDir, "*.cs");
            for (int i = 0; i < testsDirCs.Length; i++)
            {
                string rel = "tests\\" + Path.GetFileName(testsDirCs[i]);
                if (!selfSeen.ContainsKey(rel))
                {
                    Check("tests source is listed in selftest.rsp (" + rel + ")", false, "missing");
                    missingTest++;
                }
            }
        }
        if (missingTest == 0 && selftest.Count > 0)
            Check("every tests\\*.cs is listed in selftest.rsp", true, "");

        int missing = 0;
        if (Directory.Exists(root))
        {
            string[] rootCs = Directory.GetFiles(root, "*.cs");
            for (int i = 0; i < rootCs.Length; i++)
            {
                string name = Path.GetFileName(rootCs[i]);
                if (string.Equals(name, "SelfTest.cs", StringComparison.OrdinalIgnoreCase))
                    continue;
                string where;
                if (!seen.TryGetValue(name, out where))
                {
                    Check("root source is listed (" + name + ")", false, "missing from core.rsp and app.rsp");
                    missing++;
                }
            }
        }
        if (missing == 0 && dup == 0)
            Check("every root .cs except SelfTest.cs is listed once", true, "");

        string build = File.Exists(buildPath) ? File.ReadAllText(buildPath) : "";
        string test = File.Exists(testPath) ? File.ReadAllText(testPath) : "";
        Check("build.bat references sources\\core.rsp",
            build.IndexOf(@"sources\core.rsp", StringComparison.OrdinalIgnoreCase) >= 0, "core.rsp");
        Check("build.bat references sources\\app.rsp",
            build.IndexOf(@"sources\app.rsp", StringComparison.OrdinalIgnoreCase) >= 0, "app.rsp");
        List<string> buildCs = ListedCsTokens(build);
        if (buildCs.Count == 0)
            Check("build.bat has no inline .cs file names", true, "");
        else
        {
            for (int i = 0; i < buildCs.Count; i++)
                Check("build.bat has no inline .cs file names", false, buildCs[i]);
        }

        List<string> chunks = CscCompileChunks(test);
        Check("test.bat has two csc compiles", chunks.Count == 2, chunks.Count.ToString());
        if (chunks.Count >= 1)
        {
            string self = chunks[0];
            Check("SelfTest compile references core.rsp",
                self.IndexOf("SelfTest.cs", StringComparison.OrdinalIgnoreCase) >= 0
                && self.IndexOf(@"sources\core.rsp", StringComparison.OrdinalIgnoreCase) >= 0,
                "core");
            Check("SelfTest compile references selftest.rsp",
                self.IndexOf(@"sources\selftest.rsp", StringComparison.OrdinalIgnoreCase) >= 0,
                "selftest.rsp");
            Check("SelfTest compile does not reference app.rsp",
                self.IndexOf(@"sources\app.rsp", StringComparison.OrdinalIgnoreCase) < 0,
                "app.rsp");
        }
        if (chunks.Count >= 2)
        {
            string layout = chunks[1];
            Check("LayoutAudit compile references both response files",
                layout.IndexOf(@"tools\LayoutAudit.cs", StringComparison.OrdinalIgnoreCase) >= 0
                && layout.IndexOf(@"sources\core.rsp", StringComparison.OrdinalIgnoreCase) >= 0
                && layout.IndexOf(@"sources\app.rsp", StringComparison.OrdinalIgnoreCase) >= 0,
                "layout");
        }
        List<string> testCs = ListedCsTokens(test);
        int stray = 0;
        for (int i = 0; i < testCs.Count; i++)
        {
            if (AllowedTestInline(testCs[i])) continue;
            Check("test.bat inline .cs is only SelfTest, CrashForm, or LayoutAudit", false, testCs[i]);
            stray++;
        }
        if (stray == 0)
            Check("test.bat lists no root .cs inline except SelfTest.cs, CrashForm.cs, and tools\\LayoutAudit.cs", true, "");

        Check("build.bat has no *.cs wildcard",
            build.IndexOf("*.cs", StringComparison.OrdinalIgnoreCase) < 0, "*.cs");
        Check("build.bat has no /recurse: wildcard",
            build.IndexOf("/recurse:", StringComparison.OrdinalIgnoreCase) < 0, "/recurse:");
        Check("test.bat has no *.cs wildcard",
            test.IndexOf("*.cs", StringComparison.OrdinalIgnoreCase) < 0, "*.cs");
        Check("test.bat has no /recurse: wildcard",
            test.IndexOf("/recurse:", StringComparison.OrdinalIgnoreCase) < 0, "/recurse:");
    }

    static int BadRspEntry(string root, string name)
    {
        if (string.IsNullOrEmpty(name)
            || name.IndexOf('\\') >= 0 || name.IndexOf('/') >= 0
            || name.IndexOf(' ') >= 0 || name.IndexOf('\t') >= 0)
        {
            Check("listed source is one root file name", false, name ?? "(null)");
            return 1;
        }
        if (!File.Exists(Path.Combine(root, name)))
        {
            Check("listed source exists", false, name);
            return 1;
        }
        return 0;
    }

    static int BadRspPathEntry(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative)
            || relative.IndexOf(' ') >= 0 || relative.IndexOf('\t') >= 0)
        {
            Check("listed selftest path is relative", false, relative ?? "(null)");
            return 1;
        }
        string norm = relative.Replace('/', '\\');
        if (!norm.StartsWith("tests\\", StringComparison.OrdinalIgnoreCase))
        {
            Check("selftest path stays under tests\\", false, relative);
            return 1;
        }
        if (!File.Exists(Path.Combine(root, norm)))
        {
            Check("listed selftest file exists", false, relative);
            return 1;
        }
        return 0;
    }

    static bool AllowedTestInline(string token)
    {
        return string.Equals(token, "SelfTest.cs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "CrashForm.cs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, @"tools\LayoutAudit.cs", StringComparison.OrdinalIgnoreCase);
    }

    static List<string> ReadRspNames(string path)
    {
        var list = new List<string>();
        if (!File.Exists(path)) return list;
        string[] lines = File.ReadAllLines(path);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i] == null ? "" : lines[i].Trim();
            if (line.Length > 0 && line[0] == '\uFEFF')
                line = line.Substring(1).Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            list.Add(line);
        }
        return list;
    }

    static List<string> CscCompileChunks(string bat)
    {
        var chunks = new List<string>();
        if (string.IsNullOrEmpty(bat)) return chunks;
        string needle = "\"%CSC%\" /nologo";
        int from = 0;
        while (from < bat.Length)
        {
            int i = bat.IndexOf(needle, from, StringComparison.OrdinalIgnoreCase);
            if (i < 0) break;
            int next = bat.IndexOf(needle, i + needle.Length, StringComparison.OrdinalIgnoreCase);
            if (next < 0) next = bat.Length;
            chunks.Add(bat.Substring(i, next - i));
            from = next;
        }
        return chunks;
    }

    static List<string> ListedCsTokens(string text)
    {
        var found = new List<string>();
        if (string.IsNullOrEmpty(text)) return found;
        for (int i = 0; i < text.Length - 2; i++)
        {
            if (text[i] != '.') continue;
            char c1 = text[i + 1];
            char c2 = text[i + 2];
            if (c1 != 'c' && c1 != 'C') continue;
            if (c2 != 's' && c2 != 'S') continue;
            int after = i + 3;
            if (after < text.Length)
            {
                char n = text[after];
                if (char.IsLetterOrDigit(n) || n == '_') continue;
            }
            int start = i;
            while (start > 0)
            {
                char p = text[start - 1];
                if (char.IsLetterOrDigit(p) || p == '_') start--;
                else break;
            }
            if (start == i) continue;
            string name = text.Substring(start, i - start) + ".cs";
            string token = name;
            if (start >= 6)
            {
                string pre = text.Substring(start - 6, 6);
                if (string.Equals(pre, "tools\\", StringComparison.OrdinalIgnoreCase))
                    token = "tools\\" + name;
            }
            found.Add(token);
        }
        return found;
    }

    static void SmokeTests()
    {
        string kb = Path.Combine(FindRepoRoot(), "kb");
        SmokeResult ok = InstallSmoke.Run(kb);
        Check("smoke OK on shipped kb",
            ok != null && ok.ExitCode == 0
            && ok.Line != null
            && ok.Line.IndexOf("TechBench " + AppVersion.Number + " smoke OK kb=", StringComparison.Ordinal) >= 0
            && ok.Line.IndexOf(kb, StringComparison.OrdinalIgnoreCase) >= 0,
            ok == null ? "(null)" : ok.Line);
        int records = -1;
        if (ok != null && ok.Line != null)
        {
            int recAt = ok.Line.IndexOf("records=", StringComparison.Ordinal);
            if (recAt >= 0)
            {
                string tail = ok.Line.Substring(recAt + "records=".Length).Trim();
                int.TryParse(tail, out records);
            }
        }
        Check("smoke OK record count is positive", records > 0, records.ToString());

        string empty = Path.Combine(Path.GetTempPath(), "tb-smoke-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            SmokeResult bad = InstallSmoke.Run(empty);
            Check("smoke FAIL on empty kb dir",
                bad != null && bad.ExitCode != 0
                && bad.Line != null
                && bad.Line.IndexOf("smoke FAIL:", StringComparison.Ordinal) >= 0
                && bad.Line.IndexOf("smoke OK", StringComparison.Ordinal) < 0,
                bad == null ? "(null)" : bad.Line);
        }
        finally
        {
            try { Directory.Delete(empty, true); } catch { }
        }

        string programPath = Path.Combine(FindRepoRoot(), "Program.cs");
        string program = File.Exists(programPath) ? File.ReadAllText(programPath) : "";
        int mainAt = program.IndexOf("static int Main(string[] args)", StringComparison.Ordinal);
        int wantsAt = mainAt < 0 ? -1 : program.IndexOf("static bool WantsSmoke", mainAt, StringComparison.Ordinal);
        string mainBody = "";
        if (mainAt >= 0 && wantsAt > mainAt)
            mainBody = program.Substring(mainAt, wantsAt - mainAt);
        int callSmoke = mainBody.IndexOf("WantsSmoke(", StringComparison.Ordinal);
        int callRun = mainBody.IndexOf("RunSmoke(", StringComparison.Ordinal);
        int callSwap = mainBody.IndexOf("TryBeginPendingSwap", StringComparison.Ordinal);
        int smokeLit = wantsAt < 0 ? -1 : program.IndexOf("\"--smoke\"", wantsAt, StringComparison.Ordinal);
        Check("Program.cs routes --smoke before TryBeginPendingSwap",
            mainAt >= 0 && wantsAt > mainAt
            && callSmoke >= 0 && callRun > callSmoke && callSwap > callRun
            && smokeLit > wantsAt,
            "main=" + mainAt + " wants=" + wantsAt + " call=" + callSmoke
            + " run=" + callRun + " swap=" + callSwap + " lit=" + smokeLit);
        string appVerPath = Path.Combine(FindRepoRoot(), "AppVersion.cs");
        string appVer = File.Exists(appVerPath) ? File.ReadAllText(appVerPath) : "";
        Check("smoke loads through KbIndex.FindRoot",
            appVer.IndexOf("KbIndex.FindRoot()", StringComparison.Ordinal) >= 0, "FindRoot");
    }
}
