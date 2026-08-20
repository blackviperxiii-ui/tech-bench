using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;

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

    internal static class Names
    {
        public static string Sa(int sa)
        {
            switch (sa)
            {
                case 0: return "ECM 1";
                case 3: return "Trans / retarder";
                case 11: return "Brakes";
                case 15: return "Retarder";
                case 23: return "Instrument cluster";
                case 33: return "Body controller";
                case 37: return "Cab controller";
                case 47: return "Ignition";
                case 48: return "Compressor controller";
                case 49: return "Cab display";
                case 59: return "Beede / trans display";
                case 128: return "Grayhill keypad";
                case 249: return "This tool / off-board diag";
                case 253: return "OEM reserved";
                case 255: return "Broadcast";
                default: return "SA " + sa;
            }
        }

        public static string Pgn(int pgn)
        {
            switch (pgn)
            {
                case 0x0000: return "TSC1 speed/torque";
                case 0xEA00: return "Request";
                case 0xEB00: return "TP.DT";
                case 0xEC00: return "TP.CM";
                case 0xEE00: return "Address claim";
                case 0xF001: return "ERC1 retarder";
                case 0xF003: return "EEC2 load/pedal";
                case 0xF004: return "EEC1 RPM";
                case 0xF00A: return "EEC3";
                case 0xFE56: return "DEF tank 1";
                case 0xFECA: return "DM1 active DTCs";
                case 0xFECB: return "DM2 previous DTCs";
                case 0xFECC: return "DM3 clear previous";
                case 0xFED3: return "DM11 clear active";
                case 0xFEDA: return "Software ID";
                case 0xFEEB: return "Component ID";
                case 0xFEEC: return "VIN";
                case 0xFEED: return "Reference weight";
                case 0xFEEE: return "Temps (coolant)";
                case 0xFEEF: return "Oil / fuel pressure";
                case 0xFEF1: return "CCVS speed";
                case 0xFEF2: return "Fuel rate";
                case 0xFEF6: return "Inlet air";
                case 0xFEF7: return "Electrical / battery";
                case 0xFEFC: return "Fuel level";
                case 0xDF00: return "DM13 stop/start bcast";
                default: return "";
            }
        }
    }

    internal static class SessionIo
    {
        public static string Folder()
        {
            string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "TechBench-sessions");
            Directory.CreateDirectory(d);
            return d;
        }

        public static string Stamp()
        {
            return DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        public static string SaveTextCsv(string body, string csv, string jobTag)
        {
            string dir = Folder();
            string baseName = "session_" + Stamp();
            if (!string.IsNullOrWhiteSpace(jobTag))
            {
                var sb = new StringBuilder();
                foreach (char c in jobTag.Trim())
                    sb.Append(char.IsLetterOrDigit(c) ? c : '_');
                string tag = sb.ToString().Trim('_');
                if (tag.Length > 40) tag = tag.Substring(0, 40);
                if (tag.Length > 0) baseName = tag + "_" + baseName;
            }
            string txt = Path.Combine(dir, baseName + ".txt");
            string csvPath = Path.Combine(dir, baseName + ".csv");
            File.WriteAllText(txt, body, Encoding.UTF8);
            File.WriteAllText(csvPath, csv, Encoding.UTF8);
            return txt;
        }

        public static string Screenshot(Control c)
        {
            if (c == null) return null;
            string path = Path.Combine(Folder(), "shot_" + Stamp() + ".png");
            using (var bmp = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height)))
            {
                c.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(path, ImageFormat.Png);
            }
            return path;
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
            sb.AppendLine("Codes gone (in A, not B):");
            AppendMissing(sb, a.Active, b.Active);
            sb.AppendLine("Codes new (in B, not A):");
            AppendMissing(sb, b.Active, a.Active);
            sb.AppendLine("Unchanged active:");
            foreach (string s in a.Active)
                if (b.Active.Contains(s)) sb.AppendLine("  " + s);
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
