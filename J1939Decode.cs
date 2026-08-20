using System;
using System.Collections.Generic;

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

        public static string SpnName(int spn)
        {
            switch (spn)
            {
                case 91: return "Accelerator pedal";
                case 94: return "Fuel delivery pressure";
                case 97: return "Water in fuel";
                case 98: return "Oil level";
                case 100: return "Oil pressure";
                case 102: return "Boost / MAP";
                case 105: return "Intake manifold temp";
                case 110: return "Coolant temp";
                case 157: return "Fuel rail pressure";
                case 168: return "Battery voltage";
                case 190: return "Engine speed";
                case 611: return "System diagnostic";
                case 629: return "Controller";
                case 639: return "J1939 network";
                case 651: return "Injector cylinder 1";
                case 677: return "Starter motor relay";
                case 723: return "Camshaft speed/position";
                case 1079: return "Sensor supply 1";
                case 1080: return "Sensor supply 2";
                case 1569: return "Engine protection / DEF empty derate";
                case 1761: return "DEF tank level";
                case 2791: return "EGR valve";
                case 3031: return "DEF tank temp";
                case 3216: return "AT1 intake NOx";
                case 3226: return "AT1 outlet NOx";
                case 3361: return "DEF dosing unit";
                case 3363: return "DEF tank heater";
                case 3364: return "DEF tank quality";
                case 3515: return "DEF line heater";
                case 3516: return "DEF tank temp";
                case 4331: return "DEF pressure";
                case 4334: return "DEF doser pressure";
                case 5245: return "SCR inducement time";
                case 5246: return "SCR operator inducement severity";
                case 5392: return "DEF pump";
                case 5394: return "DEF pump state";
                default: return "";
            }
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
            red = false; amber = false;
            if (d == null || d.Length < 2) return;
            int b0 = d[0];
            amber = ((b0 >> 2) & 3) == 1;
            red = ((b0 >> 4) & 3) == 1;
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

        public static string Ascii(byte[] d)
        {
            if (d == null || d.Length == 0) return "";
            var chars = new char[d.Length];
            int n = 0;
            for (int i = 0; i < d.Length; i++)
            {
                byte b = d[i];
                if (b == 0 || b == 0xFF || b == '*') continue;
                if (b >= 32 && b < 127) chars[n++] = (char)b;
            }
            return new string(chars, 0, n).Trim();
        }
    }
}
