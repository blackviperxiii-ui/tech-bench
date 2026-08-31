// Offline checks for the parts of Tech Bench that can be verified without an adapter or the real KB:
// J1939 decoding, BAM reassembly, and knowledge-base loading/search. Run with test.bat.
using System;
using System.Collections.Generic;
using System.IO;
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
        Console.WriteLine(_fail == 0 ? "ALL PASS" : (_fail + " FAILURES"));
        return _fail == 0 ? 0 : 1;
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
}
