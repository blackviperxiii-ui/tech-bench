using System;
using System.Collections.Generic;
using System.Text;

namespace J1939Reader
{
    internal enum ClearKind
    {
        /// <summary>DM11 (clear active) then DM3 (clear previously active).</summary>
        All,
        /// <summary>DM3 (clear previously active) only.</summary>
        Previous
    }

    /// <summary>How one module answered a directed DM11 or DM3 request.</summary>
    internal enum ClearAnswer
    {
        NotAsked,
        SendFailed,
        NotOnBus,
        NoAnswer,
        Ack,
        Nack,
        Denied,
        Busy
    }

    /// <summary>
    /// The bus as a code clear sees it. BusWorker implements this over the live adapter and keeps
    /// reading frames while the clear waits, so the ACKs and the fresh DM1/DM2 actually land.
    /// SelfTest implements it with scripted ECMs.
    /// </summary>
    internal interface IClearLink
    {
        DateTime Now { get; }

        /// <summary>False when the adapter refused the frame.</summary>
        bool Send(J1939Tx tx);

        /// <summary>
        /// Keep reading the bus for up to <paramref name="ms"/>, returning early once
        /// <paramref name="done"/> (may be null) is true. False if the adapter dropped or the tech
        /// pressed Disconnect.
        /// </summary>
        bool Pump(int ms, Func<bool> done);

        void Progress(string what);

        string LastError { get; }
    }

    /// <summary>One module the clear asked directly, before and after.</summary>
    internal sealed class ClearTarget
    {
        public int Sa;
        public bool OnBus;
        public ClearAnswer Dm11 = ClearAnswer.NotAsked;
        public ClearAnswer Dm3 = ClearAnswer.NotAsked;
        public bool HadDm1;
        public bool HadDm2;
        public readonly List<Dtc> ActiveBefore = new List<Dtc>();
        public readonly List<Dtc> PrevBefore = new List<Dtc>();
        /// <summary>Null until a DM1 arrives after the clear (or the list was emptied on an ACK).</summary>
        public List<Dtc> ActiveAfter;
        /// <summary>Null until a DM2 arrives after the clear (or the list was emptied on an ACK).</summary>
        public List<Dtc> PrevAfter;
        public bool EmptiedActive;
        public bool EmptiedPrev;
        public bool FreshDm1;
        public bool Red, Amber, Protect, Mil;
    }

    /// <summary>
    /// One J1939-73 code clear, start to finish:
    ///   1. note what every module says now (DM1/DM2, DEF/SCR),
    ///   2. Request DM11 (reset all only) to each module directly, then to everyone, and wait for
    ///      each module's ACK / NACK; one retry for a module on the bus that stayed quiet,
    ///   3. the same for DM3,
    ///   4. give the modules a second to re-check, re-request DM1/DM2/FE56, and wait for them,
    ///   5. report per module what cleared, what came back, and what the DEF/SCR side shows.
    /// The old reset fired three blind rounds, never read an answer, and reported before the
    /// fresh DM1 had arrived, so a good clear and a refused one looked the same.
    /// </summary>
    internal sealed class CodeClearRun
    {
        /// <summary>J1939-21 T3: how long a requester waits for the answer.</summary>
        public const int AckWaitMs = 1250;
        public const int RetryWaitMs = 750;
        /// <summary>Time for the modules to re-check their faults before DM1/DM2 are read back.</summary>
        public const int SettleMs = 1000;
        public const int RereadMs = 3000;
        /// <summary>An address counts as on the bus if it sent anything this recently.</summary>
        public const int OnBusSeconds = 10;

        readonly ClearKind _kind;
        readonly BusMonitor _mon;
        readonly object _gate;
        readonly IClearLink _link;
        readonly List<ClearTarget> _targets = new List<ClearTarget>();

        int _engineSa;
        DateTime _started;
        DateTime _reread = DateTime.MaxValue;
        int _sent;
        int _rejected;
        string _rejectWhy = "";
        bool _aborted;
        string _abortWhy = "";
        bool _hadDef;
        bool _defFresh;
        int _defSa = -1;
        AftState _aftBefore = new AftState();
        AftState _aftAfter = new AftState();

        public CodeClearRun(ClearKind kind, BusMonitor mon, object gate, IClearLink link)
        {
            if (mon == null) throw new ArgumentNullException("mon");
            if (link == null) throw new ArgumentNullException("link");
            _kind = kind;
            _mon = mon;
            _gate = gate ?? new object();
            _link = link;
        }

        public ClearKind Kind { get { return _kind; } }
        public List<ClearTarget> Targets { get { return _targets; } }
        public bool Aborted { get { return _aborted; } }
        public int Sent { get { return _sent; } }
        public int Rejected { get { return _rejected; } }
        public string Headline { get; private set; }

        public ClearTarget Target(int sa)
        {
            foreach (ClearTarget t in _targets) if (t.Sa == sa) return t;
            return null;
        }

        /// <summary>Run the whole clear on the calling (bus) thread and return the report text.</summary>
        public string Execute()
        {
            _started = _link.Now;
            Before();
            if (_kind == ClearKind.All) Phase(J1939Clear.Dm11);
            if (!Stopped()) Phase(J1939Clear.Dm3);
            if (!Stopped()) ReadBack();
            After();
            return Report();
        }

        bool Stopped()
        {
            return _aborted || (_sent == 0 && _rejected > 0);
        }

        // ---------- steps ----------

        void Before()
        {
            lock (_gate)
            {
                _engineSa = _mon.EngineSa;
                byte[] dests = J1939Clear.Destinations(_engineSa, _mon.DiagnosticModules());
                DateTime recent = _started.AddSeconds(-OnBusSeconds);
                foreach (byte sa in J1939Clear.Directed(dests))
                {
                    var t = new ClearTarget { Sa = sa, OnBus = _mon.SeenSince(sa, recent) };
                    ModuleFaults m = _mon.Peek(sa);
                    if (m != null)
                    {
                        t.HadDm1 = m.HasDm1;
                        t.HadDm2 = m.LastDm2 != DateTime.MinValue;
                        t.ActiveBefore.AddRange(m.Active);
                        t.PrevBefore.AddRange(m.Prev);
                    }
                    _targets.Add(t);
                }
                _hadDef = _mon.LastDef != DateTime.MinValue;
                _aftBefore = _mon.Aft ?? new AftState();
            }
        }

        void Phase(int pgn)
        {
            string name = PhaseName(pgn);
            DateTime since = _link.Now;
            if (SendRequests(pgn, _targets, true) > 0)
            {
                _link.Progress("clearing codes — " + name + " sent, waiting for ACK");
                Wait(pgn, since, AckWaitMs);
            }
            if (Stopped()) return;

            // One retry: the adapter refused the frame, or the module is on the bus but stayed
            // quiet or said busy.
            var again = new List<ClearTarget>();
            foreach (ClearTarget t in _targets)
            {
                ClearAnswer a = Answer(t, pgn);
                if (a == ClearAnswer.SendFailed
                    || (t.OnBus && (a == ClearAnswer.NoAnswer || a == ClearAnswer.Busy)))
                    again.Add(t);
            }
            if (again.Count > 0)
            {
                DateTime retry = _link.Now;
                if (SendRequests(pgn, again, false) > 0)
                {
                    _link.Progress("clearing codes — " + name + " retry, waiting for ACK");
                    Wait(pgn, retry, RetryWaitMs);
                }
            }

            foreach (ClearTarget t in _targets)
                if (!t.OnBus && Answer(t, pgn) == ClearAnswer.NoAnswer) SetAnswer(t, pgn, ClearAnswer.NotOnBus);
        }

        int SendRequests(int pgn, List<ClearTarget> list, bool global)
        {
            int ok = 0;
            foreach (ClearTarget t in list)
            {
                bool sent = Send(J1939Clear.RequestTx(pgn, (byte)t.Sa));
                if (sent) ok++;
                // A retry keeps an earlier "busy" / "no answer"; only a first send sets the state.
                ClearAnswer was = Answer(t, pgn);
                if (was == ClearAnswer.NotAsked || was == ClearAnswer.SendFailed)
                    SetAnswer(t, pgn, sent ? ClearAnswer.NoAnswer : ClearAnswer.SendFailed);
            }
            // Global last: catches a module this tool has not heard from. It is never answered.
            if (global && Send(J1939Clear.RequestTx(pgn, J1939Clear.Broadcast))) ok++;
            return ok;
        }

        bool Send(J1939Tx tx)
        {
            bool ok;
            try { ok = _link.Send(tx); }
            catch (Exception ex) { ok = false; _rejectWhy = ex.Message; }
            if (ok) _sent++;
            else
            {
                _rejected++;
                string why = _link.LastError;
                if (!string.IsNullOrEmpty(why)) _rejectWhy = why;
            }
            return ok;
        }

        void Wait(int pgn, DateTime since, int ms)
        {
            bool live = _link.Pump(ms, delegate { return AllAnswered(pgn, since); });
            TakeAnswers(pgn, since);
            if (!live) Abort();
        }

        bool AllAnswered(int pgn, DateTime since)
        {
            lock (_gate)
            {
                foreach (ClearTarget t in _targets)
                {
                    if (!t.OnBus) continue;
                    ClearAnswer a = Answer(t, pgn);
                    if (a != ClearAnswer.NoAnswer && a != ClearAnswer.Busy) continue;
                    if (_mon.AckFrom(t.Sa, pgn, since) == null) return false;
                }
            }
            return true;
        }

        void TakeAnswers(int pgn, DateTime since)
        {
            lock (_gate)
            {
                foreach (ClearTarget t in _targets)
                {
                    ClearAnswer a = Answer(t, pgn);
                    if (a == ClearAnswer.NotAsked || a == ClearAnswer.SendFailed) continue;
                    AckReply r = _mon.AckFrom(t.Sa, pgn, since);
                    if (r != null) SetAnswer(t, pgn, FromControl(r.Control));
                }
            }
        }

        void ReadBack()
        {
            _link.Progress("clearing codes — letting the modules re-check");
            if (!_link.Pump(SettleMs, null)) { Abort(); return; }

            _reread = _link.Now;
            var dests = new List<byte>();
            foreach (ClearTarget t in _targets) dests.Add((byte)t.Sa);
            dests.Add(J1939Clear.Broadcast);
            foreach (J1939Tx tx in J1939Clear.RefreshDmFrames(dests)) Send(tx);

            var defDests = new List<byte>();
            defDests.Add(J1939Clear.UnicastDest(_engineSa, J1939Clear.EngineSa));
            int defSa;
            lock (_gate) defSa = _mon.DefSa;
            if (defSa >= 0 && defSa <= 253 && !defDests.Contains((byte)defSa)) defDests.Add((byte)defSa);
            defDests.Add(J1939Clear.Broadcast);
            foreach (J1939Tx tx in J1939Clear.AftertreatmentReadFrames(defDests)) Send(tx);

            _link.Progress("clearing codes — reading DM1 / DM2 / DEF back");
            if (!_link.Pump(RereadMs, ReadBackDone)) Abort();
        }

        bool ReadBackDone()
        {
            lock (_gate)
            {
                foreach (ClearTarget t in _targets)
                {
                    if (!t.OnBus) continue;
                    ModuleFaults m = _mon.Peek(t.Sa);
                    if (t.HadDm1 && (m == null || m.LastDm1 < _reread)) return false;
                    bool wantDm2 = t.HadDm2 || t.Dm3 == ClearAnswer.Ack;
                    if (wantDm2 && (m == null || m.LastDm2 < _reread)
                        && _mon.AckFrom(t.Sa, J1939Clear.Dm2, _reread) == null)
                        return false;
                }
                if (_hadDef && _mon.LastDef < _reread) return false;
            }
            return true;
        }

        void After()
        {
            DateTime now = _link.Now;
            bool readBack = _reread != DateTime.MaxValue;
            lock (_gate)
            {
                foreach (ClearTarget t in _targets)
                {
                    ModuleFaults m = _mon.Peek(t.Sa);
                    if (m == null) continue;
                    if (readBack && m.LastDm1 >= _reread)
                    {
                        t.FreshDm1 = true;
                        t.ActiveAfter = new List<Dtc>(m.Active);
                        t.Red = m.Red; t.Amber = m.Amber; t.Protect = m.Protect; t.Mil = m.Mil;
                    }
                    else if (readBack && !_aborted && t.Dm11 == ClearAnswer.Ack && m.Active.Count > 0)
                    {
                        _mon.MarkCleared(t.Sa, true, false, now);
                        t.EmptiedActive = true;
                        t.ActiveAfter = new List<Dtc>();
                    }
                    if (readBack && m.LastDm2 >= _reread)
                        t.PrevAfter = new List<Dtc>(m.Prev);
                    else if (readBack && !_aborted && t.Dm3 == ClearAnswer.Ack && m.Prev.Count > 0)
                    {
                        _mon.MarkCleared(t.Sa, false, true, now);
                        t.EmptiedPrev = true;
                        t.PrevAfter = new List<Dtc>();
                    }
                }
                _defSa = _mon.DefSa;
                _defFresh = readBack && _mon.LastDef >= _reread;
                _aftAfter = _mon.Aft ?? new AftState();
            }
        }

        void Abort()
        {
            if (_aborted) return;
            _aborted = true;
            _abortWhy = "the adapter dropped or Disconnect was pressed";
        }

        // ---------- answers ----------

        static ClearAnswer Answer(ClearTarget t, int pgn)
        {
            return pgn == J1939Clear.Dm11 ? t.Dm11 : t.Dm3;
        }

        static void SetAnswer(ClearTarget t, int pgn, ClearAnswer a)
        {
            if (pgn == J1939Clear.Dm11) t.Dm11 = a;
            else t.Dm3 = a;
        }

        static ClearAnswer FromControl(int control)
        {
            switch (control)
            {
                case J1939Clear.AckPositive: return ClearAnswer.Ack;
                case J1939Clear.AckNegative: return ClearAnswer.Nack;
                case J1939Clear.AckDenied: return ClearAnswer.Denied;
                case J1939Clear.AckBusy: return ClearAnswer.Busy;
                default: return ClearAnswer.Nack;
            }
        }

        static bool Refused(ClearAnswer a)
        {
            return a == ClearAnswer.Nack || a == ClearAnswer.Denied;
        }

        public static string AnswerText(ClearAnswer a)
        {
            switch (a)
            {
                case ClearAnswer.Ack: return "ACK (cleared)";
                case ClearAnswer.Nack: return "NACK (refused)";
                case ClearAnswer.Denied: return "access denied";
                case ClearAnswer.Busy: return "busy (cannot respond)";
                case ClearAnswer.NoAnswer: return "no answer";
                case ClearAnswer.NotOnBus: return "not on the bus";
                case ClearAnswer.SendFailed: return "adapter rejected the request";
                default: return "not asked";
            }
        }

        static string PhaseName(int pgn)
        {
            return pgn == J1939Clear.Dm11 ? "DM11 clear active" : "DM3 clear previous";
        }

        // ---------- report ----------

        bool Silent(ClearTarget t)
        {
            bool asked11 = _kind == ClearKind.All;
            bool quiet11 = !asked11 || t.Dm11 == ClearAnswer.NotOnBus || t.Dm11 == ClearAnswer.NoAnswer;
            bool quiet3 = t.Dm3 == ClearAnswer.NotOnBus || t.Dm3 == ClearAnswer.NoAnswer || t.Dm3 == ClearAnswer.NotAsked;
            return !t.OnBus && quiet11 && quiet3 && t.ActiveBefore.Count == 0 && t.PrevBefore.Count == 0
                && !t.HadDm1 && !t.HadDm2 && t.ActiveAfter == null && t.PrevAfter == null;
        }

        /// <summary>The answer that decides this kind of clear: DM11 for reset all, DM3 for clear previous.</summary>
        ClearAnswer MainAnswer(ClearTarget t)
        {
            return _kind == ClearKind.All ? t.Dm11 : t.Dm3;
        }

        /// <summary>The list this kind of clear empties, as read back after it (null = not re-read).</summary>
        List<Dtc> MainAfter(ClearTarget t)
        {
            if (_kind == ClearKind.All) return t.FreshDm1 ? t.ActiveAfter : null;
            return t.EmptiedPrev ? null : t.PrevAfter;
        }

        List<Dtc> MainBefore(ClearTarget t)
        {
            return _kind == ClearKind.All ? t.ActiveBefore : t.PrevBefore;
        }

        /// <summary>Codes a module ACKed away and then reported again: still true, not a failed clear.</summary>
        int CameBack()
        {
            int n = 0;
            foreach (ClearTarget t in _targets)
            {
                if (MainAnswer(t) != ClearAnswer.Ack) continue;
                List<Dtc> after = MainAfter(t);
                if (after != null) n += after.Count;
            }
            return n;
        }

        /// <summary>On the bus, never answered, and still showing (or not re-sending) its codes.</summary>
        List<ClearTarget> Stuck()
        {
            var list = new List<ClearTarget>();
            foreach (ClearTarget t in _targets)
            {
                ClearAnswer a = MainAnswer(t);
                if (!t.OnBus || (a != ClearAnswer.NoAnswer && a != ClearAnswer.Busy)) continue;
                List<Dtc> after = MainAfter(t);
                if ((after != null && after.Count > 0) || (after == null && MainBefore(t).Count > 0)) list.Add(t);
            }
            return list;
        }

        static string SaList(List<ClearTarget> targets)
        {
            var names = new List<string>();
            foreach (ClearTarget t in targets) names.Add("SA " + t.Sa);
            return string.Join(", ", names.ToArray());
        }

        List<Dtc> AftertreatmentAfter()
        {
            var list = new List<Dtc>();
            foreach (ClearTarget t in _targets)
            {
                if (!t.FreshDm1 || t.ActiveAfter == null) continue;
                foreach (Dtc d in t.ActiveAfter)
                    if (J1939Clear.IsAftertreatmentSpn(d.Spn)) list.Add(d);
            }
            return list;
        }

        string BuildHeadline()
        {
            if (_aborted)
                return "RESULT: INTERRUPTED — " + _abortWhy + ". Connect again and re-run the clear.";
            if (_sent == 0 && _rejected > 0)
                return "RESULT: FAILED — the adapter rejected every request"
                    + (string.IsNullOrEmpty(_rejectWhy) ? "" : " (" + _rejectWhy + ")") + ". Nothing was sent.";

            var refused = new List<string>();
            bool acked = false;
            foreach (ClearTarget t in _targets)
            {
                if (Refused(t.Dm11) || Refused(t.Dm3)) refused.Add("SA " + t.Sa);
                if (t.Dm11 == ClearAnswer.Ack || t.Dm3 == ClearAnswer.Ack) acked = true;
            }
            int back = CameBack();
            List<ClearTarget> stuck = Stuck();
            string stuckNote = stuck.Count == 0 ? ""
                : "; " + SaList(stuck) + " did not answer and still show" + (stuck.Count == 1 ? "s" : "") + " codes";
            string what = _kind == ClearKind.All ? "active" : "previously active";
            if (refused.Count > 0)
                return "RESULT: REFUSED by " + string.Join(", ", refused.ToArray())
                    + (acked ? " (others cleared)" : "") + ". See below.";
            if (acked && back > 0)
                return "RESULT: cleared — " + back + " " + what + " code" + (back == 1 ? "" : "s")
                    + " came straight back (still true right now)" + stuckNote + ".";
            if (acked && stuck.Count > 0)
                return "RESULT: partly cleared" + stuckNote + ".";
            if (acked)
                return _kind == ClearKind.All
                    ? "RESULT: cleared — no active codes came back."
                    : "RESULT: previously active codes cleared.";

            int before = 0, after = 0;
            bool fresh = false;
            foreach (ClearTarget t in _targets)
            {
                if (_kind == ClearKind.All)
                {
                    before += t.ActiveBefore.Count + t.PrevBefore.Count;
                    if (t.FreshDm1) { fresh = true; after += t.ActiveAfter.Count; }
                    if (t.PrevAfter != null) after += t.PrevAfter.Count;
                }
                else
                {
                    before += t.PrevBefore.Count;
                    if (t.PrevAfter != null) { fresh = true; after += t.PrevAfter.Count; }
                }
            }
            if (fresh && before > 0 && after == 0)
                return "RESULT: codes gone — no module sent an ACK, but the fresh DM1/DM2 are empty.";
            return "RESULT: NOT CONFIRMED — no module acknowledged the clear"
                + (fresh && after >= before && before > 0 ? " and the codes did not change." : ".");
        }

        public string Report()
        {
            Headline = BuildHeadline();
            var sb = new StringBuilder();
            sb.AppendLine(_kind == ClearKind.All
                ? "CODE CLEAR — reset all codes (J1939 DM11 clear active + DM3 clear previous)"
                : "CODE CLEAR — clear previously active codes (J1939 DM3)");
            sb.AppendLine(Headline);
            sb.AppendLine();

            var asked = new List<string>();
            foreach (ClearTarget t in _targets) asked.Add("SA " + t.Sa);
            sb.AppendLine("Asked directly: " + string.Join(", ", asked.ToArray()) + ", then everyone (SA 255).");
            if (_rejected > 0 && _sent > 0)
                sb.AppendLine("Adapter rejected " + _rejected + " of " + (_sent + _rejected) + " frames"
                    + (string.IsNullOrEmpty(_rejectWhy) ? "." : ": " + _rejectWhy));
            sb.AppendLine();

            foreach (ClearTarget t in _targets) AppendTarget(sb, t);
            AppendLamps(sb);
            AppendDef(sb);
            AppendAdvice(sb);
            return sb.ToString().TrimEnd() + "\r\n";
        }

        void AppendTarget(StringBuilder sb, ClearTarget t)
        {
            string label = Names.SaLabel(t.Sa);
            if (Silent(t))
            {
                sb.AppendLine(label + " — not on the bus (nothing heard from this address); request sent anyway.");
                return;
            }
            sb.AppendLine(label);
            if (_kind == ClearKind.All)
                sb.AppendLine("  DM11 clear active: " + AnswerText(t.Dm11) + "    DM3 clear previous: " + AnswerText(t.Dm3));
            else
                sb.AppendLine("  DM3 clear previous: " + AnswerText(t.Dm3));

            if (_kind == ClearKind.All)
            {
                if (t.EmptiedActive)
                    sb.AppendLine("  Active: " + t.ActiveBefore.Count + " before → 0 (ACKed, no DM1 since; the list was emptied — a new DM1 puts back anything still active)");
                else if (t.ActiveAfter != null)
                {
                    if (t.ActiveBefore.Count > 0 || t.ActiveAfter.Count > 0)
                        sb.AppendLine("  Active: " + t.ActiveBefore.Count + " before → " + t.ActiveAfter.Count + " now");
                    AppendCodes(sb, "    still active: ", t.ActiveAfter);
                }
                else if (t.ActiveBefore.Count > 0)
                    sb.AppendLine("  Active: " + t.ActiveBefore.Count + " before → no DM1 since the clear (the list still shows what it last sent)");
            }

            if (t.EmptiedPrev)
                sb.AppendLine("  Previous: " + t.PrevBefore.Count + " before → 0 (ACKed, no DM2 since; the list was emptied)");
            else if (t.PrevAfter != null)
            {
                if (t.PrevBefore.Count > 0 || t.PrevAfter.Count > 0)
                    sb.AppendLine("  Previous: " + t.PrevBefore.Count + " before → " + t.PrevAfter.Count + " now");
                AppendCodes(sb, "    still previously active: ", t.PrevAfter);
            }
            else if (t.PrevBefore.Count > 0)
                sb.AppendLine("  Previous: " + t.PrevBefore.Count + " before → no DM2 since the clear");
        }

        static void AppendCodes(StringBuilder sb, string prefix, List<Dtc> codes)
        {
            foreach (Dtc d in codes)
            {
                string name = string.IsNullOrEmpty(d.Name) ? "" : "  " + d.Name;
                string tag = J1939Clear.IsAftertreatmentSpn(d.Spn) ? "  [DEF/SCR]" : "";
                sb.AppendLine(prefix + "SPN " + d.Spn + " FMI " + d.Fmi + name + tag);
            }
        }

        void AppendLamps(StringBuilder sb)
        {
            if (_kind != ClearKind.All) return;
            ClearTarget eng = Target(J1939Clear.UnicastDest(_engineSa, J1939Clear.EngineSa));
            sb.AppendLine();
            if (eng != null && eng.FreshDm1)
                sb.AppendLine("Engine lamps now: Red Stop " + (eng.Red ? "ON" : "off")
                    + "   Amber " + (eng.Amber ? "ON" : "off")
                    + (eng.Protect ? "   Protect ON" : "") + (eng.Mil ? "   MIL ON" : "")
                    + "  (fresh DM1, SA " + eng.Sa + "). Lamps are DM1 bits; they follow the active codes.");
            else
                sb.AppendLine("Engine lamps: no DM1 from the engine since the clear.");
        }

        void AppendDef(StringBuilder sb)
        {
            sb.AppendLine();
            if (!_hadDef && !_defFresh)
            {
                sb.AppendLine("DEF / SCR: no DEF tank message (PGN FE56) on this hookup — the tank header or the module that reports it is not talking.");
                return;
            }
            string from = _defSa >= 0 ? " from " + Names.SaLabel(_defSa) : "";
            sb.AppendLine(_defFresh
                ? "DEF / SCR (PGN FE56" + from + ", read after the clear):"
                : "DEF / SCR (PGN FE56" + from + ", NOT re-sent since the clear — last values):");
            foreach (string line in _aftAfter.Text().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
                sb.AppendLine("  " + line);
            if (_aftBefore.SeverityRaw >= 0 && _aftBefore.SeverityRaw != _aftAfter.SeverityRaw)
                sb.AppendLine("  SCR inducement was: " + _aftBefore.Severity);
        }

        void AppendAdvice(StringBuilder sb)
        {
            var tips = new List<string>();
            if (_aborted)
                tips.Add("The clear was interrupted before it could be confirmed. Connect again and re-run it.");
            if (_sent == 0 && _rejected > 0)
                tips.Add("The adapter refused every frame. Disconnect, close USB-Link Explorer / Guidanz, Connect again, then retry. If it keeps failing, check the Log for the tool address claim.");

            var refused = new List<string>();
            var quiet = new List<string>();
            bool quietCompressor = false;
            foreach (ClearTarget t in _targets)
            {
                if (Refused(t.Dm11) || Refused(t.Dm3)) refused.Add(Names.SaLabel(t.Sa));
                else if (!_aborted && t.OnBus && (t.Dm11 == ClearAnswer.NoAnswer || t.Dm3 == ClearAnswer.NoAnswer))
                {
                    quiet.Add(Names.SaLabel(t.Sa));
                    if (t.Sa == J1939Clear.CompressorSa) quietCompressor = true;
                }
            }
            if (refused.Count > 0)
                tips.Add(string.Join(", ", refused.ToArray()) + " refused the clear. Many ECMs only clear with key ON, engine OFF — shut the engine down, leave the key ON, and clear again.");
            if (quiet.Count > 0)
                tips.Add(string.Join(", ", quiet.ToArray()) + " did not answer, even after a retry. It may not take J1939 clears from a service tool"
                    + (quietCompressor ? "; some compressor controllers only reset faults from their own keypad or display." : "."));
            if (CameBack() > 0)
                tips.Add("Codes that came straight back are still true right now. The clear worked; the fault did not go away. Fix it, then clear again.");

            bool tankDead = false;
            List<Dtc> aft = AftertreatmentAfter();
            foreach (Dtc d in aft)
                if ((d.Spn == 1761 || d.Spn == 3031 || d.Spn == 3364) && d.Fmi == 9) tankDead = true;
            int sev = _aftAfter.SeverityRaw;
            if (tankDead)
                tips.Add("DEF tank header is not talking (FMI 9 on 1761 / 3031 / 3364). Nothing DEF-related will stay cleared until it talks — check its power, ground, and CAN at the tank.");
            if (aft.Count > 0 || (sev >= 1 && sev <= 5))
                tips.Add("DEF/SCR: a code clear does not end SCR inducement (SPN 5246) by itself. The ECM drops it once it sees the repair working: DEF level, temp, and quality reading real values (above), then key OFF until the ECM powers down, key ON, and run the engine while you watch SCR inducement go to \"not active\". Guidanz aftertreatment reset may still be required. Tech Bench never disables DEF/SCR.");

            if (tips.Count == 0) return;
            sb.AppendLine();
            sb.AppendLine("What to do:");
            foreach (string tip in tips) sb.AppendLine("- " + tip);
        }
    }
}
