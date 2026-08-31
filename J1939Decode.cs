using System;
using System.Collections.Generic;
using System.Text;

namespace J1939Reader
{
    internal sealed class Dtc
    {
        public int Spn;
        public int Fmi;
        public int Occ;
        public string Name;
        public string FmiText;
        public override string ToString()
        {
            string n = string.IsNullOrEmpty(Name) ? "" : "  " + Name;
            return "SPN " + Spn + "  FMI " + Fmi + "  occ " + Occ + "  " + FmiText + n;
        }
    }

    internal static class J1939Decode
    {
        public static ushort U16(byte[] d, int i)
        {
            if (d == null || i + 1 >= d.Length) return 0xFFFF;
            return (ushort)(d[i] | (d[i + 1] << 8));
        }

        public static uint U32(byte[] d, int i)
        {
            if (d == null || i + 3 >= d.Length) return 0xFFFFFFFF;
            return (uint)(d[i] | (d[i + 1] << 8) | (d[i + 2] << 16) | (d[i + 3] << 24));
        }

        public static string SpnName(int spn)
        {
            return Names.Spn(spn);
        }

        public static string FmiName(int fmi)
        {
            switch (fmi)
            {
                case 0: return "above normal (most severe)";
                case 1: return "below normal (most severe)";
                case 2: return "data erratic";
                case 3: return "voltage high";
                case 4: return "voltage low";
                case 5: return "current low";
                case 6: return "current high";
                case 7: return "not responding";
                case 8: return "abnormal frequency";
                case 9: return "abnormal update rate (not talking)";
                case 10: return "abnormal rate of change";
                case 11: return "root cause unknown";
                case 12: return "bad intelligent device";
                case 13: return "out of calibration";
                case 14: return "special instruction";
                case 15: return "above normal (least severe)";
                case 16: return "above normal (moderate)";
                case 17: return "below normal (least severe)";
                case 18: return "below normal (moderate)";
                case 19: return "received network data in error";
                case 31: return "condition exists";
                default: return "FMI " + fmi;
            }
        }

        public static void ParseDm(byte[] d, List<Dtc> list, out bool red, out bool amber)
        {
            bool protect, mil;
            ParseDm(d, list, out red, out amber, out protect, out mil);
        }

        /// <summary>DM1/DM2 byte 0 carries four lamps: MIL 7-6, red stop 5-4, amber 3-2, protect 1-0.</summary>
        public static void ParseDm(byte[] d, List<Dtc> list, out bool red, out bool amber, out bool protect, out bool mil)
        {
            red = false; amber = false; protect = false; mil = false;
            if (d == null || d.Length < 2) return;
            int b0 = d[0];
            protect = (b0 & 3) == 1;
            amber = ((b0 >> 2) & 3) == 1;
            red = ((b0 >> 4) & 3) == 1;
            mil = ((b0 >> 6) & 3) == 1;
            for (int i = 2; i + 3 < d.Length; i += 4)
            {
                if (d[i] == 0xFF && d[i + 1] == 0xFF) continue;
                int spn = d[i] | (d[i + 1] << 8) | ((d[i + 2] & 0xE0) << 11);
                int fmi = d[i + 2] & 0x1F;
                int oc = d[i + 3] & 0x7F;
                if (spn == 0 || spn >= 0x7FFFF) continue;
                bool dup = false;
                foreach (Dtc x in list)
                    if (x.Spn == spn && x.Fmi == fmi) { dup = true; break; }
                if (dup) continue;
                list.Add(new Dtc
                {
                    Spn = spn,
                    Fmi = fmi,
                    Occ = oc,
                    Name = SpnName(spn),
                    FmiText = FmiName(fmi)
                });
            }
        }

        public static double Rpm(byte[] d)
        {
            if (d == null || d.Length < 5) return -1;
            int raw = U16(d, 3);
            if (raw == 0xFFFF) return -1;
            return raw * 0.125;
        }

        public static string DefText(byte[] d)
        {
            if (d == null || d.Length < 2) return "n/a";
            if (d[0] >= 0xFB) return "n/a (not talking)";
            return (d[0] * 0.4).ToString("0.0") + "%  temp " +
                   (d[1] >= 0xFB ? "n/a" : ((d[1] - 40).ToString() + " C"));
        }

        public static string TempC(byte b)
        {
            if (b >= 0xFB) return "n/a";
            return (b - 40).ToString() + " C";
        }

        public static string PressureKpa(byte b, int scale)
        {
            if (b >= 0xFB) return "n/a";
            int k = b * scale;
            return k + " kPa (" + (k * 0.145).ToString("0.0") + " psi)";
        }

        public static string BatteryV(byte[] d)
        {
            if (d == null || d.Length < 6) return "n/a";
            int raw = U16(d, 4);
            if (raw >= 0xFB00) return "n/a";
            return (raw * 0.05).ToString("0.00") + " V";
        }

        public static string FuelRate(byte[] d)
        {
            if (d == null || d.Length < 2) return "n/a";
            int raw = U16(d, 0);
            if (raw >= 0xFB00) return "n/a";
            return (raw * 0.05).ToString("0.00") + " L/h";
        }

        /// <summary>PGN 0xFEE5 total engine hours, 0.05 h/bit in bytes 1-4.</summary>
        public static string Hours(byte[] d)
        {
            if (d == null || d.Length < 4) return null;
            uint raw = U32(d, 0);
            if (raw >= 0xFAFFFFFF) return null;
            return (raw * 0.05).ToString("0.0") + " h";
        }

        public static string Ascii(byte[] d)
        {
            return Ascii(d, false);
        }

        /// <summary>
        /// J1939 ASCII strings are '*'-delimited. VIN is one field so the star is noise; component ID
        /// packs make/model/serial/unit and needs the fields kept apart.
        /// </summary>
        public static string Ascii(byte[] d, bool keepFields)
        {
            if (d == null || d.Length == 0) return "";
            var sb = new StringBuilder(d.Length + 8);
            for (int i = 0; i < d.Length; i++)
            {
                byte b = d[i];
                if (b == '*')
                {
                    if (keepFields && sb.Length > 0) sb.Append("  ·  ");
                    continue;
                }
                if (b == 0 || b == 0xFF) continue;
                if (b >= 32 && b < 127) sb.Append((char)b);
            }
            return sb.ToString().Trim().Trim('·').Trim();
        }
    }
}
