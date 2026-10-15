// Pure helpers: TrendSample / TrendLog and DtcEvent / DtcTimeline. Explicit DateTime values only; no disk or UI.
using System;
using System.Collections.Generic;
using J1939Reader;

static partial class SelfTest
{
    static TrendSample TrendAt(DateTime t, double rpm)
    {
        return new TrendSample { Time = t, Rpm = rpm, CoolantC = double.NaN, OilKpa = double.NaN, BatteryV = double.NaN, FuelLph = double.NaN };
    }

    static List<Dtc> TlDtcs(params Dtc[] items)
    {
        return new List<Dtc>(items);
    }

    [SelfTestCase("TrendSample and TrendLog ring, throttle, range, CSV", Order = 305)]
    static void TrendLogTests()
    {
        Eq("Has number", TrendSample.Has(0.0), true);
        Eq("Has negative", TrendSample.Has(-1.5), true);
        Eq("Has NaN", TrendSample.Has(double.NaN), false);

        Eq("default capacity", new TrendLog().Capacity, 1800);
        Eq("capacity 0 floors to 2", new TrendLog(0).Capacity, 2);
        Eq("capacity 1 floors to 2", new TrendLog(1).Capacity, 2);
        Eq("negative capacity floors to 2", new TrendLog(-5).Capacity, 2);
        Eq("capacity 2 stays", new TrendLog(2).Capacity, 2);
        Eq("capacity 3 stays", new TrendLog(3).Capacity, 3);

        DateTime t0 = new DateTime(2026, 10, 15, 8, 0, 0);
        var log = new TrendLog(3);
        Eq("empty count", log.Count, 0);
        log.Add(TrendAt(t0, 1));
        log.Add(TrendAt(t0.AddSeconds(1), 2));
        Eq("count before wrap", log.Count, 2);
        Eq("first index before wrap", log[0].Rpm, 1);
        log.Add(TrendAt(t0.AddSeconds(2), 3));
        log.Add(TrendAt(t0.AddSeconds(3), 4));
        log.Add(TrendAt(t0.AddSeconds(4), 5));
        Eq("count caps at capacity", log.Count, 3);
        Eq("wrap drops oldest, [0]", log[0].Rpm, 3);
        Eq("wrap order [1]", log[1].Rpm, 4);
        Eq("wrap order [2]", log[2].Rpm, 5);
        List<TrendSample> copy = log.Copy();
        Eq("Copy count", copy.Count, 3);
        Eq("Copy oldest first", copy[0].Rpm, 3);
        Eq("Copy newest last", copy[2].Rpm, 5);
        bool threw = false;
        try { double r = log[3].Rpm; Eq("unreachable", r, 0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Eq("index past count throws", threw, true);
        threw = false;
        try { double r = log[-1].Rpm; Eq("unreachable", r, 0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Eq("negative index throws", threw, true);

        log.Clear();
        Eq("Clear empties", log.Count, 0);

        var th = new TrendLog(10);
        Eq("first offer taken", th.Offer(TrendAt(t0, 1)), true);
        Eq("offer 500 ms later refused", th.Offer(TrendAt(t0.AddMilliseconds(500), 2)), false);
        Eq("offer 949 ms later refused", th.Offer(TrendAt(t0.AddMilliseconds(949), 3)), false);
        Eq("offer 950 ms later taken", th.Offer(TrendAt(t0.AddMilliseconds(950), 4)), true);
        Eq("offer 50 ms after last taken is refused", th.Offer(TrendAt(t0.AddMilliseconds(1000), 5)), false);
        Eq("throttle keeps only taken samples", th.Count, 2);
        Eq("throttle second sample", th[1].Rpm, 4);
        th.Clear();
        Eq("offer after Clear taken", th.Offer(TrendAt(t0.AddMilliseconds(1000), 6)), true);

        var s1 = new TrendSample { Rpm = 1500, CoolantC = 90, OilKpa = 300, BatteryV = 13.5, FuelLph = 12 };
        Eq("Value rpm", TrendLog.Value(s1, TrendChannel.Rpm), 1500);
        Eq("Value coolant", TrendLog.Value(s1, TrendChannel.Coolant), 90);
        Eq("Value oil", TrendLog.Value(s1, TrendChannel.Oil), 300);
        Eq("Value battery", TrendLog.Value(s1, TrendChannel.Battery), 13.5);
        Eq("Value fuel", TrendLog.Value(s1, TrendChannel.Fuel), 12);
        Eq("Value unknown channel is NaN", double.IsNaN(TrendLog.Value(s1, (TrendChannel)99)), true);
        Eq("Label rpm", TrendLog.Label(TrendChannel.Rpm), "RPM");
        Eq("Label oil", TrendLog.Label(TrendChannel.Oil), "Oil kPa");
        Eq("Label battery", TrendLog.Label(TrendChannel.Battery), "Battery V");
        Eq("Label fuel", TrendLog.Label(TrendChannel.Fuel), "Fuel L/h");
        Eq("Label unknown", TrendLog.Label((TrendChannel)99), "?");

        double mn, mx;
        var rs = new List<TrendSample> { TrendAt(t0, 800), TrendAt(t0, double.NaN), TrendAt(t0, 2100), TrendAt(t0, 1200) };
        Eq("Range found", TrendLog.Range(rs, TrendChannel.Rpm, out mn, out mx), true);
        Eq("Range min", mn, 800);
        Eq("Range max", mx, 2100);
        Eq("Range all-NaN channel", TrendLog.Range(rs, TrendChannel.Oil, out mn, out mx), false);
        Eq("Range empty list", TrendLog.Range(new List<TrendSample>(), TrendChannel.Rpm, out mn, out mx), false);

        var row = new TrendSample
        {
            Time = t0, Rpm = 1500.4, CoolantC = double.NaN, OilKpa = 300.6, BatteryV = 13.456,
            FuelLph = 12.5, Red = true, Amber = false, ActiveCodes = 2
        };
        string nl = Environment.NewLine;
        string head = "time,rpm,coolant_c,oil_kpa,battery_v,fuel_lph,red,amber,active_codes" + nl;
        Eq("Trend CSV header and row",
            TrendLog.Csv(new List<TrendSample> { row }),
            head + "2026-10-15T08:00:00.0000000,1500,,301,13.46,12.50,1,0,2" + nl);
        Eq("Trend CSV empty is header only", TrendLog.Csv(new List<TrendSample>()), head);
    }

    [SelfTestCase("DtcTimeline update, order, CSV and DtcEvent text", Order = 306)]
    static void DtcTimelineTests()
    {
        DateTime t0 = new DateTime(2026, 10, 15, 8, 0, 0);
        var derate = new Dtc { Spn = 5246, Fmi = 0, Occ = 1, Name = "Derate", FmiText = "Above" };
        var tl = new DtcTimeline();
        Eq("new timeline empty", tl.Count, 0);

        tl.Update(TlDtcs(derate), t0, 180);
        Eq("new code makes one event", tl.Count, 1);
        DtcEvent e = tl.InOrder()[0];
        Eq("Key", e.Key, "5246/0");
        Eq("FirstSeen", e.FirstSeen, t0);
        Eq("LastSeen starts at first", e.LastSeen, t0);
        Eq("RpmAtFirst", e.RpmAtFirst, 180);
        Eq("RpmAtLast starts at first", e.RpmAtLast, 180);
        Eq("Active", e.Active, true);
        Eq("Cycles starts at 1", e.Cycles, 1);
        Eq("Duration under a second", e.Duration(), "<1s");
        Eq("ToString active", e.ToString(),
            "08:00:00  ON     SPN 5246   FMI 0   <1s   @180   RPM  Derate");

        var again = new Dtc { Spn = 5246, Fmi = 0, Occ = 3, Name = "Derate", FmiText = "Above" };
        tl.Update(TlDtcs(again), t0.AddSeconds(10), 900);
        Eq("repeat keeps one event", tl.Count, 1);
        Eq("repeat updates LastSeen", e.LastSeen, t0.AddSeconds(10));
        Eq("repeat keeps FirstSeen", e.FirstSeen, t0);
        Eq("repeat keeps RpmAtFirst", e.RpmAtFirst, 180);
        Eq("repeat updates RpmAtLast", e.RpmAtLast, 900);
        Eq("repeat updates Occ", e.Occ, 3);
        Eq("Duration seconds", e.Duration(), "10s");

        tl.Update(TlDtcs(), t0.AddSeconds(20), 0);
        Eq("absent code flips inactive", e.Active, false);
        Eq("cleared code is kept", tl.Count, 1);
        Eq("cleared keeps LastSeen", e.LastSeen, t0.AddSeconds(10));
        Eq("ToString cleared", e.ToString(),
            "08:00:00  cleared SPN 5246   FMI 0   10s   @180   RPM  Derate");
        tl.Update(null, t0.AddSeconds(25), 0);
        Eq("null list is harmless", tl.Count, 1);
        Eq("still inactive after null", e.Active, false);

        tl.Update(TlDtcs(again), t0.AddSeconds(30), 1200);
        Eq("comeback is same event", tl.Count, 1);
        Eq("comeback active", e.Active, true);
        Eq("comeback bumps Cycles", e.Cycles, 2);
        Eq("comeback resets FirstSeen", e.FirstSeen, t0.AddSeconds(30));
        Eq("comeback RpmAtFirst", e.RpmAtFirst, 1200);
        Eq("ToString with cycles", e.ToString(),
            "08:00:30  ON     SPN 5246   FMI 0   <1s   @1200  RPM  Derate  (x2)");

        var low = new Dtc { Spn = 100, Fmi = 1, Occ = 1, Name = null, FmiText = "Low" };
        tl.Update(TlDtcs(low), t0.AddSeconds(40), 1000);
        Eq("second code adds event", tl.Count, 2);
        Eq("derate went inactive", e.Active, false);
        List<DtcEvent> order = tl.InOrder();
        Eq("InOrder first", order[0].Key, "5246/0");
        Eq("InOrder second", order[1].Key, "100/1");
        List<DtcEvent> recent = tl.Recent();
        Eq("Recent newest first", recent[0].Key, "100/1");
        Eq("Recent oldest last", recent[1].Key, "5246/0");
        Eq("null Name becomes empty", order[1].Name, "");
        Eq("ToString falls back to FmiText", order[1].ToString(),
            "08:00:40  ON     SPN 100    FMI 1   <1s   @1000  RPM  Low");

        var long1 = new DtcEvent { FirstSeen = t0, LastSeen = t0.AddSeconds(59) };
        Eq("Duration 59 s", long1.Duration(), "59s");
        long1.LastSeen = t0.AddSeconds(125);
        Eq("Duration minutes", long1.Duration(), "2m05s");
        var nan = new DtcEvent { Spn = 7, Fmi = 3, FirstSeen = t0, LastSeen = t0, FmiText = "x" };
        Eq("ToString NaN rpm shows dash", nan.ToString().IndexOf("@—") >= 0, true);

        tl.Clear();
        Eq("Clear empties", tl.Count, 0);
        Eq("Clear empties InOrder", tl.InOrder().Count, 0);
        tl.Update(TlDtcs(derate), t0.AddSeconds(50), 500);
        Eq("after Clear a code is new again", tl.InOrder()[0].Cycles, 1);

        var ct = new DtcTimeline();
        var quoted = new Dtc { Spn = 5246, Fmi = 0, Occ = 2, Name = "Derate, hard", FmiText = "Above \"normal\"" };
        ct.Update(TlDtcs(quoted), t0, 180);
        ct.Update(TlDtcs(quoted, new Dtc { Spn = 1, Fmi = 2 }), t0.AddSeconds(5), double.NaN);
        string nl = Environment.NewLine;
        string head = "first_seen,last_seen,job,spn,fmi,state,cycles,occurrences,rpm_at_first,rpm_at_last,name,fmi_text" + nl;
        Eq("Timeline CSV rows", ct.Csv("JOB-1"),
            head +
            "2026-10-15T08:00:00.0000000,2026-10-15T08:00:05.0000000,\"JOB-1\",5246,0,active,1,2,180,,\"Derate, hard\",\"Above \"\"normal\"\"\"" + nl +
            "2026-10-15T08:00:05.0000000,2026-10-15T08:00:05.0000000,\"JOB-1\",1,2,active,1,0,,,\"\",\"\"" + nl);
        Eq("Timeline CSV empty is header only", new DtcTimeline().Csv(null), head);
    }
}
