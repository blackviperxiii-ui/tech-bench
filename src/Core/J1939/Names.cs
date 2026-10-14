using System;
using System.Collections.Generic;

namespace J1939Reader
{
    /// <summary>
    /// SPN / PGN / source-address labels. The built-in tables are the shop defaults; the knowledge
    /// base can extend or override any of them via data\j1939-names.json so a new SPN does not need
    /// a rebuild to show a name.
    /// </summary>
    internal static class Names
    {
        static Dictionary<int, string> _spnOverride;
        static Dictionary<int, string> _pgnOverride;
        static Dictionary<int, string> _saOverride;

        public static int OverrideCount { get; private set; }

        public static void Load(Dictionary<int, string> spn, Dictionary<int, string> pgn, Dictionary<int, string> sa)
        {
            _spnOverride = spn;
            _pgnOverride = pgn;
            _saOverride = sa;
            OverrideCount = Size(spn) + Size(pgn) + Size(sa);
        }

        public static void Reset()
        {
            _spnOverride = null;
            _pgnOverride = null;
            _saOverride = null;
            OverrideCount = 0;
        }

        static int Size(Dictionary<int, string> d) { return d == null ? 0 : d.Count; }

        static string Lookup(Dictionary<int, string> d, int key)
        {
            if (d == null) return null;
            string v;
            if (!d.TryGetValue(key, out v)) return null;
            return string.IsNullOrEmpty(v) ? null : v;
        }

        public static string Spn(int spn)
        {
            string over = Lookup(_spnOverride, spn);
            if (over != null) return over;
            return BuiltInSpn(spn);
        }

        public static string Sa(int sa)
        {
            string over = Lookup(_saOverride, sa);
            if (over != null) return over;
            return BuiltInSa(sa);
        }

        /// <summary>"SA 0 ECM 1", or just "SA 61" when the address has no name.</summary>
        public static string SaLabel(int sa)
        {
            string bare = "SA " + sa;
            string name = Sa(sa);
            if (string.IsNullOrEmpty(name) || name == bare) return bare;
            return bare + " " + name;
        }

        public static string Pgn(int pgn)
        {
            string over = Lookup(_pgnOverride, pgn);
            if (over != null) return over;
            return BuiltInPgn(pgn);
        }

        static string BuiltInSa(int sa)
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

        static string BuiltInPgn(int pgn)
        {
            switch (pgn)
            {
                case 0x0000: return "TSC1 speed/torque";
                case 0xE800: return "ACK / NACK";
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
                case 0xFEE5: return "Engine hours";
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

        static string BuiltInSpn(int spn)
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
    }
}
