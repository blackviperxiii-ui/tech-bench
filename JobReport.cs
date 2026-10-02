using System;
using System.Collections.Generic;
using System.Text;

namespace J1939Reader
{
    internal sealed class ReportData
    {
        public DateTime When = DateTime.Now;
        public string JobTag = "";
        public string Adapter = "";
        public string Vin = "";
        public string Sw = "";
        public string CompId = "";
        public string Hours = "";
        public double Rpm = double.NaN;
        public bool Red, Amber, Protect, Mil;
        public string Def = "—", Coolant = "—", Oil = "—", Batt = "—", Fuel = "—";
        public List<Dtc> Active = new List<Dtc>();
        public List<Dtc> Prev = new List<Dtc>();
        public List<DtcEvent> Timeline = new List<DtcEvent>();
        public List<string> Markers = new List<string>();
        public string Diff = "";
        public string HistoryNote = "";
        /// <summary>The last DM11/DM3 clear report from this hookup, if one was run.</summary>
        public string CodeClear = "";
    }

    /// <summary>
    /// A one-page summary a tech can staple to a work order. Text generation is separate from
    /// printing so it can be verified without a printer.
    /// </summary>
    internal static class JobReport
    {
        public static List<string> Lines(ReportData d)
        {
            var L = new List<string>();
            L.Add("TECH BENCH — DIAGNOSTIC REPORT");
            L.Add(new string('=', 64));
            L.Add("Job:      " + (d.JobTag.Trim().Length > 0 ? d.JobTag : "(not entered)"));
            L.Add("Date:     " + d.When.ToString("yyyy-MM-dd HH:mm"));
            if (d.Adapter.Length > 0) L.Add("Adapter:  " + d.Adapter);
            L.Add("");

            L.Add("ENGINE IDENTITY");
            L.Add("  VIN / NAME:  " + Or(d.Vin));
            L.Add("  Software:    " + Or(d.Sw));
            L.Add("  Component:   " + Or(d.CompId));
            L.Add("  Engine hours:" + " " + Or(d.Hours));
            L.Add("");

            L.Add("READINGS AT REPORT TIME");
            L.Add("  RPM:      " + (double.IsNaN(d.Rpm) ? "—" : d.Rpm.ToString("0")));
            L.Add("  Lamps:    Red Stop " + OnOff(d.Red) + "   Amber " + OnOff(d.Amber)
                             + "   Protect " + OnOff(d.Protect) + "   MIL " + OnOff(d.Mil));
            L.Add("  DEF:      " + d.Def);
            L.Add("  Coolant:  " + d.Coolant + "     Oil: " + d.Oil);
            L.Add("  Battery:  " + d.Batt + "     Fuel rate: " + d.Fuel);
            L.Add("");

            L.Add("ACTIVE FAULTS (DM1)");
            if (d.Active.Count == 0) L.Add("  (none)");
            else foreach (Dtc t in d.Active) L.Add("  " + t);
            L.Add("");

            L.Add("PREVIOUSLY ACTIVE (DM2)");
            if (d.Prev.Count == 0) L.Add("  (none)");
            else foreach (Dtc t in d.Prev) L.Add("  " + t);
            L.Add("");

            if (d.Timeline.Count > 0)
            {
                L.Add("FAULT TIMELINE THIS HOOKUP");
                int n = 0;
                foreach (DtcEvent e in d.Timeline)
                {
                    L.Add("  " + e);
                    if (++n >= 20) { L.Add("  … " + (d.Timeline.Count - n) + " more"); break; }
                }
                L.Add("");
            }

            if (d.Markers.Count > 0)
            {
                L.Add("MARKERS");
                foreach (string m in d.Markers) L.Add("  " + m);
                L.Add("");
            }

            if (d.CodeClear != null && d.CodeClear.Trim().Length > 0)
            {
                L.Add("LAST CODE CLEAR");
                foreach (string line in d.CodeClear.Replace("\r\n", "\n").Split('\n'))
                    if (line.Trim().Length > 0) L.Add("  " + line.TrimEnd());
                L.Add("");
            }

            if (d.Diff.Trim().Length > 0 && d.Diff.IndexOf("Need Snapshot", StringComparison.Ordinal) < 0)
            {
                L.Add("BEFORE / AFTER");
                foreach (string line in d.Diff.Replace("\r\n", "\n").Split('\n'))
                    if (line.Trim().Length > 0) L.Add("  " + line.TrimEnd());
                L.Add("");
            }

            if (d.HistoryNote.Trim().Length > 0)
            {
                L.Add("UNIT HISTORY");
                foreach (string line in d.HistoryNote.Replace("\r\n", "\n").Split('\n'))
                    if (line.Trim().Length > 0) L.Add("  " + line.TrimEnd());
                L.Add("");
            }

            L.Add(new string('-', 64));
            L.Add("Red Stop and Amber are DM1 lamp bits, not separate codes. They go out when the");
            L.Add("faults that set them are gone. No DEF/SCR or engine-protection function was");
            L.Add("disabled to produce this report.");
            L.Add("");
            L.Add("Tech: ______________________    Signature: ______________________");
            return L;
        }

        public static string Text(ReportData d)
        {
            var sb = new StringBuilder();
            foreach (string line in Lines(d)) sb.AppendLine(line);
            return sb.ToString();
        }

        static string Or(string s)
        {
            return string.IsNullOrWhiteSpace(s) ? "(not published)" : s;
        }

        static string OnOff(bool v) { return v ? "ON" : "off"; }
    }
}
