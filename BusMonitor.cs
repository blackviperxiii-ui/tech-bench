using System;
using System.Collections.Generic;

namespace J1939Reader
{
    /// <summary>Everything one module on the bus has told us.</summary>
    internal sealed class ModuleFaults
    {
        public int Sa;
        public readonly List<Dtc> Active = new List<Dtc>();
        public readonly List<Dtc> Prev = new List<Dtc>();
        public bool Red, Amber, Protect, Mil;
        public DateTime LastDm1 = DateTime.MinValue;
        public DateTime LastDm2 = DateTime.MinValue;
        public readonly DtcTimeline Timeline = new DtcTimeline();

        public bool HasDm1 { get { return LastDm1 != DateTime.MinValue; } }
    }

    /// <summary>
    /// Frame routing and decoded state. Deliberately free of threading and WinForms so the whole
    /// decode path can be exercised offline in SelfTest.
    /// </summary>
    internal sealed class BusMonitor
    {
        public const int AutoEngineSa = -1;

        readonly BamAssembler _bam = new BamAssembler();
        readonly Dictionary<string, PgnRow> _pgns = new Dictionary<string, PgnRow>();
        readonly Dictionary<int, SaRow> _sas = new Dictionary<int, SaRow>();
        readonly Dictionary<int, ModuleFaults> _modules = new Dictionary<int, ModuleFaults>();
        readonly List<AckReply> _acks = new List<AckReply>();
        readonly TrendLog _trend = new TrendLog();

        const int MaxAcks = 64;
        /// <summary>A non-engine FE56 is only used after the engine has been quiet on FE56 this long.</summary>
        const double DefFallbackSeconds = 3.0;

        int _engineSaSetting = AutoEngineSa;
        int _engineSa = 0;

        public double Rpm = double.NaN;
        public double CoolantC = double.NaN;
        public double OilKpa = double.NaN;
        public double BatteryV = double.NaN;
        public double FuelLph = double.NaN;
        public double DefPct = double.NaN;
        public double DefTempC = double.NaN;
        public bool DefSilent = true;
        public AftState Aft = new AftState();
        /// <summary>Who the DEF/SCR readout came from: the engine, or the module that actually sends FE56.</summary>
        public int DefSa = -1;
        public DateTime LastDef = DateTime.MinValue;
        DateTime _lastEngineDef = DateTime.MinValue;
        public string Vin = "";
        public string Sw = "";
        public string CompId = "";
        public string HoursText = "";

        public DateTime LastFrame = DateTime.MinValue;
        public int Frames;

        public TrendLog Trend { get { return _trend; } }

        /// <summary>Which SA the engine readouts come from; AutoEngineSa follows whoever sends EEC1.</summary>
        public int EngineSaSetting
        {
            get { return _engineSaSetting; }
            set { _engineSaSetting = value; if (value >= 0) _engineSa = value; }
        }

        public int EngineSa { get { return _engineSa; } }

        public ModuleFaults Engine { get { return Module(_engineSa); } }

        /// <summary>The module if it has said anything; unlike Module() this never creates one.</summary>
        public ModuleFaults Peek(int sa)
        {
            ModuleFaults m;
            return _modules.TryGetValue(sa, out m) ? m : null;
        }

        public ModuleFaults Module(int sa)
        {
            ModuleFaults m;
            if (!_modules.TryGetValue(sa, out m))
            {
                m = new ModuleFaults { Sa = sa };
                _modules[sa] = m;
            }
            return m;
        }

        public List<int> ModulesWithFaults()
        {
            var list = new List<int>();
            foreach (KeyValuePair<int, ModuleFaults> kv in _modules)
                if (kv.Value.HasDm1) list.Add(kv.Key);
            list.Sort();
            return list;
        }

        /// <summary>Modules that have sent DM1 or DM2 — the ones a code clear should ask directly.</summary>
        public List<int> DiagnosticModules()
        {
            var list = new List<int>();
            foreach (KeyValuePair<int, ModuleFaults> kv in _modules)
                if (kv.Value.HasDm1 || kv.Value.LastDm2 != DateTime.MinValue) list.Add(kv.Key);
            list.Sort();
            return list;
        }

        /// <summary>Has this source address sent anything at or after <paramref name="since"/>?</summary>
        public bool SeenSince(int sa, DateTime since)
        {
            SaRow r;
            return _sas.TryGetValue(sa, out r) && r.Last >= since;
        }

        /// <summary>Latest Acknowledgment from <paramref name="sa"/> for <paramref name="pgn"/> addressed to this tool.</summary>
        public AckReply AckFrom(int sa, int pgn, DateTime since)
        {
            for (int i = _acks.Count - 1; i >= 0; i--)
            {
                AckReply a = _acks[i];
                if (a.Time < since) break;
                if (a.Sa == sa && a.Pgn == pgn && a.ForTool) return a;
            }
            return null;
        }

        /// <summary>
        /// A module ACKed a clear but has not sent a fresh DM1/DM2 since. Some controllers only
        /// broadcast DM1 while they have a fault, so the old list would otherwise stay up forever.
        /// The next DM1/DM2 puts back anything that is really still there.
        /// </summary>
        public void MarkCleared(int sa, bool active, bool previous, DateTime now)
        {
            ModuleFaults m = Peek(sa);
            if (m == null) return;
            if (active)
            {
                m.Active.Clear();
                m.Red = m.Amber = m.Protect = m.Mil = false;
                m.Timeline.Update(m.Active, now, sa == _engineSa ? Rpm : double.NaN);
            }
            if (previous) m.Prev.Clear();
        }

        public void ClearLive()
        {
            Rpm = CoolantC = OilKpa = BatteryV = FuelLph = double.NaN;
            DefPct = DefTempC = double.NaN;
            DefSilent = true;
            Aft = new AftState();
            DefSa = -1;
            LastDef = DateTime.MinValue;
            _lastEngineDef = DateTime.MinValue;
            _acks.Clear();
            Vin = Sw = CompId = HoursText = "";
            _modules.Clear();
            LastFrame = DateTime.MinValue;
            if (_engineSaSetting >= 0) _engineSa = _engineSaSetting;
            else _engineSa = 0;
        }

        public void ClearBusStats()
        {
            _pgns.Clear();
            _sas.Clear();
        }

        public void ClearHistory()
        {
            _trend.Clear();
            foreach (KeyValuePair<int, ModuleFaults> kv in _modules) kv.Value.Timeline.Clear();
        }

        /// <summary>Feed one raw frame. Returns true when a DM1 or DM2 for any module was updated.</summary>
        public bool Feed(J1939Frame raw, DateTime now)
        {
            if (raw == null || raw.Data == null) return false;
            raw.Pgn = J1939Decode.NormalizePgn(raw.Pgn);
            Frames++;
            LastFrame = now;
            TrackBus(raw, now);
            J1939Frame f;
            if (!_bam.Feed(raw, out f)) return false;
            return Route(f, now);
        }

        bool Route(J1939Frame f, DateTime now)
        {
            if (f == null || f.Data == null) return false;

            // EEC1 defines the engine when we are auto-detecting: whoever broadcasts RPM is it.
            if (f.Pgn == 0xF004)
            {
                if (_engineSaSetting == AutoEngineSa && !IsKnownEngine(f.Sa)) _engineSa = f.Sa;
                if (f.Sa == _engineSa) Rpm = Nan(J1939Decode.Rpm(f.Data));
                return false;
            }

            bool engine = f.Sa == _engineSa;

            switch (f.Pgn)
            {
                case 0xFE56:
                    if (AcceptDef(f.Sa, engine, now))
                    {
                        DefSilent = f.Data.Length < 2 || f.Data[0] >= 0xFB;
                        DefPct = f.Data.Length > 0 && f.Data[0] < 0xFB ? f.Data[0] * 0.4 : double.NaN;
                        DefTempC = f.Data.Length > 1 && f.Data[1] < 0xFB ? f.Data[1] - 40 : double.NaN;
                        Aft = J1939Decode.ParseAftertreatment(f.Data);
                        DefSa = f.Sa;
                        LastDef = now;
                    }
                    return false;
                case J1939Clear.Ack:
                {
                    AckReply ack;
                    if (J1939Clear.TryParseAck(f, now, out ack))
                    {
                        if (_acks.Count >= MaxAcks) _acks.RemoveAt(0);
                        _acks.Add(ack);
                    }
                    return false;
                }
                case 0xFEEE:
                    if (engine && f.Data.Length > 0)
                        CoolantC = f.Data[0] < 0xFB ? f.Data[0] - 40 : double.NaN;
                    return false;
                case 0xFEEF:
                    if (engine && f.Data.Length > 3)
                        OilKpa = f.Data[3] < 0xFB ? f.Data[3] * 4 : double.NaN;
                    return false;
                case 0xFEF7:
                    if (engine && f.Data.Length >= 6)
                    {
                        int raw = J1939Decode.U16(f.Data, 4);
                        BatteryV = raw < 0xFB00 ? raw * 0.05 : double.NaN;
                    }
                    return false;
                case 0xFEF2:
                    if (engine && f.Data.Length >= 2)
                    {
                        int raw = J1939Decode.U16(f.Data, 0);
                        FuelLph = raw < 0xFB00 ? raw * 0.05 : double.NaN;
                    }
                    return false;
                case 0xFEEC:
                    if (engine) { string v = J1939Decode.Ascii(f.Data); if (v.Length > 0) Vin = v; }
                    return false;
                case 0xFEDA:
                    if (engine) { string v = J1939Decode.Ascii(f.Data); if (v.Length > 0) Sw = v; }
                    return false;
                case 0xFEEB:
                    if (engine) { string v = J1939Decode.Ascii(f.Data, true); if (v.Length > 0) CompId = v; }
                    return false;
                case 0xFEE5:
                    if (engine)
                    {
                        string v = J1939Decode.Hours(f.Data);
                        HoursText = v ?? "";
                    }
                    return false;
                case 0xFECA:
                {
                    // Every module's DM1 is kept, not just the engine's. On a portable compressor the
                    // controller at SA 48 has its own faults and they used to be invisible here.
                    ModuleFaults m = Module(f.Sa);
                    m.Active.Clear();
                    J1939Decode.ParseDm(f.Data, m.Active, out m.Red, out m.Amber, out m.Protect, out m.Mil);
                    m.LastDm1 = now;
                    m.Timeline.Update(m.Active, now, f.Sa == _engineSa ? Rpm : double.NaN);
                    return true;
                }
                case 0xFECB:
                {
                    ModuleFaults m = Module(f.Sa);
                    m.Prev.Clear();
                    bool r, a, p, mi;
                    J1939Decode.ParseDm(f.Data, m.Prev, out r, out a, out p, out mi);
                    m.LastDm2 = now;
                    return true;
                }
                default:
                    return false;
            }
        }

        bool IsKnownEngine(int sa)
        {
            return _engineSa == sa;
        }

        /// <summary>
        /// FE56 from the engine always wins. On machines where the DEF tank header or an
        /// aftertreatment module sends FE56 itself, the engine never does, and dropping those
        /// frames left the DEF/SCR lines at "not talking" with the tank on the bus.
        /// </summary>
        bool AcceptDef(int sa, bool engine, DateTime now)
        {
            if (engine)
            {
                _lastEngineDef = now;
                return true;
            }
            if (_lastEngineDef != DateTime.MinValue && (now - _lastEngineDef).TotalSeconds < DefFallbackSeconds)
                return false;
            if (DefSa >= 0 && DefSa != sa && DefSa != _engineSa && LastDef != DateTime.MinValue
                && (now - LastDef).TotalSeconds < DefFallbackSeconds)
                return false;
            return true;
        }

        static double Nan(double v)
        {
            return v < 0 ? double.NaN : v;
        }

        void TrackBus(J1939Frame f, DateTime now)
        {
            string k = f.Pgn.ToString("X4") + "/" + f.Sa;
            PgnRow pr;
            if (!_pgns.TryGetValue(k, out pr))
            {
                pr = new PgnRow { Pgn = f.Pgn, Sa = f.Sa };
                _pgns[k] = pr;
            }
            pr.Count++;
            pr.Last = now;
            pr.LastLen = f.Data == null ? 0 : f.Data.Length;

            SaRow sr;
            if (!_sas.TryGetValue(f.Sa, out sr))
            {
                sr = new SaRow { Sa = f.Sa };
                _sas[f.Sa] = sr;
            }
            sr.Count++;
            sr.Last = now;
            if (f.Pgn == 0xEE00 && f.Data != null)
            {
                sr.Claimed = true;
                sr.NameHex = BitConverter.ToString(f.Data);
            }
        }

        /// <summary>Offer the current readings to the trend log; it decides whether a second has passed.</summary>
        public bool SampleTrend(DateTime now)
        {
            ModuleFaults eng = Engine;
            return _trend.Offer(new TrendSample
            {
                Time = now,
                Rpm = Rpm,
                CoolantC = CoolantC,
                OilKpa = OilKpa,
                BatteryV = BatteryV,
                FuelLph = FuelLph,
                Red = eng.Red,
                Amber = eng.Amber,
                ActiveCodes = eng.Active.Count
            });
        }

        public List<PgnRow> PgnRows()
        {
            var list = new List<PgnRow>(_pgns.Values);
            // Sorted by PGN: count-ranked rows reorder under the cursor on a busy bus and become
            // unreadable. The count is already a column.
            list.Sort(delegate(PgnRow a, PgnRow b)
            {
                int c = a.Pgn.CompareTo(b.Pgn);
                return c != 0 ? c : a.Sa.CompareTo(b.Sa);
            });
            return list;
        }

        public List<SaRow> SaRows()
        {
            var list = new List<SaRow>(_sas.Values);
            list.Sort(delegate(SaRow a, SaRow b) { return a.Sa.CompareTo(b.Sa); });
            return list;
        }

        public string DefText()
        {
            if (DefSilent) return "n/a (not talking)";
            string pct = TrendSample.Has(DefPct) ? DefPct.ToString("0.0") + "%" : "n/a";
            string t = TrendSample.Has(DefTempC) ? DefTempC.ToString("0") + " C" : "n/a";
            return pct + "  temp " + t;
        }

        public string AftText()
        {
            string text = (Aft ?? new AftState()).Text();
            if (DefSa >= 0 && DefSa != _engineSa)
                text += "\r\n(FE56 from " + Names.SaLabel(DefSa) + ", not the engine)";
            return text;
        }

        public static string Fmt(double v, string unit, string fmt)
        {
            return TrendSample.Has(v) ? v.ToString(fmt) + unit : "—";
        }

        public string OilText()
        {
            if (!TrendSample.Has(OilKpa)) return "—";
            return OilKpa.ToString("0") + " kPa (" + (OilKpa * 0.145).ToString("0.0") + " psi)";
        }

        public bool HasSpn(int spn)
        {
            foreach (Dtc d in Engine.Active) if (d.Spn == spn) return true;
            return false;
        }

        public bool HasTankFmi9()
        {
            foreach (Dtc d in Engine.Active)
                if ((d.Spn == 1761 || d.Spn == 3031 || d.Spn == 3364) && d.Fmi == 9) return true;
            return false;
        }
    }
}
