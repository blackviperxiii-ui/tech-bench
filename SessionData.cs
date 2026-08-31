using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace J1939Reader
{
    internal sealed class Snap
    {
        public DateTime Time;
        public string Label;
        public double Rpm;
        public bool Red, Amber;
        public string Def, Coolant, Oil, Batt, Fuel;
        public List<string> Active = new List<string>();
        public List<string> Prev = new List<string>();
    }

    internal sealed class PgnRow
    {
        public int Pgn, Sa, Count;
        public DateTime Last;
        public int LastLen;
    }

    internal sealed class SaRow
    {
        public int Sa, Count;
        public DateTime Last;
        public string NameHex;
        public bool Claimed;
    }

    /// <summary>
    /// Session paths, files and the before/after diff. Kept free of System.Drawing and WinForms
    /// (screen capture lives in Session.cs) so all of it can be exercised in SelfTest.
    /// </summary>
    internal static partial class SessionIo
    {
        static string _folder;

        /// <summary>
        /// Desktop first because that is where a tech will look, but a redirected or read-only Desktop
        /// must not take the Save button down with it.
        /// </summary>
        public static string Folder()
        {
            if (_folder != null) return _folder;
            Environment.SpecialFolder[] bases =
            {
                Environment.SpecialFolder.DesktopDirectory,
                Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolder.LocalApplicationData
            };
            foreach (Environment.SpecialFolder b in bases)
            {
                try
                {
                    string root = Environment.GetFolderPath(b);
                    if (string.IsNullOrEmpty(root)) continue;
                    string d = Path.Combine(root, "TechBench-sessions");
                    Directory.CreateDirectory(d);
                    _folder = d;
                    return d;
                }
                catch { }
            }
            _folder = Path.GetTempPath();
            return _folder;
        }

        public static string Stamp()
        {
            return DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        public static string BaseName(string jobTag)
        {
            string baseName = "session_" + Stamp();
            if (string.IsNullOrWhiteSpace(jobTag)) return baseName;
            var sb = new StringBuilder();
            foreach (char c in jobTag.Trim())
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            string tag = sb.ToString().Trim('_');
            if (tag.Length > 40) tag = tag.Substring(0, 40);
            return tag.Length > 0 ? tag + "_" + baseName : baseName;
        }

        /// <summary>
        /// Readings, DTCs, trend and timeline each go to their own CSV. One file with several column
        /// layouts is not a CSV any spreadsheet can open.
        /// </summary>
        public static string SaveSession(string body, string readingsCsv, string dtcCsv,
            string trendCsv, string timelineCsv, string jobTag)
        {
            string dir = Folder();
            string baseName = BaseName(jobTag);
            string txt = Path.Combine(dir, baseName + ".txt");
            File.WriteAllText(txt, body, Encoding.UTF8);
            Write(dir, baseName + "_readings.csv", readingsCsv);
            Write(dir, baseName + "_dtcs.csv", dtcCsv);
            Write(dir, baseName + "_trend.csv", trendCsv);
            Write(dir, baseName + "_timeline.csv", timelineCsv);
            return txt;
        }

        static void Write(string dir, string name, string content)
        {
            if (string.IsNullOrEmpty(content)) return;
            File.WriteAllText(Path.Combine(dir, name), content, Encoding.UTF8);
        }

        public static string LogError(string where, Exception ex)
        {
            try
            {
                string path = Path.Combine(Folder(), "techbench-errors.log");
                var sb = new StringBuilder();
                sb.AppendLine("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + where + " ===");
                sb.AppendLine(ex == null ? "(no exception object)" : ex.ToString());
                sb.AppendLine();
                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
                return path;
            }
            catch { return null; }
        }

        public static string Diff(Snap a, Snap b)
        {
            if (a == null || b == null) return "Need Snapshot A and Snapshot B first.";
            var sb = new StringBuilder();
            sb.AppendLine("BEFORE  " + a.Time.ToString("HH:mm:ss") + "  (" + a.Label + ")");
            sb.AppendLine("AFTER   " + b.Time.ToString("HH:mm:ss") + "  (" + b.Label + ")");
            sb.AppendLine();
            sb.AppendLine("RPM     " + FmtRpm(a.Rpm) + "  →  " + FmtRpm(b.Rpm));
            sb.AppendLine("Red     " + OnOff(a.Red) + "  →  " + OnOff(b.Red));
            sb.AppendLine("Amber   " + OnOff(a.Amber) + "  →  " + OnOff(b.Amber));
            sb.AppendLine("DEF     " + a.Def + "  →  " + b.Def);
            sb.AppendLine();
            sb.AppendLine("Active codes gone (in A, not B):");
            AppendMissing(sb, a.Active, b.Active);
            sb.AppendLine("Active codes new (in B, not A):");
            AppendMissing(sb, b.Active, a.Active);
            sb.AppendLine("Unchanged active:");
            int same = 0;
            foreach (string s in a.Active)
                if (b.Active.Contains(s)) { sb.AppendLine("  " + s); same++; }
            if (same == 0) sb.AppendLine("  (none)");
            // Previously-active is where a repair shows up: a fault that stopped recurring moves from
            // active into previous rather than disappearing. Snap.Prev was captured and never compared.
            sb.AppendLine();
            sb.AppendLine("Previously-active gone (in A, not B):");
            AppendMissing(sb, a.Prev, b.Prev);
            sb.AppendLine("Previously-active new (in B, not A):");
            AppendMissing(sb, b.Prev, a.Prev);
            return sb.ToString();
        }

        static string FmtRpm(double r) { return r < 0 ? "—" : r.ToString("0"); }
        static string OnOff(bool v) { return v ? "ON" : "off"; }

        static void AppendMissing(StringBuilder sb, List<string> have, List<string> other)
        {
            int n = 0;
            foreach (string s in have)
                if (!other.Contains(s)) { sb.AppendLine("  " + s); n++; }
            if (n == 0) sb.AppendLine("  (none)");
        }
    }
}
