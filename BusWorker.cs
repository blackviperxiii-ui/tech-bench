using System;
using System.Collections.Generic;
using System.Threading;

namespace J1939Reader
{
    internal enum BusCmdKind
    {
        RequestCodes,
        ClearPrevious,
        ResetAll,
        PingIdentity,
        SetTsc1,
        Dm13,
        ClearBusStats,
        ClearHistory
    }

    internal sealed class BusCommand
    {
        public BusCmdKind Kind;
        public int Arg;
        public bool Flag;

        public BusCommand(BusCmdKind kind) { Kind = kind; }
        public BusCommand(BusCmdKind kind, int arg) { Kind = kind; Arg = arg; }
        public BusCommand(BusCmdKind kind, bool flag) { Kind = kind; Flag = flag; }
    }

    /// <summary>Immutable view of the bus for the UI thread.</summary>
    internal sealed class BusSnapshot
    {
        public bool Connected;
        public bool Busy;                 // a blocking operation (reset) is running
        public string BusyWhat = "";
        public string AdapterName = "";
        public short DeviceId;
        public string Protocol = "";
        public string LastError = "";
        public int Fps;
        public double QuietSeconds;
        public bool Stale;
        public int TscRpm;
        public int EngineSa;
        public int ViewSa;
        public double Rpm = double.NaN;
        public double CoolantC = double.NaN;
        public double OilKpa = double.NaN;
        public double BatteryV = double.NaN;
        public double FuelLph = double.NaN;
        public string DefText = "—";
        public string OilText = "—";
        public string Vin = "";
        public string Sw = "";
        public string CompId = "";
        public string Hours = "";
        public bool Red, Amber, Protect, Mil;
        public bool HasSpn5246, HasTankFmi9, Has1569;
        public string LastResetReport = "";
        public List<Dtc> Active = new List<Dtc>();
        public List<Dtc> Prev = new List<Dtc>();
        public List<int> FaultModules = new List<int>();
        public List<PgnRow> Pgns = new List<PgnRow>();
        public List<SaRow> Sas = new List<SaRow>();
    }

    /// <summary>
    /// Owns the adapter on a background thread. Everything that blocks — SendMessage, the 850 ms
    /// code reset, the ISO15765 probe, reconnect attempts — used to run on the UI thread from a 40 ms
    /// timer, which froze the window during a reset and stuttered while connected.
    /// </summary>
    internal sealed class BusWorker : IDisposable
    {
        const int CycleMs = 5;
        const int TscHeartbeatMs = 40;
        const int AutoRefreshMs = 2000;
        const int ReconnectEveryMs = 5000;
        const double StaleAfterSeconds = 3.0;
        const int MaxLogQueued = 500;

        readonly Rp1210 _rp = new Rp1210();
        readonly BusMonitor _mon = new BusMonitor();
        readonly object _gate = new object();
        readonly Queue<BusCommand> _cmds = new Queue<BusCommand>();
        readonly Queue<string> _log = new Queue<string>();

        Thread _thread;
        volatile bool _stop;
        volatile bool _wantConnected;
        volatile bool _autoReconnect = true;
        volatile bool _autoRefresh = true;
        volatile int _tscRpm;
        volatile int _viewSa = -1;
        volatile bool _busy;
        volatile string _busyWhat = "";

        Rp1210Api _api;
        string _lastResetReport = "";
        DateTime _lastReq = DateTime.MinValue;
        DateTime _lastTsc = DateTime.MinValue;
        DateTime _lastReconnect = DateTime.MinValue;
        DateTime _lastGood = DateTime.MinValue;
        DateTime _fpsWindow = DateTime.MinValue;
        int _framesThisWindow;
        int _fps;

        public bool AutoReconnect { get { return _autoReconnect; } set { _autoReconnect = value; } }
        public bool AutoRefresh { get { return _autoRefresh; } set { _autoRefresh = value; } }
        public bool WantConnected { get { return _wantConnected; } }

        /// <summary>Which module's DM1/DM2 the UI is showing; -1 follows the engine.</summary>
        public int ViewSa { get { return _viewSa; } set { _viewSa = value; } }

        public int EngineSaSetting
        {
            get { lock (_gate) return _mon.EngineSaSetting; }
            set { lock (_gate) _mon.EngineSaSetting = value; }
        }

        public void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "J1939 bus";
            _thread.Start();
        }

        public void Dispose()
        {
            _stop = true;
            Thread t = _thread;
            _thread = null;
            if (t != null)
            {
                try { t.Join(1500); }
                catch { }
            }
            try
            {
                if (_rp.IsConnected && _tscRpm > 0) _rp.SendTsc1(0);
            }
            catch { }
            _rp.Dispose();
        }

        public void Enqueue(BusCommand cmd)
        {
            if (cmd == null) return;
            lock (_gate) _cmds.Enqueue(cmd);
        }

        public List<string> DrainLog()
        {
            lock (_gate)
            {
                if (_log.Count == 0) return null;
                var list = new List<string>(_log.Count);
                while (_log.Count > 0) list.Add(_log.Dequeue());
                return list;
            }
        }

        void Log(string s)
        {
            lock (_gate)
            {
                if (_log.Count >= MaxLogQueued) _log.Dequeue();
                _log.Enqueue(s);
            }
        }

        /// <summary>Adapters this PC has installed. Safe to call before Start.</summary>
        public static List<Rp1210Api> Adapters()
        {
            return Rp1210.Adapters();
        }

        public void RequestConnect(Rp1210Api api)
        {
            lock (_gate) _api = api;
            _wantConnected = true;
            _lastReconnect = DateTime.MinValue;
        }

        public void RequestDisconnect()
        {
            _wantConnected = false;
        }

        public void SetTsc1(int rpm)
        {
            _tscRpm = rpm < 0 ? 0 : rpm;
            Enqueue(new BusCommand(BusCmdKind.SetTsc1, _tscRpm));
        }

        void Loop()
        {
            while (!_stop)
            {
                try { Cycle(); }
                catch (Exception ex) { Log("bus worker error: " + ex.Message); }
                Thread.Sleep(CycleMs);
            }
            try
            {
                if (_rp.IsConnected)
                {
                    if (_tscRpm > 0) _rp.SendTsc1(0);
                    _rp.Disconnect();
                }
            }
            catch { }
        }

        void Cycle()
        {
            DateTime now = DateTime.UtcNow;

            if (!_wantConnected)
            {
                if (_rp.IsConnected)
                {
                    if (_tscRpm > 0) { _rp.SendTsc1(0); _tscRpm = 0; }
                    _rp.Disconnect();
                    lock (_gate) _mon.ClearLive();
                    Log("Disconnected");
                }
                DrainCommandsWhileOffline();
                return;
            }

            if (!_rp.IsConnected)
            {
                TryConnect(now);
                return;
            }

            DrainCommands();

            if (_tscRpm > 0 && (now - _lastTsc).TotalMilliseconds >= TscHeartbeatMs)
            {
                _lastTsc = now;
                _rp.SendTsc1(_tscRpm);
            }

            if (_autoRefresh && (now - _lastReq).TotalMilliseconds >= AutoRefreshMs)
                RequestCodes(false);

            int read = 0;
            J1939Frame f;
            while (read++ < 500 && _rp.Read(out f))
            {
                _framesThisWindow++;
                _lastGood = now;
                lock (_gate) _mon.Feed(f, now);
            }

            if (_fpsWindow == DateTime.MinValue) _fpsWindow = now;
            if ((now - _fpsWindow).TotalSeconds >= 1)
            {
                _fps = _framesThisWindow;
                _framesThisWindow = 0;
                _fpsWindow = now;
            }

            lock (_gate) _mon.SampleTrend(now);

            // An adapter that stops answering keeps IsConnected true, so silence is the real signal.
            if (_autoReconnect && _lastGood != DateTime.MinValue
                && (now - _lastGood).TotalSeconds > 6
                && (now - _lastReconnect).TotalMilliseconds > ReconnectEveryMs)
            {
                _lastReconnect = now;
                Log("Auto-reconnect…");
                _rp.Disconnect();
                lock (_gate) _mon.ClearLive();
            }
        }

        void TryConnect(DateTime now)
        {
            if ((now - _lastReconnect).TotalMilliseconds < ReconnectEveryMs && _lastReconnect != DateTime.MinValue)
                return;
            _lastReconnect = now;
            Rp1210Api api;
            lock (_gate) api = _api;
            _busy = true; _busyWhat = "connecting";
            bool ok;
            try { ok = _rp.Connect(api); }
            catch (Exception ex) { ok = false; Log("Connect threw: " + ex.Message); }
            finally { _busy = false; _busyWhat = ""; }
            if (!ok)
            {
                Log("Connect failed: " + _rp.LastError);
                if (!_autoReconnect) _wantConnected = false;
                return;
            }
            _lastGood = now;
            _fpsWindow = now;
            _framesThisWindow = 0;
            lock (_gate) _mon.ClearLive();
            Log("Connected  device " + _rp.DeviceId + "  " + _rp.Protocol);
            RequestCodes(true);
        }

        void DrainCommandsWhileOffline()
        {
            lock (_gate)
            {
                while (_cmds.Count > 0)
                {
                    BusCommand c = _cmds.Dequeue();
                    if (c.Kind == BusCmdKind.ClearBusStats) _mon.ClearBusStats();
                    else if (c.Kind == BusCmdKind.ClearHistory) _mon.ClearHistory();
                }
            }
        }

        void DrainCommands()
        {
            while (true)
            {
                BusCommand c;
                lock (_gate)
                {
                    if (_cmds.Count == 0) return;
                    c = _cmds.Dequeue();
                }
                Execute(c);
            }
        }

        void Execute(BusCommand c)
        {
            switch (c.Kind)
            {
                case BusCmdKind.RequestCodes:
                    RequestCodes(true);
                    break;
                case BusCmdKind.ClearPrevious:
                {
                    byte[] payload = new byte[8];
                    _rp.SendJ1939(0xFECC, 0, payload);
                    _rp.SendJ1939(0xFECC, 255, payload);
                    _rp.RequestPgn(0xFECC, 0);
                    _rp.RequestPgn(0xFECC, 255);
                    Log("Sent DM3 (clear previously active)");
                    RequestCodes(true);
                    break;
                }
                case BusCmdKind.ResetAll:
                {
                    _busy = true; _busyWhat = "resetting codes";
                    string report;
                    try { report = _rp.ResetAllFaults(); }
                    catch (Exception ex) { report = "Reset failed: " + ex.Message; }
                    finally { _busy = false; _busyWhat = ""; }
                    lock (_gate) _lastResetReport = report;
                    Log(report.Replace("\r\n", " | "));
                    Thread.Sleep(400);
                    RequestCodes(true);
                    break;
                }
                case BusCmdKind.PingIdentity:
                    lock (_gate)
                    {
                        _mon.Vin = ""; _mon.Sw = ""; _mon.CompId = ""; _mon.HoursText = "";
                    }
                    _rp.RequestPgn(0xFEEC, 0);
                    _rp.RequestPgn(0xFEEC, 255);
                    _rp.RequestPgn(0xFEDA, 0);
                    _rp.RequestPgn(0xFEDA, 255);
                    _rp.RequestPgn(0xFEEB, 0);
                    _rp.RequestPgn(0xFEE5, 0);
                    _rp.RequestPgn(0xFECA, 0);
                    Log("Pinged ECM for VIN / software / component / hours / DM1");
                    break;
                case BusCmdKind.SetTsc1:
                    _rp.SendTsc1(c.Arg);
                    Log(c.Arg <= 0
                        ? "TSC1 release — ECM has its own throttle back"
                        : "TSC1 request " + c.Arg + " RPM (heartbeat while held)");
                    break;
                case BusCmdKind.Dm13:
                    _rp.SendDm13(c.Flag);
                    Log(c.Flag ? "DM13 Stop Broadcast sent" : "DM13 Start Broadcast sent");
                    break;
                case BusCmdKind.ClearBusStats:
                    lock (_gate) _mon.ClearBusStats();
                    Log("Bus stats cleared");
                    break;
                case BusCmdKind.ClearHistory:
                    lock (_gate) _mon.ClearHistory();
                    Log("Trend and timeline cleared");
                    break;
            }
        }

        void RequestCodes(bool log)
        {
            if (!_rp.IsConnected) return;
            _rp.RequestPgn(0xFECA, 0);
            _rp.RequestPgn(0xFECA, 255);
            _rp.RequestPgn(0xFECB, 0);
            _rp.RequestPgn(0xFECB, 255);
            _rp.RequestPgn(0xFE56, 0);
            _lastReq = DateTime.UtcNow;
            if (log) Log("Requested DM1 / DM2 / DEF tank");
        }

        public BusSnapshot Snapshot()
        {
            var s = new BusSnapshot();
            DateTime now = DateTime.UtcNow;
            s.Connected = _rp.IsConnected;
            s.Busy = _busy;
            s.BusyWhat = _busyWhat ?? "";
            s.DeviceId = _rp.DeviceId;
            s.Protocol = _rp.Protocol ?? "";
            s.LastError = _rp.LastError ?? "";
            s.AdapterName = _rp.Api == null ? "" : _rp.Api.ToString();
            s.Fps = _fps;
            s.TscRpm = _tscRpm;
            s.QuietSeconds = _lastGood == DateTime.MinValue ? 0 : (now - _lastGood).TotalSeconds;
            s.Stale = s.Connected && _lastGood != DateTime.MinValue && s.QuietSeconds > StaleAfterSeconds;

            lock (_gate)
            {
                s.EngineSa = _mon.EngineSa;
                int view = _viewSa < 0 ? _mon.EngineSa : _viewSa;
                s.ViewSa = view;
                ModuleFaults m = _mon.Module(view);
                s.Active.AddRange(m.Active);
                s.Prev.AddRange(m.Prev);
                s.Red = m.Red; s.Amber = m.Amber; s.Protect = m.Protect; s.Mil = m.Mil;
                s.FaultModules = _mon.ModulesWithFaults();
                s.Rpm = _mon.Rpm;
                s.CoolantC = _mon.CoolantC;
                s.OilKpa = _mon.OilKpa;
                s.BatteryV = _mon.BatteryV;
                s.FuelLph = _mon.FuelLph;
                s.DefText = _mon.DefText();
                s.OilText = _mon.OilText();
                s.Vin = _mon.Vin; s.Sw = _mon.Sw; s.CompId = _mon.CompId; s.Hours = _mon.HoursText;
                s.HasSpn5246 = _mon.HasSpn(5246);
                s.HasTankFmi9 = _mon.HasTankFmi9();
                s.Has1569 = _mon.HasSpn(1569);
                s.Pgns = _mon.PgnRows();
                s.Sas = _mon.SaRows();
                s.LastResetReport = _lastResetReport;
            }
            if (!s.Connected)
            {
                s.DefText = "—";
                s.OilText = "—";
            }
            return s;
        }

        public List<TrendSample> TrendCopy()
        {
            lock (_gate) return _mon.Trend.Copy();
        }

        public List<DtcEvent> TimelineCopy(int sa)
        {
            lock (_gate)
            {
                int view = sa < 0 ? _mon.EngineSa : sa;
                return _mon.Module(view).Timeline.Recent();
            }
        }

        public string TimelineCsv(int sa, string jobTag)
        {
            lock (_gate)
            {
                int view = sa < 0 ? _mon.EngineSa : sa;
                return _mon.Module(view).Timeline.Csv(jobTag);
            }
        }
    }
}
