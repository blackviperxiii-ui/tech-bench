// CodeBook (fault explainer) and FeatureBook (INLINE 7 switch rows) lookup SelfTest cases.
using System.Collections.Generic;
using J1939Reader;

static partial class SelfTest
{
    const string Dash = "—";

    static string[] SearchLines(string text)
    {
        return text.Replace("\r\n", "\n").Split('\n');
    }

    [SelfTestCase("CodeBook.Explain layout and lookups", Order = 295)]
    static void CodeBookExplainTests()
    {
        string t = CodeBook.Explain(5246, 0);
        string[] ln = SearchLines(t);
        Eq("5246/0 title is the SPN name", ln[0], "SCR operator inducement severity");
        Eq("5246/0 SPN+FMI line", ln[1], "SPN 5246   FMI 0 " + Dash + " above normal (most severe)");
        Eq("5246/0 blank after header", ln[2], "");
        Check("5246/0 body is the inducement text", ln[3].StartsWith("SCR operator inducement severity. FMI 0 = most severe"), ln[3]);
        Eq("5246/0 FMI heading present", System.Array.IndexOf(ln, "This FMI means:") > 0, true);
        Eq("5246/0 FMI 0 text", System.Array.IndexOf(ln, "Value is out of range in the most severe band. The sensor is talking; the number is too high or too low.") > 0, true);
        Eq("5246/0 start impact is YES", t.Contains("YES. This is in the no-fuel / Red Stop path on T4F."), true);
        Eq("5246/0 checks mention Guidanz reset", System.Array.IndexOf(ln, "- Then Guidanz aftertreatment reset " + Dash + " a generic clear often will not drop 5246") > 0, true);
        Eq("sections end with checks", t.TrimEnd().EndsWith("that wrecks the aftertreatment and the engine."), true);

        // SPN with no body/checks entry falls back to the generic text and the FMI name.
        string u = CodeBook.Explain(99999, 7);
        string[] ul = SearchLines(u);
        Eq("unknown SPN title", ul[0], "SPN 99999");
        Eq("unknown SPN header", ul[1], "SPN 99999   FMI 7 " + Dash + " not responding");
        Eq("unknown SPN body names the SPN", u.Contains("see Cummins troubleshooting for SPN 99999'."), true);
        Eq("unknown FMI falls back to FmiName + period", System.Array.IndexOf(ul, "not responding.") > 0, true);
        Eq("unknown SPN impact", System.Array.IndexOf(ul, "Usually not by itself. Watch whether Red Stop / 5246 is also active.") > 0, true);
        Eq("unknown SPN checks", System.Array.IndexOf(ul, "- Confirm whether the SPN is n/a (not talking) vs a real out-of-range number") > 0, true);

        // DEF tank header FMI 9: "not talking", indirect inducement, whole-module check.
        string d = CodeBook.Explain(1761, 9);
        string[] dl = SearchLines(d);
        Eq("1761/9 header", dl[1], "SPN 1761   FMI 9 " + Dash + " abnormal update rate (not talking)");
        Check("1761/9 FMI text is the update-rate text", d.Contains("Abnormal update rate " + Dash + " the ECM is not receiving messages from that device. This is NOT a 'wrong number'."), "missing");
        Eq("1761/9 impact is indirect", d.Contains("Indirectly. These do not inject fuel themselves"), true);
        Eq("1761/9 whole-module check", d.Contains("- All three FMI 9 together = whole module offline, not 'low DEF'"), true);

        // Impact tiers keyed on SPN only.
        Eq("190 impact", CodeBook.Explain(190, 4).Contains("Can. No crank RPM or no cam/crank sync = no inject."), true);
        Eq("723 impact", CodeBook.Explain(723, 2).Contains("Can. No crank RPM or no cam/crank sync = no inject."), true);
        Eq("157 impact", CodeBook.Explain(157, 3).Contains("Can, if rail/supply never builds while cranking."), true);
        Eq("94 impact", CodeBook.Explain(94, 3).Contains("Can, if rail/supply never builds while cranking."), true);
        Eq("110 impact is the default", CodeBook.Explain(110, 3).Contains("Usually not by itself."), true);

        // FMI text tiers.
        Eq("FMI 1 shares the severe-band text", CodeBook.Explain(110, 1).Contains("Value is out of range in the most severe band."), true);
        Eq("FMI 3 text", CodeBook.Explain(110, 3).Contains("Signal voltage shorted high (usually 5 V or battery)."), true);
        Eq("FMI 4 text", CodeBook.Explain(110, 4).Contains("Signal voltage shorted low (ground)."), true);
        Eq("FMI 18 shares the moderate text", CodeBook.Explain(110, 18).Contains("Moderate over/under range. Sensor is alive; reading is off."), true);
        Eq("FMI 31 text", CodeBook.Explain(110, 31).Contains("Condition exists. Often a status/result"), true);

        // Case groups map to the same body.
        Eq("1079 has the 5 V body", CodeBook.Explain(1079, 3).Contains("5 V sensor supply. If this is shorted"), true);
        Eq("1080 has the 5 V body", CodeBook.Explain(1080, 3).Contains("5 V sensor supply. If this is shorted"), true);
        Eq("4334 has the DEF pressure body", CodeBook.Explain(4334, 3).Contains("DEF system pressure at the doser."), true);
        Eq("5394 has the DEF pump body", CodeBook.Explain(5394, 3).Contains("DEF pump command/state."), true);
        Eq("different SPN, different body", CodeBook.Explain(110, 3).Contains("5 V sensor supply."), false);
    }

    static SwitchRow FindRow(List<SwitchRow> rows, string id)
    {
        for (int i = 0; i < rows.Count; i++) if (rows[i].Id == id) return rows[i];
        return null;
    }

    [SelfTestCase("FeatureBook.Live rows", Order = 296)]
    static void FeatureBookLiveTests()
    {
        List<SwitchRow> off = FeatureBook.Live(false, true, true, true, true, true, 900, "12.5 %");
        Eq("disconnected returns one row", off.Count, 1);
        Eq("disconnected id", off[0].Id, "link");
        Eq("disconnected state ignores lamp flags", off[0].State, "OFF");
        Eq("disconnected short", off[0].Short, "Not talking to the ECM.");
        Eq("disconnected ToString", off[0].ToString(), "OFF" + new string(' ', 9) + "INLINE connection  " + Dash + "  Not talking to the ECM.");

        List<SwitchRow> quiet = FeatureBook.Live(true, false, false, false, false, false, 0, "");
        string ids = "";
        for (int i = 0; i < quiet.Count; i++) ids += (i > 0 ? "," : "") + quiet[i].Id;
        Eq("connected row order", ids, "red,amber,induc,tank,defval,prot,run");
        Eq("red off", FindRow(quiet, "red").State, "off");
        Eq("red off short", FindRow(quiet, "red").Short, "No Red Stop " + Dash + " no lamp-only reset needed.");
        Eq("amber off", FindRow(quiet, "amber").State, "off");
        Eq("inducement off", FindRow(quiet, "induc").State, "off");
        Eq("tank check data", FindRow(quiet, "tank").State, "check data");
        Eq("empty DEF text shows dash", FindRow(quiet, "defval").State, Dash);
        Eq("protection off", FindRow(quiet, "prot").State, "off");
        Eq("rpm 0 is STOPPED", FindRow(quiet, "run").State, "STOPPED");
        Eq("rpm 0 short", FindRow(quiet, "run").Short, "RPM 0");

        List<SwitchRow> hot = FeatureBook.Live(true, true, true, true, true, true, 850, "n/a (not talking)");
        Eq("red ON", FindRow(hot, "red").State, "ON");
        Eq("red ON short", FindRow(hot, "red").Short, "ECM is commanding no-fuel / shutdown.");
        Eq("amber ON", FindRow(hot, "amber").State, "ON");
        Eq("amber ON short", FindRow(hot, "amber").Short, "A warning / derate path is active.");
        Eq("inducement LOCKED", FindRow(hot, "induc").State, "LOCKED");
        Eq("inducement short", FindRow(hot, "induc").Short, "Most-severe inducement " + Dash + " will not fuel.");
        Eq("tank NOT TALKING", FindRow(hot, "tank").State, "NOT TALKING");
        Eq("tank short", FindRow(hot, "tank").Short, "FMI 9 on 1761/3031/3364 " + Dash + " module offline.");
        Eq("DEF text passes through", FindRow(hot, "defval").State, "n/a (not talking)");
        Eq("protection ON", FindRow(hot, "prot").State, "ON");
        Eq("rpm 850 is RUNNING", FindRow(hot, "run").State, "RUNNING");
        Eq("rpm 850 short", FindRow(hot, "run").Short, "RPM 850");
        Eq("row ToString pads state to 10", FindRow(hot, "red").ToString(),
            "ON" + new string(' ', 10) + "Red Stop lamp  " + Dash + "  ECM is commanding no-fuel / shutdown.");

        // Lamp flags are independent of each other.
        List<SwitchRow> redOnly = FeatureBook.Live(true, true, false, false, false, false, 0, "x");
        Eq("red only: red", FindRow(redOnly, "red").State, "ON");
        Eq("red only: amber", FindRow(redOnly, "amber").State, "off");
        Eq("red only: protection", FindRow(redOnly, "prot").State, "off");

        // RPM thresholds: <0 unknown, <50 stopped, <400 cranking, else running.
        Eq("rpm -1 unknown state", FindRow(FeatureBook.Live(true, false, false, false, false, false, -1, ""), "run").State, Dash);
        Eq("rpm -1 unknown short", FindRow(FeatureBook.Live(true, false, false, false, false, false, -1, ""), "run").Short, "No EEC1 yet.");
        Eq("rpm 49 stopped", FindRow(FeatureBook.Live(true, false, false, false, false, false, 49, ""), "run").State, "STOPPED");
        Eq("rpm 50 cranking", FindRow(FeatureBook.Live(true, false, false, false, false, false, 50, ""), "run").State, "CRANKING");
        Eq("rpm 399 cranking", FindRow(FeatureBook.Live(true, false, false, false, false, false, 399, ""), "run").State, "CRANKING");
        Eq("rpm 400 running", FindRow(FeatureBook.Live(true, false, false, false, false, false, 400, ""), "run").State, "RUNNING");
    }

    [SelfTestCase("FeatureBook.GuidanzFeatures list", Order = 297)]
    static void FeatureBookGuidanzTests()
    {
        List<SwitchRow> g = FeatureBook.GuidanzFeatures();
        Eq("guidanz row count", g.Count, 13);
        string ids = "";
        for (int i = 0; i < g.Count; i++) ids += (i > 0 ? "," : "") + g[i].Id;
        Eq("guidanz row order", ids, "ep,lop,hct,idle,warm,pto,fan,acc,max,start,def,vgt,egr");
        Eq("engine protection name", FindRow(g, "ep").Name, "Engine Protection Shutdown");
        Eq("engine protection state", FindRow(g, "ep").State, "Guidanz");
        Eq("DEF row is the do-not-disable row", FindRow(g, "def").State, "DO NOT DISABLE");
        Eq("DEF row short", FindRow(g, "def").Short, "Must stay enabled. This app will not add an off switch.");
        int notGuidanz = 0;
        for (int i = 0; i < g.Count; i++) if (g[i].State != "Guidanz") notGuidanz++;
        Eq("only the DEF row is not a Guidanz item", notGuidanz, 1);
        Eq("every row has detail text", g.TrueForAll(delegate(SwitchRow r) { return !string.IsNullOrEmpty(r.Detail); }), true);
    }
}
