using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace J1939Reader
{
    public class Inline7Control : UserControl
    {
        readonly Rp1210 _rp = new Rp1210();
        readonly Timer _timer = new Timer();
        readonly List<Dtc> _active = new List<Dtc>();
        readonly List<Dtc> _prev = new List<Dtc>();

        Button _btnConnect, _btnDisc, _btnRefresh, _btnClearPrev, _btnClearActive;
        Label _status, _rpm, _lamps, _def;
        ListBox _lstActive, _lstPrev;
        TextBox _log, _codeHelp, _liveHelp, _idBox;
        ListBox _lstLive;
        CheckBox _chkSafe, _chkAuto, _chkTsc800, _chkTsc1200, _chkQuietBus;
        Button _btnPing;
        ToolTip _tip;
        Label _mon;
        DateTime _lastReq = DateTime.MinValue;
        double _rpmVal = -1;
        bool _red, _amber;
        string _defTxt = "—";
        string _coolant = "—", _oil = "—", _batt = "—", _fuelRate = "—";
        string _vin = "", _sw = "";
        int _tscRpm; // 0 = released
        List<SwitchRow> _liveRows = new List<SwitchRow>();
        readonly BamAssembler _bam = new BamAssembler();
        int _frameCount;
        DateTime _frameT0 = DateTime.UtcNow;
        int _fps;
        TabControl _tabs;
        ListBox _lstPgn, _lstSa;
        TextBox _diffBox;
        CheckBox _chkAutoRe;
        readonly Dictionary<string, PgnRow> _pgns = new Dictionary<string, PgnRow>();
        readonly Dictionary<int, SaRow> _sas = new Dictionary<int, SaRow>();
        readonly List<string> _markers = new List<string>();
        Snap _snapA, _snapB;
        bool _wantConnected;
        DateTime _lastGood = DateTime.UtcNow;
        DateTime _lastRe = DateTime.MinValue;
        DateTime _lastBusUi = DateTime.MinValue;

        public string JobTag { get; set; }

        public Inline7Control()
        {
            Dock = DockStyle.Fill;
            Font = new Font("Segoe UI", 9.5f);
            Disposed += (s, e) =>
            {
                _timer.Stop();
                try { if (_rp.IsConnected) _rp.SendTsc1(0); } catch { }
                _rp.Dispose();
            };

            _btnConnect = MkBtn("Connect", 12, 12, 110);
            _btnDisc = MkBtn("Disconnect", 128, 12, 110);
            _btnDisc.Enabled = false;
            _btnRefresh = MkBtn("Refresh codes", 248, 12, 120);
            _btnRefresh.Enabled = false;
            _btnClearPrev = MkBtn("Clear previous", 378, 12, 130);
            _btnClearPrev.Enabled = false;
            _btnClearActive = MkBtn("Reset all codes", 514, 12, 130);
            _btnClearActive.Enabled = false;
            var btnSave = MkBtn("Save", 650, 12, 60);
            var btnShot = MkBtn("Shot", 714, 12, 56);
            var btnKey = MkBtn("KEY-ON", 774, 12, 70);
            var btnCrk = MkBtn("CRANK", 848, 12, 70);
            var btnRel = MkBtn("RELEASE", 922, 12, 80);
            btnSave.Click += (s, e) => SaveSession();
            btnShot.Click += (s, e) => TakeShot();
            btnKey.Click += (s, e) => Mark("KEY-ON");
            btnCrk.Click += (s, e) => Mark("CRANK");
            btnRel.Click += (s, e) => Mark("RELEASE");

            _btnConnect.Click += (s, e) => DoConnect();
            _btnDisc.Click += (s, e) => DoDisconnect();
            _btnRefresh.Click += (s, e) => RequestCodes(true);
            _btnClearPrev.Click += (s, e) => DoClear(false);
            _btnClearActive.Click += (s, e) => DoResetAll();

            _status = new Label { Left = 12, Top = 48, Width = 1000, Height = 20, Text = "Disconnected — close Guidanz / J1939 tool / USB-Link Explorer before Connect." };
            _rpm = new Label { Left = 12, Top = 70, Width = 200, Height = 22, Text = "RPM: —", Font = new Font(Font, FontStyle.Bold) };
            _lamps = new Label { Left = 220, Top = 70, Width = 300, Height = 22, Text = "Lamps: —" };
            _def = new Label { Left = 530, Top = 70, Width = 480, Height = 22, Text = "DEF: —" };

            _tabs = new TabControl
            {
                Left = 12,
                Top = 98,
                Width = 1000,
                Height = 610,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            _tabs.TabPages.Add(BuildCodesTab());
            _tabs.TabPages.Add(BuildAdvancedTab());
            _tabs.TabPages.Add(BuildBusTab());
            Controls.Add(_tabs);
            Controls.AddRange(new Control[] { _status, _rpm, _lamps, _def });

            _timer.Interval = 40;
            _timer.Tick += OnTick;
            RefreshLiveList();
            if (_chkAuto != null) _chkAuto.Checked = true;
            SetConnected(false);
        }

        Button MkBtn(string t, int x, int y, int w)
        {
            var b = new Button { Text = t, Left = x, Top = y, Width = w, Height = 28 };
            Controls.Add(b);
            return b;
        }

        CheckBox MkToggle(Control parent, int x, int y, int w, string label, string hover)
        {
            var c = new CheckBox { Left = x, Top = y, Width = w, Height = 22, Text = label };
            parent.Controls.Add(c);
            if (_tip != null) _tip.SetToolTip(c, hover);
            c.MouseEnter += (s, e) =>
            {
                if (_liveHelp != null) _liveHelp.Text = label + "\r\n\r\n" + hover;
            };
            return c;
        }

        TabPage BuildCodesTab()
        {
            var p = new TabPage("Codes");
            var l1 = new Label { Left = 8, Top = 8, Width = 470, Text = "Active DTCs (DM1) — click a code for explanation" };
            var l2 = new Label { Left = 490, Top = 8, Width = 470, Text = "Previously active (DM2) — click a code for explanation" };
            _lstActive = new ListBox { Left = 8, Top = 28, Width = 470, Height = 200, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _lstPrev = new ListBox { Left = 490, Top = 28, Width = 470, Height = 200, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _lstActive.SelectedIndexChanged += (s, e) => ShowCode(_lstActive);
            _lstPrev.SelectedIndexChanged += (s, e) => ShowCode(_lstPrev);

            var l3 = new Label { Left = 8, Top = 234, Width = 400, Text = "What this code means" };
            _codeHelp = HelpBox(8, 254, 952, 150);
            _codeHelp.Text = "Connect, then click a code.\r\n\r\nFMI 9 = the ECM is not receiving that sensor, not that the number is slightly wrong.\r\nThis app will not disable DEF/SCR. That would wreck the aftertreatment.";

            _log = new TextBox
            {
                Left = 8, Top = 412, Width = 952, Height = 140,
                Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true,
                Font = new Font("Consolas", 9f),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };

            p.Controls.AddRange(new Control[] { l1, l2, _lstActive, _lstPrev, l3, _codeHelp, _log });
            // stretch lists with the tab
            p.Resize += (s, e) =>
            {
                int w = p.ClientSize.Width;
                int half = (w - 24) / 2;
                _lstActive.Width = half;
                _lstPrev.Left = 16 + half;
                _lstPrev.Width = half;
                _codeHelp.Width = w - 20;
                _log.Width = w - 20;
            };
            return p;
        }

        TabPage BuildAdvancedTab()
        {
            var p = new TabPage("Advanced diagnostics");
            var warn = new Label
            {
                Left = 8, Top = 4, Width = 960, Height = 48,
                Text = "These are standard J1939 diagnostic controls the ECM already understands. " +
                       "They isolate harness vs computer. They do not add new Cummins calibration bits, " +
                       "and they do not disable DEF, SCR, Red Stop, or engine protection."
            };

            var l1 = new Label { Left = 8, Top = 52, Width = 480, Text = "Live ECM states (status — click for explanation)" };
            _lstLive = new ListBox { Left = 8, Top = 72, Width = 480, Height = 160 };
            _liveHelp = HelpBox(500, 72, 460, 160);
            _liveHelp.Text = "Click a live state.";
            _lstLive.SelectedIndexChanged += (s, e) =>
            {
                int i = _lstLive.SelectedIndex;
                if (i >= 0 && i < _liveRows.Count)
                    _liveHelp.Text = _liveRows[i].Name + "\r\nState: " + _liveRows[i].State + "\r\n\r\n" + _liveRows[i].Detail;
            };

            _mon = new Label
            {
                Left = 8, Top = 238, Width = 960, Height = 40,
                Text = "Coolant: —    Oil: —    Battery: —    Fuel rate: —"
            };

            var g = new GroupBox { Left = 8, Top = 276, Width = 952, Height = 168, Text = "Diagnostic toggles — hover a switch for the full description" };
            _tip = new ToolTip
            {
                AutoPopDelay = 32000,
                InitialDelay = 250,
                ReshowDelay = 200,
                ShowAlways = true,
                ToolTipTitle = "Diagnostic toggle",
                IsBalloon = false
            };
            _chkSafe = MkToggle(g, 12, 20, 920,
                "Safety: engine already running, compressor unloaded, area clear",
                "Must be ON before a speed-request toggle will send TSC1. The engine will not start from these switches. TSC1 cannot override Red Stop or DEF inducement.");
            _chkAuto = MkToggle(g, 12, 44, 450,
                "Auto-refresh codes (every 2 s)",
                "ON: keep asking the ECM for DM1/DM2 and DEF tank so the lists stay live.\nOFF: lists only update when you click Refresh or Reset.\nThis does not change any ECM function.");
            _chkTsc800 = MkToggle(g, 12, 68, 450,
                "Hold idle 800 RPM (TSC1)",
                "ON: send J1939 TSC1 speed-control 800 RPM every 40 ms while the engine is already running.\nUse: confirm the ECM accepts a diagnostic throttle (RPM should sit near 800).\nOFF: release control back to the ECM / compressor controller.\nWill not crank, will not bypass inducement, will not disable DEF. Requires the safety toggle.");
            _chkTsc1200 = MkToggle(g, 480, 68, 450,
                "Hold 1200 RPM (TSC1)",
                "ON: same as idle hold but 1200 RPM. Mutually exclusive with 800.\nUse: see if the ECM will take a mid-speed diagnostic request after it has fired.\nOFF: release. Requires the safety toggle. Not an engine-protection or DEF bypass.");
            _chkQuietBus = MkToggle(g, 12, 92, 450,
                "Quiet bus (DM13 stop J1939 broadcast)",
                "ON: J1939-73 DM13 Stop Broadcast — asks modules to stop periodic traffic so you can see what still talks.\nOFF: DM13 Start Broadcast — normal traffic resumes.\nUse: isolate a noisy/missing module. Do not leave ON. This is not a sensor disable and not a DEF bypass.");
            _chkTsc800.CheckedChanged += (s, e) =>
            {
                if (_chkTsc800.Checked) { _chkTsc1200.Checked = false; SetTsc(800); }
                else if (!_chkTsc1200.Checked) SetTsc(0);
            };
            _chkTsc1200.CheckedChanged += (s, e) =>
            {
                if (_chkTsc1200.Checked) { _chkTsc800.Checked = false; SetTsc(1200); }
                else if (!_chkTsc800.Checked) SetTsc(0);
            };
            _chkQuietBus.CheckedChanged += (s, e) =>
            {
                if (!_rp.IsConnected) return;
                _rp.SendDm13(_chkQuietBus.Checked);
                Log(_chkQuietBus.Checked ? "DM13 Stop Broadcast sent" : "DM13 Start Broadcast sent");
            };
            _btnPing = new Button { Left = 480, Top = 92, Width = 200, Height = 24, Text = "Ping ECM identity" };
            _tip.SetToolTip(_btnPing, "Request VIN, software ID, hours, and DM1. If RPM/codes are already live but VIN never appears, this industrial ECM may not publish VIN — the computer is still alive.");
            _btnPing.Click += (s, e) => PingEcm();
            g.Controls.Add(_btnPing);

            var l3 = new Label { Left = 8, Top = 450, Width = 700, Text = "ECM identity / remaining codes after reset  (hover toggles above for descriptions)" };
            _idBox = HelpBox(8, 470, 952, 96);
            _idBox.Text = CannotDoText();

            p.Controls.AddRange(new Control[] { warn, l1, _lstLive, _liveHelp, _mon, g, l3, _idBox });
            return p;
        }

        TabPage BuildBusTab()
        {
            var p = new TabPage("Bus / snapshots");
            var l1 = new Label { Left = 8, Top = 8, Width = 480, Text = "PGN traffic (who is talking)" };
            var l2 = new Label { Left = 500, Top = 8, Width = 480, Text = "Address claim / modules" };
            _lstPgn = new ListBox { Left = 8, Top = 28, Width = 480, Height = 250 };
            _lstSa = new ListBox { Left = 500, Top = 28, Width = 460, Height = 250 };

            _chkAutoRe = new CheckBox
            {
                Left = 8, Top = 284, Width = 420, Height = 22,
                Text = "Auto-reconnect if the adapter drops",
                Checked = true
            };
            var bA = new Button { Left = 8, Top = 312, Width = 110, Height = 26, Text = "Snapshot A" };
            var bB = new Button { Left = 124, Top = 312, Width = 110, Height = 26, Text = "Snapshot B" };
            var bD = new Button { Left = 240, Top = 312, Width = 80, Height = 26, Text = "Diff" };
            var bClr = new Button { Left = 326, Top = 312, Width = 90, Height = 26, Text = "Clear stats" };
            bA.Click += (s, e) => { _snapA = TakeSnap("A before"); Log("Snapshot A saved"); ShowDiff(); };
            bB.Click += (s, e) => { _snapB = TakeSnap("B after"); Log("Snapshot B saved"); ShowDiff(); };
            bD.Click += (s, e) => ShowDiff();
            bClr.Click += (s, e) => { _pgns.Clear(); _sas.Clear(); RefreshBusLists(); Log("Bus stats cleared"); };

            _diffBox = HelpBox(8, 346, 952, 210);
            _diffBox.Text = "Snapshot A before a repair, Snapshot B after, then Diff.\r\nPGN list updates live. SA 0 = ECM, 48 = compressor controller, 128 = Grayhill. If the tank header talks you will see DEF PGN FE56 from SA 0.";

            p.Controls.AddRange(new Control[] { l1, l2, _lstPgn, _lstSa, _chkAutoRe, bA, bB, bD, bClr, _diffBox });
            return p;
        }

        static string CannotDoText()
        {
            return
                "Ping ECM to fill VIN / software here.\r\n\r\n" +
                "What this app can switch (standard J1939):\r\n" +
                "• TSC1 speed request / release — tests whether the ECM accepts a diagnostic throttle\r\n" +
                "• Identity request — VIN, software; no answer after a live DM1 usually means power/harness, not 'needs a new switch'\r\n" +
                "• Code clear (DM3/DM11) — after the fault condition is gone\r\n\r\n" +
                "What cannot be created from this adapter:\r\n" +
                "• New calibration switches inside the Cummins (those only exist if Cummins put them in the file)\r\n" +
                "• Cylinder cutout, EGR/VGT/DEF pump actuator tests — proprietary Guidanz\r\n" +
                "• Disable DEF, SCR, Red Stop, or engine protection — not diagnostic isolation, and can wreck the engine\r\n\r\n" +
                "To decide 'replace the ECM': ping identity + watch DM1. If the ECM broadcasts RPM/lamps/codes but DEF tank PGNs stay n/a, the computer is alive and the tank header/harness is not.";
        }

        static TextBox HelpBox(int x, int y, int w, int h)
        {
            return new TextBox
            {
                Left = x, Top = y, Width = w, Height = h,
                Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true,
                Font = new Font("Segoe UI", 9f)
            };
        }

        void Log(string s)
        {
            if (_log == null) return;
            _log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + Environment.NewLine);
        }

        void SetConnected(bool on)
        {
            _btnConnect.Enabled = !on;
            _btnDisc.Enabled = on;
            _btnRefresh.Enabled = on;
            _btnClearPrev.Enabled = on;
            _btnClearActive.Enabled = on;
            if (_btnPing != null) _btnPing.Enabled = on;
            if (_chkTsc800 != null)
            {
                _chkTsc800.Enabled = on;
                _chkTsc1200.Enabled = on;
                _chkQuietBus.Enabled = on;
                _chkAuto.Enabled = on;
            }
        }

        void DoConnect()
        {
            Log("Connecting… close Guidanz / J1939 tool / USB-Link Explorer first.");
            bool explorer = false;
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcesses())
                    if (p.ProcessName.IndexOf("USBLink", StringComparison.OrdinalIgnoreCase) >= 0
                        || p.ProcessName.IndexOf("Guidanz", StringComparison.OrdinalIgnoreCase) >= 0)
                        explorer = true;
            }
            catch { }
            if (!_rp.Connect())
            {
                _status.Text = "Connect failed: " + _rp.LastError;
                Log(_status.Text);
                string extra = explorer ? "\n\nUSB-Link 3 Explorer or Guidanz is still running and is holding the adapter. Close it, then Connect again." : "";
                MessageBox.Show(this,
                    "Could not open the INLINE 7." + extra + "\n\n" + _rp.LastError,
                    "Connect failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _status.Text = "Connected  device " + _rp.DeviceId + "  " + _rp.Protocol;
            Log(_status.Text);
            _wantConnected = true;
            _lastGood = DateTime.UtcNow;
            SetConnected(true);
            RequestCodes(true);
            _timer.Start();
        }

        void DoDisconnect()
        {
            _wantConnected = false;
            _timer.Stop();
            if (_rp.IsConnected && _tscRpm > 0)
            {
                _rp.SendTsc1(0);
                _tscRpm = 0;
            }
            _rp.Disconnect();
            SetConnected(false);
            _status.Text = "Disconnected";
            Log("Disconnected");
            _tscRpm = 0;
            RefreshLiveList();
        }

        void RequestCodes(bool log)
        {
            if (!_rp.IsConnected) return;
            _rp.RequestPgn(0xFECA, 0);
            _rp.RequestPgn(0xFECA, 255);
            _rp.RequestPgn(0xFECB, 0);
            _rp.RequestPgn(0xFECB, 255);
            _rp.RequestPgn(0xFE56, 0);
            _lastReq = DateTime.Now;
            if (log) Log("Requested DM1 / DM2 / DEF tank");
        }

        void DoClear(bool active)
        {
            if (active) { DoResetAll(); return; }
            var r = MessageBox.Show(this,
                "Clear PREVIOUSLY ACTIVE codes only (DM3)?\n\nActive faults that are still happening will stay.",
                "Clear previous", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            byte[] payload = new byte[8];
            _rp.SendJ1939(0xFECC, 0, payload);
            _rp.SendJ1939(0xFECC, 255, payload);
            _rp.RequestPgn(0xFECC, 0);
            _rp.RequestPgn(0xFECC, 255);
            Log("Sent DM3 (clear previously active)");
            RequestCodes(true);
        }

        void DoResetAll()
        {
            var r = MessageBox.Show(this,
                "Reset all codes on the ECM?\n\n" +
                "Sends J1939 DM11 + DM3 (3 rounds) and UDS 0x14 if ISO15765 opens.\n\n" +
                "Red Stop and Amber are not separate codes. They are lamp bits on DM1. They go OFF by themselves when the DTCs that set them are gone.\n" +
                "There is no legal 'Red Stop off' / 'Amber off' switch — forcing the lamps off while 5246 / FMI 9 are still active would hide a no-fuel command, not diagnose it.\n\n" +
                "If FMI 9 or inducement is still true, lamps and those SPNs will come right back.\n\nContinue?",
                "Reset all codes",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            Log("Reset all codes…");
            _timer.Stop();
            Cursor = Cursors.WaitCursor;
            string result = "";
            try { result = _rp.ResetAllFaults(); }
            finally { Cursor = Cursors.Default; _timer.Start(); }
            Log(result.Replace("\r\n", " | "));
            var until = DateTime.Now.AddMilliseconds(800);
            while (DateTime.Now < until) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
            RequestCodes(true);
            string remain = LampHolders();
            if (_idBox != null)
                _idBox.Text = result + "\r\n\r\n"
                    + "Red Stop: " + (_red ? "STILL ON" : "off") + "     Amber: " + (_amber ? "STILL ON" : "off") + "\r\n"
                    + "Lamps are not cleared separately. They track the active DTCs below.\r\n\r\n"
                    + remain
                    + "\r\nIf Red Stop stays on, SPN 5246 / 1569 are still latched or the tank header is still FMI 9. Fix that, reset again. Guidanz aftertreatment reset may still be required for 5246.";
        }

        string LampHolders()
        {
            if (_active.Count == 0)
                return "No active DTCs parsed yet — wait a second for DM1, or the reset held and lamps should drop.\r\n";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Active DTCs holding the lamps:");
            foreach (Dtc d in _active)
            {
                string role = "";
                if (d.Spn == 5246 || d.Spn == 1569 || d.Spn == 5245) role = "  ← drives Red Stop / no-fuel";
                else if (d.Spn == 1761 || d.Spn == 3031 || d.Spn == 3364) role = "  ← tank not talking; feeds inducement";
                sb.AppendLine("  SPN " + d.Spn + " FMI " + d.Fmi + "  " + d.Name + role);
            }
            return sb.ToString();
        }

        void SetTsc(int rpm)
        {
            if (!_rp.IsConnected) return;
            if (rpm > 0)
            {
                if (_chkSafe == null || !_chkSafe.Checked)
                {
                    if (_chkTsc800 != null) _chkTsc800.Checked = false;
                    if (_chkTsc1200 != null) _chkTsc1200.Checked = false;
                    MessageBox.Show(this,
                        "Turn ON the safety toggle first. Speed request is only for an engine that is already running, unloaded.\nIt will not start a shut-off engine and it will not override Red Stop or DEF inducement.",
                        "Speed request", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            _tscRpm = rpm;
            _rp.SendTsc1(rpm);
            Log(rpm <= 0 ? "TSC1 release — ECM has its own throttle back" : ("TSC1 request " + rpm + " RPM (heartbeat while held)"));
        }

        void PingEcm()
        {
            if (!_rp.IsConnected) return;
            _vin = ""; _sw = "";
            _rp.RequestPgn(0xFEEC, 0);
            _rp.RequestPgn(0xFEEC, 255);
            _rp.RequestPgn(0xFEDA, 0);
            _rp.RequestPgn(0xFEDA, 255);
            _rp.RequestPgn(0xFEEB, 0);
            _rp.RequestPgn(0xFEE5, 0);
            _rp.RequestPgn(0xFECA, 0);
            Log("Pinged ECM for VIN / software / hours / DM1");
            _idBox.Text = "Waiting for ECM identity…\r\nIf DM1 is already updating at the top but VIN never appears, the computer is alive — it just may not publish VIN on this industrial calibration.\r\n\r\n" + CannotDoText();
        }

        void ShowCode(ListBox box)
        {
            Dtc d = SelectedDtc(box);
            if (d == null) return;
            _codeHelp.Text = CodeBook.Explain(d.Spn, d.Fmi) + "\r\nOccurrence count: " + d.Occ;
        }

        Dtc SelectedDtc(ListBox box)
        {
            int i = box.SelectedIndex;
            var src = box == _lstActive ? _active : _prev;
            if (i < 0 || i >= src.Count) return null;
            return src[i];
        }

        void OnTick(object sender, EventArgs e)
        {
            if (!_rp.IsConnected)
            {
                TickReconnect();
                return;
            }
            try { OnTickCore(); }
            catch (Exception ex) { Log("tick error: " + ex.Message); }
        }

        void HandleFrame(J1939Frame f, ref bool dm1hit, ref bool dm2hit)
        {
            if (f == null || f.Data == null) return;
            if (f.Pgn == 0xF004 && f.Sa == 0)
                _rpmVal = J1939Decode.Rpm(f.Data);
            else if (f.Pgn == 0xFE56 && f.Sa == 0)
                _defTxt = J1939Decode.DefText(f.Data);
            else if (f.Pgn == 0xFEEE && f.Sa == 0 && f.Data.Length > 0)
                _coolant = J1939Decode.TempC(f.Data[0]);
            else if (f.Pgn == 0xFEEF && f.Sa == 0 && f.Data.Length > 3)
                _oil = J1939Decode.PressureKpa(f.Data[3], 4);
            else if (f.Pgn == 0xFEF7 && f.Sa == 0)
                _batt = J1939Decode.BatteryV(f.Data);
            else if (f.Pgn == 0xFEF2 && f.Sa == 0)
                _fuelRate = J1939Decode.FuelRate(f.Data);
            else if (f.Pgn == 0xFEEC && f.Sa == 0)
            {
                string v = J1939Decode.Ascii(f.Data);
                if (v.Length > 0) { _vin = v; UpdateIdBox(); }
            }
            else if (f.Pgn == 0xFEDA && f.Sa == 0)
            {
                string v = J1939Decode.Ascii(f.Data);
                if (v.Length > 0) { _sw = v; UpdateIdBox(); }
            }
            else if (f.Pgn == 0xFECA && f.Sa == 0)
            {
                _active.Clear();
                J1939Decode.ParseDm(f.Data, _active, out _red, out _amber);
                dm1hit = true;
            }
            else if (f.Pgn == 0xFECB && f.Sa == 0)
            {
                _prev.Clear();
                bool r, a;
                J1939Decode.ParseDm(f.Data, _prev, out r, out a);
                dm2hit = true;
            }
        }

        void OnTickCore()
        {
            if (_tscRpm > 0) _rp.SendTsc1(_tscRpm);
            bool auto = _chkAuto == null || _chkAuto.Checked;
            if (auto && (DateTime.Now - _lastReq).TotalSeconds > 2)
                RequestCodes(false);

            bool dm1hit = false, dm2hit = false;
            J1939Frame f;
            int n = 0;
            while (n++ < 400 && _rp.Read(out f))
            {
                _frameCount++;
                _lastGood = DateTime.UtcNow;
                TrackBus(f);
                J1939Frame assembled;
                if (!_bam.Feed(f, out assembled)) continue;
                HandleFrame(assembled, ref dm1hit, ref dm2hit);
            }
            if ((DateTime.UtcNow - _frameT0).TotalSeconds >= 1)
            {
                _fps = _frameCount;
                _frameCount = 0;
                _frameT0 = DateTime.UtcNow;
                if (_rp.IsConnected)
                    _status.Text = "Connected  device " + _rp.DeviceId + "  " + _rp.Protocol + "  " + _fps + " frames/s";
            }

            _rpm.Text = _rpmVal < 0 ? "RPM: —" : ("RPM: " + _rpmVal.ToString("0"));
            _lamps.Text = "Red Stop: " + (_red ? "ON" : "off") + "     Amber: " + (_amber ? "ON" : "off");
            _lamps.ForeColor = _red ? Color.Firebrick : Color.Black;
            _def.Text = "DEF: " + _defTxt;
            if (_mon != null)
                _mon.Text = "Coolant: " + _coolant + "    Oil: " + _oil + "    Battery: " + _batt + "    Fuel rate: " + _fuelRate
                    + (_tscRpm > 0 ? "    TSC1 holding " + _tscRpm + " RPM" : "    TSC1 released");

            if (dm1hit) FillList(_lstActive, _active);
            if (dm2hit) FillList(_lstPrev, _prev);
            if (dm1hit) RefreshLiveList();
            if ((DateTime.UtcNow - _lastBusUi).TotalMilliseconds > 800)
            {
                _lastBusUi = DateTime.UtcNow;
                RefreshBusLists();
            }
            TickReconnect();
        }

        bool HasSpn(int spn)
        {
            foreach (Dtc d in _active) if (d.Spn == spn) return true;
            return false;
        }

        bool HasTankFmi9()
        {
            foreach (Dtc d in _active)
                if ((d.Spn == 1761 || d.Spn == 3031 || d.Spn == 3364) && d.Fmi == 9) return true;
            return false;
        }

        void RefreshLiveList()
        {
            if (_lstLive == null) return;
            int keep = _lstLive.SelectedIndex;
            _liveRows = FeatureBook.Live(_rp.IsConnected, _red, _amber, HasSpn(5246), HasTankFmi9(), HasSpn(1569), _rpmVal, _defTxt);
            _lstLive.BeginUpdate();
            _lstLive.Items.Clear();
            foreach (SwitchRow r in _liveRows) _lstLive.Items.Add(r.ToString());
            _lstLive.EndUpdate();
            if (keep >= 0 && keep < _lstLive.Items.Count) _lstLive.SelectedIndex = keep;
        }

        void UpdateIdBox()
        {
            if (_idBox == null) return;
            _idBox.Text =
                "VIN / NAME: " + (string.IsNullOrEmpty(_vin) ? "(not published yet)" : _vin) + "\r\n" +
                "Software: " + (string.IsNullOrEmpty(_sw) ? "(not published yet)" : _sw) + "\r\n" +
                "DM1: " + (_active.Count == 0 ? "no active codes parsed" : (_active.Count + " active")) + "\r\n" +
                "RPM broadcast: " + (_rpmVal < 0 ? "none" : _rpmVal.ToString("0")) + "\r\n\r\n" +
                CannotDoText();
        }

        static void FillList(ListBox box, List<Dtc> items)
        {
            int keep = box.SelectedIndex;
            box.BeginUpdate();
            box.Items.Clear();
            if (items.Count == 0) box.Items.Add("(none)");
            else foreach (Dtc d in items) box.Items.Add(d.ToString());
            box.EndUpdate();
            if (keep >= 0 && keep < box.Items.Count) box.SelectedIndex = keep;
        }

        void TrackBus(J1939Frame f)
        {
            if (f == null) return;
            string k = f.Pgn.ToString("X4") + "/" + f.Sa;
            PgnRow pr;
            if (!_pgns.TryGetValue(k, out pr))
            {
                pr = new PgnRow { Pgn = f.Pgn, Sa = f.Sa };
                _pgns[k] = pr;
            }
            pr.Count++;
            pr.Last = DateTime.UtcNow;
            pr.LastLen = f.Data == null ? 0 : f.Data.Length;

            SaRow sr;
            if (!_sas.TryGetValue(f.Sa, out sr))
            {
                sr = new SaRow { Sa = f.Sa };
                _sas[f.Sa] = sr;
            }
            sr.Count++;
            sr.Last = DateTime.UtcNow;
            if (f.Pgn == 0xEE00 && f.Data != null)
            {
                sr.Claimed = true;
                sr.NameHex = BitConverter.ToString(f.Data);
            }
        }

        void RefreshBusLists()
        {
            if (_lstPgn == null) return;
            var now = DateTime.UtcNow;
            var pgnList = new List<PgnRow>(_pgns.Values);
            pgnList.Sort((a, b) => b.Count.CompareTo(a.Count));
            _lstPgn.BeginUpdate();
            _lstPgn.Items.Clear();
            int n = 0;
            foreach (PgnRow r in pgnList)
            {
                double age = (now - r.Last).TotalSeconds;
                string pn = Names.Pgn(r.Pgn);
                _lstPgn.Items.Add(string.Format("{0:X4}  SA{1,-3} {2,-22} x{3,-5} {4:0.0}s ago",
                    r.Pgn, r.Sa, pn, r.Count, age));
                if (++n >= 80) break;
            }
            _lstPgn.EndUpdate();

            var saList = new List<SaRow>(_sas.Values);
            saList.Sort((a, b) => a.Sa.CompareTo(b.Sa));
            _lstSa.BeginUpdate();
            _lstSa.Items.Clear();
            foreach (SaRow r in saList)
            {
                double age = (now - r.Last).TotalSeconds;
                string live = age < 2 ? "LIVE" : (age < 10 ? "quiet" : "gone");
                _lstSa.Items.Add(string.Format("SA {0,-3} {1,-24} {2,-5} x{3}  {4:0.0}s {5}",
                    r.Sa, Names.Sa(r.Sa), live, r.Count, age, r.Claimed ? "CLAIM" : ""));
            }
            _lstSa.EndUpdate();
        }

        void TickReconnect()
        {
            if (_chkAutoRe == null || !_chkAutoRe.Checked) return;
            if (!_wantConnected) return;
            bool dead = !_rp.IsConnected || (DateTime.UtcNow - _lastGood).TotalSeconds > 6;
            if (!dead) return;
            if ((DateTime.UtcNow - _lastRe).TotalSeconds < 5) return;
            _lastRe = DateTime.UtcNow;
            Log("Auto-reconnect…");
            try
            {
                _rp.Disconnect();
                if (_rp.Connect())
                {
                    _lastGood = DateTime.UtcNow;
                    Log("Reconnected device " + _rp.DeviceId + " " + _rp.Protocol);
                    RequestCodes(false);
                }
                else Log("Auto-reconnect failed: " + _rp.LastError);
            }
            catch (Exception ex) { Log("Auto-reconnect error: " + ex.Message); }
        }

        Snap TakeSnap(string label)
        {
            var s = new Snap
            {
                Time = DateTime.Now,
                Label = label,
                Rpm = _rpmVal,
                Red = _red,
                Amber = _amber,
                Def = _defTxt,
                Coolant = _coolant,
                Oil = _oil,
                Batt = _batt,
                Fuel = _fuelRate
            };
            foreach (Dtc d in _active) s.Active.Add(d.ToString());
            foreach (Dtc d in _prev) s.Prev.Add(d.ToString());
            return s;
        }

        void ShowDiff()
        {
            if (_diffBox == null) return;
            _diffBox.Text = SessionIo.Diff(_snapA, _snapB);
        }

        void Mark(string tag)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + tag +
                "  RPM=" + (_rpmVal < 0 ? "—" : _rpmVal.ToString("0")) +
                "  Red=" + (_red ? "ON" : "off") +
                "  Amber=" + (_amber ? "ON" : "off") +
                "  DEF=" + _defTxt;
            _markers.Add(line);
            Log(line);
        }

        void TakeShot()
        {
            Control c = _tabs != null ? (Control)_tabs : this;
            string path = SessionIo.Screenshot(c);
            Log("Screenshot " + path);
        }

        void SaveSession()
        {
            var sb = new StringBuilder();
            sb.AppendLine("J1939Reader session  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Device " + _rp.DeviceId + "  " + _rp.Protocol + "  connected=" + _rp.IsConnected);
            sb.AppendLine("RPM " + (_rpmVal < 0 ? "—" : _rpmVal.ToString("0"))
                + "  Red Stop " + (_red ? "ON" : "off")
                + "  Amber " + (_amber ? "ON" : "off"));
            sb.AppendLine("DEF " + _defTxt);
            sb.AppendLine("Coolant " + _coolant + "  Oil " + _oil + "  Battery " + _batt + "  Fuel " + _fuelRate);
            sb.AppendLine("VIN " + _vin + "  SW " + _sw);
            sb.AppendLine();
            sb.AppendLine("Active DTCs:");
            if (_active.Count == 0) sb.AppendLine("  (none)");
            else foreach (Dtc d in _active) sb.AppendLine("  " + d);
            sb.AppendLine("Previous DTCs:");
            if (_prev.Count == 0) sb.AppendLine("  (none)");
            else foreach (Dtc d in _prev) sb.AppendLine("  " + d);
            sb.AppendLine();
            sb.AppendLine("Markers:");
            if (_markers.Count == 0) sb.AppendLine("  (none)");
            else foreach (string m in _markers) sb.AppendLine("  " + m);
            sb.AppendLine();
            sb.AppendLine("Modules:");
            foreach (SaRow r in _sas.Values)
                sb.AppendLine("  SA " + r.Sa + " " + Names.Sa(r.Sa) + " x" + r.Count);
            var csv = new StringBuilder();
            csv.AppendLine("time,rpm,red,amber,def,coolant,oil,battery,fuel,active_count");
            csv.AppendLine(string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}",
                DateTime.Now.ToString("o"),
                _rpmVal, _red, _amber,
                Quote(_defTxt), Quote(_coolant), Quote(_oil), Quote(_batt), Quote(_fuelRate),
                _active.Count));
            foreach (Dtc d in _active)
                csv.AppendLine("dtc,active," + d.Spn + "," + d.Fmi + "," + d.Occ + "," + Quote(d.Name));
            if (!string.IsNullOrWhiteSpace(JobTag))
                sb.Insert(0, "Job  " + JobTag + Environment.NewLine);
            string path = SessionIo.SaveTextCsv(sb.ToString(), csv.ToString(), JobTag);
            Log("Saved " + path);
        }

        static string Quote(string s)
        {
            if (s == null) s = "";
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
