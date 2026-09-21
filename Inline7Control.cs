using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using TechBench;

namespace J1939Reader
{
    /// <summary>
    /// The INLINE 7 tab. Holds no adapter state of its own: it drives a BusWorker on a background
    /// thread and repaints from snapshots, so nothing here can block on the datalink.
    ///
    /// Layout is built from docked and flow containers rather than absolute pixel coordinates so it
    /// survives DPI scaling and window resizing.
    /// </summary>
    internal sealed class Inline7Control : UserControl
    {
        readonly BusWorker _bus = new BusWorker();
        readonly Timer _ui = new Timer();
        readonly List<string> _markers = new List<string>();
        readonly List<Rp1210Api> _adapters;

        ComboBox _cboAdapter, _cboModule, _cboChannel;
        Button _btnConnect, _btnDisc, _btnRefresh, _btnClearPrev, _btnClearActive, _btnPostRepair, _btnPing;
        Label _rpm, _lamps, _def;
        TextBox _status, _mon, _aft;
        ListBox _lstActive, _lstPrev, _lstLive, _lstPgn, _lstSa, _lstTimeline, _lstGuidanz;
        TextBox _log, _codeHelp, _liveHelp, _idBox, _diffBox, _guidanzHelp, _histBox, _trendInfo;
        CheckBox _chkSafe, _chkAuto, _chkTsc800, _chkTsc1200, _chkQuietBus, _chkAutoRe;
        ToolTip _tip;
        TabControl _tabs;
        TabPage _tabTrend, _tabTimeline, _tabHistory;
        TrendChart _chart;
        Button _btnKbLookup;

        List<SwitchRow> _liveRows = new List<SwitchRow>();
        List<SwitchRow> _guidanzRows = new List<SwitchRow>();
        List<DtcEvent> _timelineRows = new List<DtcEvent>();
        BusSnapshot _snap = new BusSnapshot();
        Snap _snapA, _snapB;
        string _sigActive = "", _sigPrev = "", _sigModules = "", _sigTimeline = "", _sigLive = "";
        DateTime _lastBusUi = DateTime.MinValue;
        string _shownResetReport = "";
        bool _holdResetReport;
        int _tscRpm;
        History _history;
        string _historyJob = "";
        string _hudStatus = "";
        string _hudAft = "";
        string _hudMon = "";

        /// <summary>Set by the shell so a DTC can be handed to the knowledge-base search.</summary>
        public Action<string> OpenInSearch;

        /// <summary>Set by the shell; returns KB text for an SPN/FMI, or null.</summary>
        public Func<int, int, string> LookupCode;

        /// <summary>Set by the shell after a session file is written, so shop history can sync.</summary>
        public Action<string> SessionSaved;

        public string JobTag { get; set; }

        /// <summary>
        /// True while the adapter is connecting, connected, or in the middle of a reset.
        /// Updates must not swap TechBench.exe in that state.
        /// </summary>
        public bool SessionLive
        {
            get { return _bus.WantConnected || _snap.Connected || _snap.Busy; }
        }

        /// <summary>WO tab attaches the PNG after Shot.</summary>
        public Action<string> AfterScreenshot;

        /// <summary>WO tab attaches the diagnostic report text after Report.</summary>
        public Action<string> AfterReport;

        public string LiveReportText()
        {
            return JobReport.Text(BuildReportData());
        }

        public Inline7Control()
        {
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9.5f);
            _adapters = BusWorker.Adapters();

            _tip = new ToolTip
            {
                AutoPopDelay = 32000,
                InitialDelay = 250,
                ReshowDelay = 200,
                ShowAlways = true,
                ToolTipTitle = "Diagnostic toggle",
                IsBalloon = false
            };

            _tabs = new TabControl { Dock = DockStyle.Fill, Multiline = true };
            _tabs.TabPages.Add(BuildCodesTab());
            _tabs.TabPages.Add(BuildAdvancedTab());
            _tabs.TabPages.Add(BuildBusTab());
            _tabTrend = BuildTrendTab();
            _tabs.TabPages.Add(_tabTrend);
            _tabTimeline = BuildTimelineTab();
            _tabs.TabPages.Add(_tabTimeline);
            _tabHistory = BuildHistoryTab();
            _tabs.TabPages.Add(_tabHistory);
            _tabs.TabPages.Add(BuildGuidanzTab());

            // Docked children are laid out last-added-first, so the fill control goes in first.
            Controls.Add(_tabs);
            Controls.Add(BuildHud());
            Controls.Add(BuildToolbar());

            _ui.Interval = 150;
            _ui.Tick += OnUiTick;
            _bus.Start();
            _ui.Start();

            RefreshLiveList(_snap);
            SetConnected(false);

            Disposed += delegate
            {
                _ui.Stop();
                _ui.Dispose();
                _bus.Dispose();
                _tip.Dispose();
            };
        }

        // ---------- layout helpers ----------

        static Button Btn(string text, EventHandler onClick)
        {
            var b = new Button { Text = text, AutoSize = true, Margin = new Padding(0, 0, 6, 0), MinimumSize = new Size(0, 28) };
            if (onClick != null) b.Click += onClick;
            return b;
        }

        static Label Lbl(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(0, 7, 4, 0) };
        }

        static TextBox HelpBox()
        {
            return new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                MaxLength = 0,
                Font = new Font("Segoe UI", 9f),
                Dock = DockStyle.Fill
            };
        }

        static ListBox List()
        {
            return new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        }

        static Panel Titled(string title, Control body)
        {
            var p = new Panel { Dock = DockStyle.Fill };
            p.Controls.Add(body);
            body.Dock = DockStyle.Fill;
            TextBox cap = UiLayout.WrapText(title);
            cap.Dock = DockStyle.Top;
            cap.Padding = new Padding(0, 0, 0, 2);
            p.Controls.Add(cap);
            return p;
        }

        static SplitContainer Split(Orientation orientation, int distance, Control a, Control b)
        {
            return UiLayout.Split(orientation, distance, 80, 80, a, b);
        }

        static SplitContainer Split(Orientation orientation, int distance, int min1, int min2, Control a, Control b)
        {
            return UiLayout.Split(orientation, distance, min1, min2, a, b);
        }

        Control BuildToolbar()
        {
            var flow = UiLayout.WrapBar(new Padding(8, 4, 8, 2));

            flow.Controls.Add(Lbl("Adapter"));
            _cboAdapter = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 280,
                DropDownWidth = 520,
                Margin = new Padding(0, 3, 10, 0)
            };
            foreach (Rp1210Api a in _adapters) _cboAdapter.Items.Add(a);
            if (_adapters.Count == 0) _cboAdapter.Items.Add("(no RP1210 adapter installed)");
            _cboAdapter.SelectedIndex = 0;
            _tip.SetToolTip(_cboAdapter,
                "RP1210 adapters this PC has installed, read from RP121032.INI. Cummins INLINE 7 is listed first when present.");
            flow.Controls.Add(_cboAdapter);

            _btnConnect = Btn("Connect", delegate { DoConnect(); });
            _btnDisc = Btn("Disconnect", delegate { DoDisconnect(); });
            _btnRefresh = Btn("Refresh codes", delegate { _bus.Enqueue(new BusCommand(BusCmdKind.RequestCodes)); });
            _btnClearPrev = Btn("Clear previous", delegate { DoClearPrevious(); });
            _btnClearActive = Btn("Reset all codes", delegate { DoResetAll(); });
            _btnPostRepair = Btn("Clear codes after repair", delegate { DoClearAfterRepair(); });
            _tip.SetToolTip(_btnPostRepair,
                "DM11 + DM3 to the engine and compressor SA 48, then re-request DM1 and the DEF/SCR tank message. Not a DEF delete.");
            flow.Controls.Add(_btnConnect);
            flow.Controls.Add(_btnDisc);
            flow.Controls.Add(_btnRefresh);
            flow.Controls.Add(_btnClearPrev);
            flow.Controls.Add(_btnClearActive);
            flow.Controls.Add(_btnPostRepair);

            flow.Controls.Add(Btn("Save", delegate { SaveSession(); }));
            flow.Controls.Add(Btn("Report", delegate { PrintReport(true); }));
            flow.Controls.Add(Btn("Shot", delegate { TakeShot(); }));
            flow.Controls.Add(Btn("KEY-ON", delegate { Mark("KEY-ON"); }));
            flow.Controls.Add(Btn("CRANK", delegate { Mark("CRANK"); }));
            flow.Controls.Add(Btn("RELEASE", delegate { Mark("RELEASE"); }));

            flow.Controls.Add(Lbl("Module"));
            _cboModule = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 190,
                Margin = new Padding(0, 3, 0, 0)
            };
            _cboModule.Items.Add("Engine (auto)");
            _cboModule.SelectedIndex = 0;
            _cboModule.SelectedIndexChanged += delegate { ApplyModuleSelection(); };
            _tip.SetToolTip(_cboModule,
                "Whose DM1/DM2 the code lists show. On a portable compressor the controller at SA 48 has its own faults.");
            flow.Controls.Add(_cboModule);

            return flow;
        }

        Control BuildHud()
        {
            var panel = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 0, 8, 2) };

            var line = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true
            };
            _rpm = new Label
            {
                Text = "RPM: —",
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(0, 2, 24, 2)
            };
            _lamps = new Label { Text = "Lamps: —", AutoSize = true, Margin = new Padding(0, 2, 24, 2) };
            _def = new Label { Text = "DEF: —", AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
            line.Controls.Add(_rpm);
            line.Controls.Add(_lamps);
            line.Controls.Add(_def);

            _aft = UiLayout.WrapText(new AftState().Text());
            _aft.Dock = DockStyle.Top;
            _aft.ForeColor = Color.FromArgb(20, 28, 38);
            _aft.Padding = new Padding(0, 2, 0, 4);

            _status = UiLayout.WrapText("Disconnected — close Guidanz / J1939 tool / USB-Link Explorer before Connect.");
            _status.Dock = DockStyle.Top;
            _status.Padding = new Padding(0, 2, 0, 2);

            panel.Controls.Add(_status);
            panel.Controls.Add(_aft);
            panel.Controls.Add(line);
            return panel;
        }

        // ---------- tabs ----------

        TabPage BuildCodesTab()
        {
            var p = new TabPage("Codes") { Padding = new Padding(8) };

            _lstActive = List();
            _lstPrev = List();
            _lstActive.SelectedIndexChanged += delegate { ShowCode(_lstActive); };
            _lstPrev.SelectedIndexChanged += delegate { ShowCode(_lstPrev); };
            SplitContainer lists = Split(Orientation.Vertical, 430, 160, 160,
                Titled("Active DTCs (DM1) — click a code for explanation", _lstActive),
                Titled("Previously active (DM2) — click a code", _lstPrev));

            _codeHelp = HelpBox();
            _codeHelp.Text = "Connect, then click a code.\r\n\r\n"
                + "FMI 9 = the ECM is not receiving that sensor, not that the number is slightly wrong.\r\n"
                + "This app will not disable DEF/SCR. That would wreck the aftertreatment.";

            var helpHost = new Panel { Dock = DockStyle.Fill };
            helpHost.Controls.Add(_codeHelp);
            var helpBar = UiLayout.WrapBar(new Padding(0, 0, 0, 4));
            helpBar.Controls.Add(Lbl("What this code means"));
            _btnKbLookup = Btn("Look up in knowledge base", delegate { LookupSelectedInKb(); });
            _btnKbLookup.Enabled = false;
            helpBar.Controls.Add(_btnKbLookup);

            _log = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                // Default MaxLength is 32767, after which AppendText silently drops everything.
                MaxLength = 0,
                Font = new Font("Consolas", 9f)
            };

            SplitContainer lower = Split(Orientation.Vertical, 560, 200, 160, helpHost, Titled("Log", _log));
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                AutoScroll = true
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            helpBar.Dock = DockStyle.Fill;
            grid.Controls.Add(helpBar, 0, 0);
            grid.Controls.Add(Split(Orientation.Horizontal, 200, 80, 140, lists, lower), 0, 1);
            p.Controls.Add(grid);
            return p;
        }

        TabPage BuildAdvancedTab()
        {
            var p = new TabPage("Advanced diagnostics") { Padding = new Padding(8) };

            var warn = UiLayout.WrapText(
                "These are standard J1939 diagnostic controls the ECM already understands. " +
                "They isolate harness vs computer. They do not add new Cummins calibration bits, " +
                "and they do not disable DEF, SCR, Red Stop, or engine protection.");
            warn.Dock = DockStyle.Top;
            warn.Padding = new Padding(0, 0, 0, 4);

            _lstLive = List();
            _liveHelp = HelpBox();
            _liveHelp.Text = "Click a live state.";
            _lstLive.SelectedIndexChanged += delegate
            {
                int i = _lstLive.SelectedIndex;
                if (i >= 0 && i < _liveRows.Count)
                    _liveHelp.Text = _liveRows[i].Name + "\r\nState: " + _liveRows[i].State + "\r\n\r\n" + _liveRows[i].Detail;
            };
            SplitContainer top = Split(Orientation.Vertical, 470, 180, 140,
                Titled("Live ECM states (status — click for explanation)", _lstLive),
                Titled("Explanation", _liveHelp));

            _mon = UiLayout.WrapText("Coolant: —    Oil: —    Battery: —    Fuel rate: —");
            _mon.Dock = DockStyle.Top;
            _mon.Padding = new Padding(0, 2, 0, 2);

            var g = new GroupBox
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Text = "Diagnostic toggles — hover a switch for the full description"
            };
            var toggles = UiLayout.WrapBar(new Padding(8, 4, 8, 6));
            toggles.Dock = DockStyle.Fill;
            _chkSafe = MkToggle(toggles,
                "Safety: engine already running, compressor unloaded, area clear",
                "Must be ON before a speed-request toggle will send TSC1. The engine will not start from these switches. TSC1 cannot override Red Stop or DEF inducement.");
            _chkAuto = MkToggle(toggles,
                "Auto-refresh codes (every 2 s)",
                "ON: keep asking the ECM for DM1/DM2 and DEF tank so the lists stay live.\nOFF: lists only update when you click Refresh or Reset.\nThis does not change any ECM function.");
            _chkAuto.Checked = true;
            _chkAuto.CheckedChanged += delegate { _bus.AutoRefresh = _chkAuto.Checked; };
            _chkTsc800 = MkToggle(toggles,
                "Hold idle 800 RPM (TSC1)",
                "ON: send J1939 TSC1 speed-control 800 RPM every 40 ms while the engine is already running.\nUse: confirm the ECM accepts a diagnostic throttle (RPM should sit near 800).\nOFF: release control back to the ECM / compressor controller.\nWill not crank, will not bypass inducement, will not disable DEF. Requires the safety toggle.");
            _chkTsc1200 = MkToggle(toggles,
                "Hold 1200 RPM (TSC1)",
                "ON: same as idle hold but 1200 RPM. Mutually exclusive with 800.\nUse: see if the ECM will take a mid-speed diagnostic request after it has fired.\nOFF: release. Requires the safety toggle. Not an engine-protection or DEF bypass.");
            _chkQuietBus = MkToggle(toggles,
                "Quiet bus (DM13 stop J1939 broadcast)",
                "ON: J1939-73 DM13 Stop Broadcast — asks modules to stop periodic traffic so you can see what still talks.\nOFF: DM13 Start Broadcast — normal traffic resumes.\nUse: isolate a noisy/missing module. Do not leave ON. This is not a sensor disable and not a DEF bypass.");
            _chkTsc800.CheckedChanged += delegate
            {
                if (_chkTsc800.Checked) { _chkTsc1200.Checked = false; SetTsc(800); }
                else if (!_chkTsc1200.Checked) SetTsc(0);
            };
            _chkTsc1200.CheckedChanged += delegate
            {
                if (_chkTsc1200.Checked) { _chkTsc800.Checked = false; SetTsc(1200); }
                else if (!_chkTsc800.Checked) SetTsc(0);
            };
            _chkQuietBus.CheckedChanged += delegate
            {
                if (!_snap.Connected) return;
                _bus.Enqueue(new BusCommand(BusCmdKind.Dm13, _chkQuietBus.Checked));
            };

            _btnPing = Btn("Ping ECM identity", delegate { PingEcm(); });
            _btnPing.Margin = new Padding(8, 2, 0, 2);
            _tip.SetToolTip(_btnPing,
                "Request VIN, software ID, component ID, hours, and DM1. If RPM/codes are already live but VIN never appears, this industrial ECM may not publish VIN — the computer is still alive.");
            toggles.Controls.Add(_btnPing);
            g.Controls.Add(toggles);
            EventHandler fitToggles = delegate
            {
                int inner = Math.Max(1, g.ClientSize.Width);
                Size pref = toggles.GetPreferredSize(new Size(inner, 0));
                int h = pref.Height + 22;
                if (h < 48) h = 48;
                if (g.Height != h) g.Height = h;
            };
            g.Layout += delegate { fitToggles(null, EventArgs.Empty); };
            g.SizeChanged += delegate { fitToggles(null, EventArgs.Empty); };
            p.SizeChanged += delegate { fitToggles(null, EventArgs.Empty); };

            _idBox = HelpBox();
            _idBox.Text = CannotDoText();

            var host = new Panel { Dock = DockStyle.Fill };
            host.Controls.Add(Titled("ECM identity / remaining codes after reset", _idBox));

            // Table rows, not a pile of Dock.Top chrome: on a 900×560 shop laptop the
            // GroupBox used to paint Ping below the page (unreadable / unclickable).
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                AutoScroll = true
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            warn.Dock = DockStyle.Fill;
            _mon.Dock = DockStyle.Fill;
            g.Dock = DockStyle.Fill;
            grid.Controls.Add(warn, 0, 0);
            grid.Controls.Add(_mon, 0, 1);
            grid.Controls.Add(g, 0, 2);
            grid.Controls.Add(Split(Orientation.Horizontal, 160, 80, 80, top, host), 0, 3);
            p.Controls.Add(grid);
            return p;
        }

        CheckBox MkToggle(Control parent, string label, string hover)
        {
            var c = new CheckBox { Text = label, AutoSize = true, Margin = new Padding(0, 2, 16, 2) };
            parent.Controls.Add(c);
            _tip.SetToolTip(c, hover);
            c.MouseEnter += delegate
            {
                if (_liveHelp != null) _liveHelp.Text = label + "\r\n\r\n" + hover;
            };
            return c;
        }

        TabPage BuildBusTab()
        {
            var p = new TabPage("Bus / snapshots") { Padding = new Padding(8) };

            _lstPgn = List();
            _lstSa = List();
            SplitContainer lists = Split(Orientation.Vertical, 470, 180, 160,
                Titled("PGN traffic (who is talking)", _lstPgn),
                Titled("Address claim / modules", _lstSa));

            var bar = UiLayout.WrapBar(new Padding(0, 4, 0, 4));
            _chkAutoRe = new CheckBox
            {
                Text = "Auto-reconnect if the adapter drops",
                AutoSize = true,
                Checked = true,
                Margin = new Padding(0, 5, 16, 0)
            };
            _chkAutoRe.CheckedChanged += delegate { _bus.AutoReconnect = _chkAutoRe.Checked; };
            bar.Controls.Add(_chkAutoRe);
            bar.Controls.Add(Btn("Snapshot A", delegate { _snapA = TakeSnap("A before"); Log("Snapshot A saved"); ShowDiff(); }));
            bar.Controls.Add(Btn("Snapshot B", delegate { _snapB = TakeSnap("B after"); Log("Snapshot B saved"); ShowDiff(); }));
            bar.Controls.Add(Btn("Diff", delegate { ShowDiff(); }));
            bar.Controls.Add(Btn("Clear stats", delegate { _bus.Enqueue(new BusCommand(BusCmdKind.ClearBusStats)); }));

            _diffBox = HelpBox();
            _diffBox.Text = "Snapshot A before a repair, Snapshot B after, then Diff.\r\n"
                + "PGN list updates live. SA 0 = ECM, 48 = compressor controller, 128 = Grayhill. "
                + "If the tank header talks you will see DEF PGN FE56 from the engine.";

            var lower = new Panel { Dock = DockStyle.Fill };
            lower.Controls.Add(_diffBox);
            lower.Controls.Add(bar);

            p.Controls.Add(Split(Orientation.Horizontal, 220, 80, 110, lists, lower));
            return p;
        }

        TabPage BuildTrendTab()
        {
            var p = new TabPage("Trend") { Padding = new Padding(8) };

            var bar = UiLayout.WrapBar(new Padding(0, 0, 0, 6));
            bar.Controls.Add(Lbl("Channel"));
            _cboChannel = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 150,
                Margin = new Padding(0, 3, 12, 0)
            };
            foreach (TrendChannel ch in new[] { TrendChannel.Rpm, TrendChannel.Coolant, TrendChannel.Oil, TrendChannel.Battery, TrendChannel.Fuel })
                _cboChannel.Items.Add(TrendLog.Label(ch));
            _cboChannel.SelectedIndex = 0;
            _cboChannel.SelectedIndexChanged += delegate
            {
                _chart.Channel = (TrendChannel)_cboChannel.SelectedIndex;
            };
            bar.Controls.Add(_cboChannel);
            bar.Controls.Add(Btn("Export CSV", delegate { ExportTrend(); }));
            bar.Controls.Add(Btn("Clear history", delegate { _bus.Enqueue(new BusCommand(BusCmdKind.ClearHistory)); }));

            _chart = new TrendChart { Dock = DockStyle.Fill };
            _trendInfo = HelpBox();
            _trendInfo.Height = 76;
            _trendInfo.Dock = DockStyle.Bottom;
            _trendInfo.Text = "One sample a second, last 30 minutes. Shaded bands are where the red (pink) "
                + "or amber (yellow) lamp was on — so you can see what a reading did before the lamp came on.";

            p.Controls.Add(_chart);
            p.Controls.Add(_trendInfo);
            p.Controls.Add(bar);
            return p;
        }

        TabPage BuildTimelineTab()
        {
            var p = new TabPage("Timeline") { Padding = new Padding(8) };
            var bar = UiLayout.WrapBar(new Padding(0, 0, 0, 6));
            bar.Controls.Add(Lbl("Every code that came and went on this hookup, newest first."));
            bar.Controls.Add(Btn("Export CSV", delegate { ExportTimeline(); }));

            _lstTimeline = List();
            _lstTimeline.Font = new Font("Consolas", 9f);
            _lstTimeline.DoubleClick += delegate { LookupTimelineInKb(); };

            p.Controls.Add(_lstTimeline);
            p.Controls.Add(bar);
            return p;
        }

        TabPage BuildHistoryTab()
        {
            var p = new TabPage("Unit history") { Padding = new Padding(8) };
            var bar = UiLayout.WrapBar(new Padding(0, 0, 0, 6));
            bar.Controls.Add(Lbl("Faults this model/serial has shown in saved sessions."));
            bar.Controls.Add(Btn("Rescan", delegate { _history = null; _historyJob = ""; RefreshHistory(true); }));

            _histBox = HelpBox();
            _histBox.Font = new Font("Consolas", 9f);
            p.Controls.Add(_histBox);
            p.Controls.Add(bar);
            return p;
        }

        TabPage BuildGuidanzTab()
        {
            var p = new TabPage("Guidanz reference") { Padding = new Padding(8) };
            var note = UiLayout.WrapText(
                "Cummins Features & Parameters this app does NOT write — reference only, so you know "
                + "what lives in Guidanz and what does not belong in a J1939 tool at all.");
            note.Dock = DockStyle.Top;
            note.Padding = new Padding(0, 0, 0, 6);
            _guidanzRows = FeatureBook.GuidanzFeatures();
            _lstGuidanz = List();
            foreach (SwitchRow r in _guidanzRows) _lstGuidanz.Items.Add(r.ToString());
            _guidanzHelp = HelpBox();
            _guidanzHelp.Text = "Click an item.";
            _lstGuidanz.SelectedIndexChanged += delegate
            {
                int i = _lstGuidanz.SelectedIndex;
                if (i >= 0 && i < _guidanzRows.Count)
                    _guidanzHelp.Text = _guidanzRows[i].Name + "\r\n" + _guidanzRows[i].State
                        + "\r\n\r\n" + _guidanzRows[i].Detail;
            };
            p.Controls.Add(Split(Orientation.Vertical, 430,
                Titled("Feature", _lstGuidanz),
                Titled("What it does / why it is not here", _guidanzHelp)));
            p.Controls.Add(note);
            return p;
        }

        static string CannotDoText()
        {
            return
                "Ping ECM to fill VIN / software here.\r\n\r\n" +
                "What this app can switch (standard J1939):\r\n" +
                "• TSC1 speed request / release — tests whether the ECM accepts a diagnostic throttle\r\n" +
                "• Identity request — VIN, software; no answer after a live DM1 usually means power/harness, not 'needs a new switch'\r\n" +
                "• Code clear (DM3/DM11) — engine + compressor controller SA 48, after the fault condition is gone\r\n\r\n" +
                "What cannot be created from this adapter:\r\n" +
                "• New calibration switches inside the Cummins (those only exist if Cummins put them in the file)\r\n" +
                "• Cylinder cutout, EGR/VGT/DEF pump actuator tests — proprietary Guidanz\r\n" +
                "• Disable DEF, SCR, Red Stop, or engine protection — not diagnostic isolation, and can wreck the engine\r\n\r\n" +
                "To decide 'replace the ECM': ping identity + watch DM1. If the ECM broadcasts RPM/lamps/codes but DEF tank PGNs stay n/a, the computer is alive and the tank header/harness is not.";
        }

        // ---------- actions ----------

        void Log(string s)
        {
            if (_log == null) return;
            if (_log.TextLength > 80000)
            {
                string[] lines = _log.Lines;
                int drop = lines.Length / 2;
                var keep = new string[lines.Length - drop];
                Array.Copy(lines, drop, keep, 0, keep.Length);
                _log.Lines = keep;
            }
            _log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + Environment.NewLine);
        }

        Rp1210Api SelectedAdapter()
        {
            return _cboAdapter == null ? null : _cboAdapter.SelectedItem as Rp1210Api;
        }

        void SetConnected(bool on)
        {
            _btnConnect.Enabled = !on;
            _btnDisc.Enabled = on;
            _btnRefresh.Enabled = on;
            _btnClearPrev.Enabled = on;
            _btnClearActive.Enabled = on;
            if (_btnPostRepair != null) _btnPostRepair.Enabled = on;
            _btnPing.Enabled = on;
            _cboAdapter.Enabled = !on;
            _chkTsc800.Enabled = on;
            _chkTsc1200.Enabled = on;
            _chkQuietBus.Enabled = on;
        }

        void DoConnect()
        {
            Rp1210Api api = SelectedAdapter();
            if (api == null)
            {
                MessageBox.Show(this,
                    "No RP1210 adapter is installed on this PC.\n\n" +
                    "Install the Cummins INLINE 7 (CIL7) drivers, then restart Tech Bench.",
                    "Connect", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!Rp1210.HostIs32Bit)
            {
                MessageBox.Show(this,
                    "This build is 64-bit and RP1210 drivers are 32-bit only.\n\nRebuild with build.bat (/platform:x86).",
                    "Connect", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Log("Connecting to " + api + " … close Guidanz / J1939 tool / USB-Link Explorer first.");
            if (AdapterHolderRunning())
                Log("NOTE: USB-Link Explorer or Guidanz appears to be running and may be holding the adapter.");
            _bus.RequestConnect(api);
        }

        static bool AdapterHolderRunning()
        {
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcesses())
                    if (p.ProcessName.IndexOf("USBLink", StringComparison.OrdinalIgnoreCase) >= 0
                        || p.ProcessName.IndexOf("Guidanz", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
            }
            catch { }
            return false;
        }

        void DoDisconnect()
        {
            _tscRpm = 0;
            _bus.SetTsc1(0);
            _bus.RequestDisconnect();
            if (_chkTsc800 != null) _chkTsc800.Checked = false;
            if (_chkTsc1200 != null) _chkTsc1200.Checked = false;
        }

        void DoClearPrevious()
        {
            var r = MessageBox.Show(this,
                "Clear PREVIOUSLY ACTIVE codes only (DM3) on the engine and compressor controller (SA 48)?\n\nActive faults that are still happening will stay.",
                "Clear previous", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            _bus.Enqueue(new BusCommand(BusCmdKind.ClearPrevious));
        }

        void DoResetAll()
        {
            var r = MessageBox.Show(this,
                "Reset all codes on the engine and compressor controller (SA 48)?\n\n" +
                "Sends J1939 DM11 + DM3 (3 rounds) to SA 0, SA 48, and broadcast, then re-requests DM1/DM2. UDS 0x14 is also tried if ISO15765 opens.\n\n" +
                "Red Stop and Amber are not separate codes. They are lamp bits on DM1. They go OFF by themselves when the DTCs that set them are gone.\n" +
                "There is no legal 'Red Stop off' / 'Amber off' switch — forcing the lamps off while 5246 / FMI 9 are still active would hide a no-fuel command, not diagnose it.\n\n" +
                "If FMI 9 or inducement is still true, lamps and those SPNs will come right back.\n\nContinue?",
                "Reset all codes",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            Log("Reset all codes queued — runs on the bus thread, window stays live.");
            _bus.Enqueue(new BusCommand(BusCmdKind.ResetAll));
        }

        void DoClearAfterRepair()
        {
            var r = MessageBox.Show(this,
                "Clear codes after repair?\n\n" +
                "Sends the existing J1939 DM11 (clear active) and DM3 (clear previously active) to the engine and compressor controller (SA 48), then asks again for DM1 and the DEF/SCR tank message (PGN FE56: level 1761, temp 3031, inducement 5246, low-level 5245).\n\n" +
                "This is a code clear. It does not reset DEF dosing, disable SCR, turn sensors off, or defeat lamps. There is no public SAE routine that resets DEF dosing without disabling SCR, so this button does not send one.\n\n" +
                "If the fault is still true, those codes come back on the next DM1.\n\nContinue?",
                "Clear codes after repair",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            Log("Clear codes after repair queued — DM11/DM3, then re-read DM1 and DEF/SCR. Not a DEF delete.");
            _bus.Enqueue(new BusCommand(BusCmdKind.ResetAll));
        }

        void SetTsc(int rpm)
        {
            if (rpm > 0)
            {
                if (!_snap.Connected)
                {
                    if (_chkTsc800 != null) _chkTsc800.Checked = false;
                    if (_chkTsc1200 != null) _chkTsc1200.Checked = false;
                    return;
                }
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
            _bus.SetTsc1(rpm);
        }

        void PingEcm()
        {
            _holdResetReport = false;
            _bus.Enqueue(new BusCommand(BusCmdKind.PingIdentity));
            _idBox.Text = "Waiting for ECM identity…\r\n"
                + "If DM1 is already updating at the top but VIN never appears, the computer is alive — it just may not publish VIN on this industrial calibration.\r\n\r\n"
                + CannotDoText();
        }

        void ApplyModuleSelection()
        {
            int i = _cboModule.SelectedIndex;
            if (i <= 0) { _bus.ViewSa = -1; return; }
            string item = Convert.ToString(_cboModule.Items[i]);
            int sa;
            if (int.TryParse(item.Split(' ')[1], out sa)) _bus.ViewSa = sa;
        }

        Dtc SelectedDtc(ListBox box)
        {
            int i = box.SelectedIndex;
            List<Dtc> src = box == _lstActive ? _snap.Active : _snap.Prev;
            if (i < 0 || i >= src.Count) return null;
            return src[i];
        }

        void ShowCode(ListBox box)
        {
            Dtc d = SelectedDtc(box);
            if (d == null) { if (_btnKbLookup != null) _btnKbLookup.Enabled = false; return; }
            var sb = new StringBuilder();
            sb.Append(CodeBook.Explain(d.Spn, d.Fmi));
            sb.AppendLine("Occurrence count: " + d.Occ);
            // The knowledge base has richer, editable text on the same faults than the built-in book.
            if (LookupCode != null)
            {
                string kb = LookupCode(d.Spn, d.Fmi);
                if (!string.IsNullOrEmpty(kb))
                {
                    sb.AppendLine();
                    sb.AppendLine("── From the knowledge base ──");
                    sb.AppendLine(kb);
                }
            }
            _codeHelp.Text = sb.ToString();
            _codeHelp.SelectionStart = 0;
            _codeHelp.ScrollToCaret();
            if (_btnKbLookup != null) _btnKbLookup.Enabled = true;
        }

        void LookupSelectedInKb()
        {
            Dtc d = SelectedDtc(_lstActive) ?? SelectedDtc(_lstPrev);
            if (d == null || OpenInSearch == null) return;
            OpenInSearch(KbQuery(d.Spn, d.Fmi));
        }

        void LookupTimelineInKb()
        {
            int i = _lstTimeline.SelectedIndex;
            if (i < 0 || i >= _timelineRows.Count || OpenInSearch == null) return;
            DtcEvent e = _timelineRows[i];
            OpenInSearch(KbQuery(e.Spn, e.Fmi));
        }

        static string KbQuery(int spn, int fmi)
        {
            string name = Names.Spn(spn);
            return name.Length > 0 ? name : ("SPN " + spn + " FMI " + fmi);
        }

        // ---------- UI refresh ----------

        void OnUiTick(object sender, EventArgs e)
        {
            try { Refresh(_bus.Snapshot()); }
            catch (Exception ex) { Log("ui error: " + ex.Message); }
        }

        void Refresh(BusSnapshot s)
        {
            _snap = s;

            List<string> lines = _bus.DrainLog();
            if (lines != null) foreach (string l in lines) Log(l);

            SetConnected(s.Connected || s.Busy);

            string status;
            if (s.Busy) status = "Working: " + s.BusyWhat + "…";
            else if (s.Connected)
                status = "Connected  " + s.AdapterName + "  device " + s.DeviceId + "  " + s.Protocol
                       + "  " + s.Fps + " frames/s"
                       + (s.Stale ? "  —  NO FRAMES for " + s.QuietSeconds.ToString("0") + "s, readings below are stale" : "");
            else if (_bus.WantConnected) status = "Connecting… " + s.LastError;
            else status = "Disconnected — close Guidanz / J1939 tool / USB-Link Explorer before Connect.";
            if (_hudStatus != status)
            {
                _hudStatus = status;
                _status.Text = status;
            }

            _rpm.ForeColor = s.Stale ? Color.DimGray : Color.Black;
            _rpm.Text = double.IsNaN(s.Rpm) ? "RPM: —" : ("RPM: " + s.Rpm.ToString("0") + (s.Stale ? " (stale)" : ""));
            _lamps.Text = "Red Stop: " + OnOff(s.Red) + "   Amber: " + OnOff(s.Amber)
                + (s.Protect ? "   Protect: ON" : "") + (s.Mil ? "   MIL: ON" : "");
            _lamps.ForeColor = s.Red ? Color.Firebrick : Color.Black;
            _def.Text = "DEF: " + s.DefText;
            if (_aft != null && s.AftText != null && _hudAft != s.AftText)
            {
                _hudAft = s.AftText;
                _aft.Text = s.AftText;
            }
            string mon = "Coolant: " + BusMonitor.Fmt(s.CoolantC, " C", "0")
                + "    Oil: " + s.OilText
                + "    Battery: " + BusMonitor.Fmt(s.BatteryV, " V", "0.00")
                + "    Fuel rate: " + BusMonitor.Fmt(s.FuelLph, " L/h", "0.00")
                + (s.TscRpm > 0 ? "    TSC1 holding " + s.TscRpm + " RPM" : "    TSC1 released");
            if (_hudMon != mon)
            {
                _hudMon = mon;
                _mon.Text = mon;
            }

            FillList(_lstActive, s.Active, ref _sigActive);
            FillList(_lstPrev, s.Prev, ref _sigPrev);
            RefreshModuleList(s);
            RefreshLiveList(s);
            UpdateIdBox(s);
            // The bus lists carry live ages, so they have to be rebuilt rather than diffed. Once a
            // second is fast enough to read and slow enough not to fight the scrollbar.
            if ((DateTime.UtcNow - _lastBusUi).TotalMilliseconds > 800)
            {
                _lastBusUi = DateTime.UtcNow;
                Repopulate(_lstPgn, PgnItems(s));
                Repopulate(_lstSa, SaItems(s));
            }

            if (_tabs.SelectedTab == _tabTrend) _chart.SetData(_bus.TrendCopy());
            if (_tabs.SelectedTab == _tabTimeline) RefreshTimeline();
            if (_tabs.SelectedTab == _tabHistory) RefreshHistory(false);

            if (s.LastResetReport != _shownResetReport && s.LastResetReport.Length > 0)
            {
                _shownResetReport = s.LastResetReport;
                _holdResetReport = true;
                _idBox.Text = s.LastResetReport + "\r\n\r\n"
                    + "Red Stop: " + (s.Red ? "STILL ON" : "off") + "     Amber: " + (s.Amber ? "STILL ON" : "off") + "\r\n"
                    + "Lamps are not cleared separately. They track the active DTCs below.\r\n\r\n"
                    + LampHolders(s)
                    + "\r\nIf Red Stop stays on, SPN 5246 / 1569 are still latched or the tank header is still FMI 9. Fix that, reset again. Guidanz aftertreatment reset may still be required for 5246.";
            }
        }

        static string OnOff(bool v) { return v ? "ON" : "off"; }

        static string LampHolders(BusSnapshot s)
        {
            if (s.Active.Count == 0)
                return "No active DTCs parsed yet — wait a second for DM1, or the reset held and lamps should drop.\r\n";
            var sb = new StringBuilder();
            sb.AppendLine("Active DTCs holding the lamps:");
            foreach (Dtc d in s.Active)
            {
                string role = "";
                if (d.Spn == 5246 || d.Spn == 1569 || d.Spn == 5245) role = "  ← drives Red Stop / no-fuel";
                else if (d.Spn == 1761 || d.Spn == 3031 || d.Spn == 3364) role = "  ← tank not talking; feeds inducement";
                sb.AppendLine("  SPN " + d.Spn + " FMI " + d.Fmi + "  " + d.Name + role);
            }
            return sb.ToString();
        }

        /// <summary>
        /// DM1 lands every 2 s. Rebuilding the box every time drops the selection, which fires
        /// SelectedIndexChanged and scrolls the explanation the tech is mid-way through reading, so
        /// only touch it when the codes actually changed.
        /// </summary>
        static void FillList(ListBox box, List<Dtc> items, ref string sig)
        {
            string now = Signature(items);
            if (now == sig) return;
            sig = now;
            int keep = box.SelectedIndex;
            box.BeginUpdate();
            box.Items.Clear();
            if (items.Count == 0) box.Items.Add("(none)");
            else foreach (Dtc d in items) box.Items.Add(d.ToString());
            box.EndUpdate();
            if (keep >= 0 && keep < box.Items.Count) box.SelectedIndex = keep;
        }

        static string Signature(List<Dtc> items)
        {
            var sb = new StringBuilder();
            foreach (Dtc d in items)
                sb.Append(d.Spn).Append('/').Append(d.Fmi).Append('/').Append(d.Occ).Append(';');
            return sb.ToString();
        }

        void RefreshModuleList(BusSnapshot s)
        {
            var sb = new StringBuilder();
            foreach (int sa in s.FaultModules) sb.Append(sa).Append(';');
            string sig = sb.ToString();
            if (sig == _sigModules) return;
            _sigModules = sig;
            object keep = _cboModule.SelectedItem;
            _cboModule.BeginUpdate();
            _cboModule.Items.Clear();
            _cboModule.Items.Add("Engine (auto)");
            foreach (int sa in s.FaultModules)
                _cboModule.Items.Add("SA " + sa + " " + Names.Sa(sa));
            _cboModule.EndUpdate();
            int idx = keep == null ? 0 : _cboModule.Items.IndexOf(keep);
            _cboModule.SelectedIndex = idx >= 0 ? idx : 0;
        }

        void RefreshLiveList(BusSnapshot s)
        {
            if (_lstLive == null) return;
            _liveRows = FeatureBook.Live(s.Connected, s.Red, s.Amber, s.HasSpn5246, s.HasTankFmi9,
                s.Has1569, double.IsNaN(s.Rpm) ? -1 : s.Rpm, s.DefText);
            var items = new List<object>(_liveRows.Count);
            var sb = new StringBuilder();
            foreach (SwitchRow r in _liveRows)
            {
                string line = r.ToString();
                items.Add(line);
                sb.Append(line).Append('\n');
            }
            // Rebuilding fires SelectedIndexChanged and replaces the explanation being read, so only
            // do it when a state actually changed.
            string sig = sb.ToString();
            if (sig == _sigLive) return;
            _sigLive = sig;
            Repopulate(_lstLive, items);
        }

        List<object> PgnItems(BusSnapshot s)
        {
            var items = new List<object>();
            DateTime now = DateTime.UtcNow;
            int n = 0;
            foreach (PgnRow r in s.Pgns)
            {
                double age = (now - r.Last).TotalSeconds;
                items.Add(string.Format("{0:X4}  SA{1,-3} {2,-22} x{3,-5} {4:0.0}s ago",
                    r.Pgn, r.Sa, Names.Pgn(r.Pgn), r.Count, age));
                if (++n >= 200) break;
            }
            return items;
        }

        List<object> SaItems(BusSnapshot s)
        {
            var items = new List<object>();
            DateTime now = DateTime.UtcNow;
            foreach (SaRow r in s.Sas)
            {
                double age = (now - r.Last).TotalSeconds;
                string live = age < 2 ? "LIVE" : (age < 10 ? "quiet" : "gone");
                items.Add(string.Format("SA {0,-3} {1,-24} {2,-5} x{3}  {4:0.0}s {5}",
                    r.Sa, Names.Sa(r.Sa), live, r.Count, age, r.Claimed ? "CLAIM" : ""));
            }
            return items;
        }

        /// <summary>Refresh a live list without throwing away where the user had scrolled to.</summary>
        static void Repopulate(ListBox box, List<object> items)
        {
            if (box == null) return;
            int top = box.TopIndex;
            int sel = box.SelectedIndex;
            box.BeginUpdate();
            box.Items.Clear();
            foreach (object o in items) box.Items.Add(o);
            box.EndUpdate();
            if (sel >= 0 && sel < box.Items.Count) box.SelectedIndex = sel;
            if (top > 0 && top < box.Items.Count) box.TopIndex = top;
        }

        void RefreshTimeline()
        {
            _timelineRows = _bus.TimelineCopy(_bus.ViewSa);
            var sb = new StringBuilder();
            foreach (DtcEvent ev in _timelineRows)
                sb.Append(ev.Key).Append(ev.Active ? "+" : "-").Append(ev.Cycles).Append(';');
            string sig = sb.ToString();
            if (sig == _sigTimeline) return;
            _sigTimeline = sig;
            var items = new List<object>(_timelineRows.Count);
            foreach (DtcEvent ev in _timelineRows) items.Add(ev.ToString());
            if (items.Count == 0) items.Add("(nothing yet — connect and let DM1 arrive)");
            Repopulate(_lstTimeline, items);
        }

        void RefreshHistory(bool force)
        {
            string job = JobTag ?? "";
            if (!force && _history != null && job == _historyJob) return;
            _historyJob = job;
            if (_history == null) _history = History.Load();
            _histBox.Text = _history.Summary(job);
        }

        void UpdateIdBox(BusSnapshot s)
        {
            if (_idBox == null) return;
            if (_holdResetReport) return;   // reset report takes the box until the next ping
            string text =
                "VIN / NAME: " + Or(s.Vin) + "\r\n" +
                "Software: " + Or(s.Sw) + "\r\n" +
                "Component: " + Or(s.CompId) + "\r\n" +
                "Engine hours: " + Or(s.Hours) + "\r\n" +
                "Engine SA: " + s.EngineSa + "\r\n" +
                "DM1: " + (s.Active.Count == 0 ? "no active codes parsed" : (s.Active.Count + " active")) + "\r\n" +
                "RPM broadcast: " + (double.IsNaN(s.Rpm) ? "none" : s.Rpm.ToString("0")) + "\r\n\r\n" +
                CannotDoText();
            if (_idBox.Text != text) _idBox.Text = text;
        }

        static string Or(string s)
        {
            return string.IsNullOrEmpty(s) ? "(not published yet)" : s;
        }

        // ---------- snapshots / export ----------

        Snap TakeSnap(string label)
        {
            BusSnapshot s = _snap;
            var snap = new Snap
            {
                Time = DateTime.Now,
                Label = label,
                Rpm = double.IsNaN(s.Rpm) ? -1 : s.Rpm,
                Red = s.Red,
                Amber = s.Amber,
                Def = s.DefText,
                Coolant = BusMonitor.Fmt(s.CoolantC, " C", "0"),
                Oil = s.OilText,
                Batt = BusMonitor.Fmt(s.BatteryV, " V", "0.00"),
                Fuel = BusMonitor.Fmt(s.FuelLph, " L/h", "0.00")
            };
            foreach (Dtc d in s.Active) snap.Active.Add(d.ToString());
            foreach (Dtc d in s.Prev) snap.Prev.Add(d.ToString());
            return snap;
        }

        void ShowDiff()
        {
            if (_diffBox == null) return;
            _diffBox.Text = SessionIo.Diff(_snapA, _snapB);
        }

        void Mark(string tag)
        {
            BusSnapshot s = _snap;
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + tag +
                "  RPM=" + (double.IsNaN(s.Rpm) ? "—" : s.Rpm.ToString("0")) +
                "  Red=" + OnOff(s.Red) +
                "  Amber=" + OnOff(s.Amber) +
                "  DEF=" + s.DefText;
            _markers.Add(line);
            Log(line);
        }

        void TakeShot()
        {
            try
            {
                string path = SessionIo.Screenshot(_tabs != null ? (Control)_tabs : this);
                Log("Screenshot " + path);
                if (AfterScreenshot != null) AfterScreenshot(path);
            }
            catch (Exception ex) { Log("Screenshot failed: " + ex.Message); }
        }

        void ExportTrend()
        {
            try
            {
                List<TrendSample> data = _bus.TrendCopy();
                if (data.Count == 0) { Log("No trend samples yet."); return; }
                string path = System.IO.Path.Combine(SessionIo.Folder(),
                    SessionIo.BaseName(JobTag) + "_trend.csv");
                System.IO.File.WriteAllText(path, TrendLog.Csv(data), Encoding.UTF8);
                Log("Trend exported " + path);
            }
            catch (Exception ex) { Log("Trend export failed: " + ex.Message); }
        }

        void ExportTimeline()
        {
            try
            {
                string csv = _bus.TimelineCsv(_bus.ViewSa, JobTag);
                string path = System.IO.Path.Combine(SessionIo.Folder(),
                    SessionIo.BaseName(JobTag) + "_timeline.csv");
                System.IO.File.WriteAllText(path, csv, Encoding.UTF8);
                Log("Timeline exported " + path);
            }
            catch (Exception ex) { Log("Timeline export failed: " + ex.Message); }
        }

        ReportData BuildReportData()
        {
            BusSnapshot s = _snap;
            var d = new ReportData
            {
                JobTag = JobTag ?? "",
                Adapter = s.AdapterName,
                Vin = s.Vin,
                Sw = s.Sw,
                CompId = s.CompId,
                Hours = s.Hours,
                Rpm = s.Rpm,
                Red = s.Red,
                Amber = s.Amber,
                Protect = s.Protect,
                Mil = s.Mil,
                Def = s.DefText,
                Coolant = BusMonitor.Fmt(s.CoolantC, " C", "0"),
                Oil = s.OilText,
                Batt = BusMonitor.Fmt(s.BatteryV, " V", "0.00"),
                Fuel = BusMonitor.Fmt(s.FuelLph, " L/h", "0.00"),
                Diff = SessionIo.Diff(_snapA, _snapB)
            };
            d.Active.AddRange(s.Active);
            d.Prev.AddRange(s.Prev);
            d.Timeline.AddRange(_bus.TimelineCopy(_bus.ViewSa));
            d.Markers.AddRange(_markers);
            RefreshHistory(false);
            if (_history != null && !string.IsNullOrWhiteSpace(JobTag))
                d.HistoryNote = _history.Summary(JobTag);
            return d;
        }

        void PrintReport(bool preview)
        {
            try
            {
                ReportData d = BuildReportData();
                JobReportPrint.Print(d, preview, this);
                if (AfterReport != null) AfterReport(JobReport.Text(d));
            }
            catch (Exception ex)
            {
                Log("Report failed: " + ex.Message);
                MessageBox.Show(this, "Could not build the report.\n\n" + ex.Message,
                    "Report", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void SaveSession()
        {
            try { SaveSessionCore(); }
            catch (Exception ex)
            {
                Log("Save failed: " + ex.Message);
                MessageBox.Show(this, "Could not write the session file.\n\n" + ex.Message,
                    "Save", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void SaveSessionCore()
        {
            BusSnapshot s = _snap;
            var sb = new StringBuilder();
            sb.AppendLine("Tech Bench session  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Adapter " + s.AdapterName + "  device " + s.DeviceId + "  " + s.Protocol
                + "  connected=" + s.Connected);
            sb.AppendLine("Engine SA " + s.EngineSa + "  showing SA " + s.ViewSa);
            sb.AppendLine("RPM " + (double.IsNaN(s.Rpm) ? "—" : s.Rpm.ToString("0"))
                + "  Red Stop " + OnOff(s.Red)
                + "  Amber " + OnOff(s.Amber)
                + "  Protect " + OnOff(s.Protect)
                + "  MIL " + OnOff(s.Mil));
            sb.AppendLine("DEF " + s.DefText);
            sb.AppendLine("Coolant " + BusMonitor.Fmt(s.CoolantC, " C", "0")
                + "  Oil " + s.OilText
                + "  Battery " + BusMonitor.Fmt(s.BatteryV, " V", "0.00")
                + "  Fuel " + BusMonitor.Fmt(s.FuelLph, " L/h", "0.00"));
            sb.AppendLine("VIN " + s.Vin + "  SW " + s.Sw);
            sb.AppendLine("Component " + s.CompId + "  Hours " + s.Hours);
            sb.AppendLine();
            sb.AppendLine("Active DTCs:");
            if (s.Active.Count == 0) sb.AppendLine("  (none)");
            else foreach (Dtc d in s.Active) sb.AppendLine("  " + d);
            sb.AppendLine("Previous DTCs:");
            if (s.Prev.Count == 0) sb.AppendLine("  (none)");
            else foreach (Dtc d in s.Prev) sb.AppendLine("  " + d);
            sb.AppendLine();
            sb.AppendLine("Fault timeline:");
            List<DtcEvent> timeline = _bus.TimelineCopy(_bus.ViewSa);
            if (timeline.Count == 0) sb.AppendLine("  (none)");
            else foreach (DtcEvent ev in timeline) sb.AppendLine("  " + ev);
            sb.AppendLine();
            sb.AppendLine("Markers:");
            if (_markers.Count == 0) sb.AppendLine("  (none)");
            else foreach (string m in _markers) sb.AppendLine("  " + m);
            sb.AppendLine();
            sb.AppendLine("Modules:");
            foreach (SaRow r in s.Sas)
                sb.AppendLine("  SA " + r.Sa + " " + Names.Sa(r.Sa) + " x" + r.Count);

            var csv = new StringBuilder();
            csv.AppendLine("time,job,rpm,red,amber,protect,mil,def,coolant,oil,battery,fuel,active_count,previous_count");
            csv.AppendLine(string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13}",
                DateTime.Now.ToString("o"), Quote(JobTag),
                double.IsNaN(s.Rpm) ? "" : s.Rpm.ToString("0"), s.Red, s.Amber, s.Protect, s.Mil,
                Quote(s.DefText), Quote(BusMonitor.Fmt(s.CoolantC, " C", "0")), Quote(s.OilText),
                Quote(BusMonitor.Fmt(s.BatteryV, " V", "0.00")), Quote(BusMonitor.Fmt(s.FuelLph, " L/h", "0.00")),
                s.Active.Count, s.Prev.Count));

            var dtcCsv = new StringBuilder();
            dtcCsv.AppendLine("time,job,state,spn,fmi,occurrences,name,fmi_text");
            AppendDtcRows(dtcCsv, "active", s.Active);
            AppendDtcRows(dtcCsv, "previous", s.Prev);

            if (!string.IsNullOrWhiteSpace(JobTag))
                sb.Insert(0, "Job  " + JobTag + Environment.NewLine);

            string path = SessionIo.SaveSession(sb.ToString(), csv.ToString(), dtcCsv.ToString(),
                TrendLog.Csv(_bus.TrendCopy()), _bus.TimelineCsv(_bus.ViewSa, JobTag), JobTag);
            Log("Saved " + path);
            // The new files are history for next time; drop the cached index so a rescan picks them up.
            _history = null;
            _historyJob = "";
            if (SessionSaved != null) SessionSaved(path);
        }

        void AppendDtcRows(StringBuilder csv, string state, List<Dtc> items)
        {
            string when = DateTime.Now.ToString("o");
            foreach (Dtc d in items)
                csv.AppendLine(when + "," + Quote(JobTag) + "," + state + "," + d.Spn + "," + d.Fmi + ","
                    + d.Occ + "," + Quote(d.Name) + "," + Quote(d.FmiText));
        }

        static string Quote(string s)
        {
            if (s == null) s = "";
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
