using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using J1939Reader;

namespace TechBench
{
    public class ShellForm : Form
    {
        readonly TextBox _model;
        readonly TextBox _serial;
        readonly TextBox _customer;
        readonly ComboBox _woPick;
        readonly Inline7Control _inline;
        readonly SearchControl _search;
        readonly WorkOrderControl _orders;
        readonly Panel _host;
        readonly Control[] _pages;
        readonly Control _pSearch;
        readonly Control _pOrders;
        readonly FlowLayoutPanel _nav;
        readonly FlowLayoutPanel _job;
        readonly FlowLayoutPanel _actions;
        readonly List<Control> _jobExtras = new List<Control>();
        readonly Font _navNorm;
        readonly Font _navBold;
        readonly Font _navNormShort;
        readonly Font _navBoldShort;
        int _page;
        bool _shortChrome;
        bool _chromeBusy;
        readonly ToolStripMenuItem _installUpdate;
        readonly AppSettings _settings;
        readonly TextBox _syncStatus;
        readonly Label _jobSync;
        readonly System.Windows.Forms.Timer _watchDebounce = new System.Windows.Forms.Timer();
        readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        KbIndex _kb;
        UpdateManifest _ready;
        bool _checking;
        ShopSyncResult _sync;
        bool _syncing;
        bool _filling;

        public ShellForm(KbIndex kb)
        {
            _kb = kb;
            _settings = AppSettings.Load();
            Text = Title("");
            // 800-tall client + chrome is already taller than a 1366×768 shop laptop.
            MinimumSize = new Size(760, 520);
            Size = UiLayout.SizeForScreen(1180, 720, 760, 520);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9.5f);
            _navNorm = new Font("Segoe UI", 12f, FontStyle.Regular);
            _navBold = new Font("Segoe UI", 12f, FontStyle.Bold);
            _navNormShort = new Font("Segoe UI", 10f, FontStyle.Regular);
            _navBoldShort = new Font("Segoe UI", 10f, FontStyle.Bold);
            string assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
            try
            {
                string ico = Path.Combine(assets, "app.ico");
                if (File.Exists(ico)) Icon = new Icon(ico);
            }
            catch { }

            var menu = new MenuStrip();
            var shop = new ToolStripMenuItem("&Shop");
            var syncNow = new ToolStripMenuItem("Sync &now");
            var syncDlg = new ToolStripMenuItem("&Sync…");
            var addNote = new ToolStripMenuItem("Add &note");
            var addFile = new ToolStripMenuItem("Add &file…");
            syncNow.Click += delegate { RunSync(false); };
            syncDlg.Click += delegate { OpenSync(); };
            addNote.Click += delegate { AddNote(); };
            addFile.Click += delegate { AddShopFile(); };
            shop.DropDownItems.Add(syncNow);
            shop.DropDownItems.Add(syncDlg);
            shop.DropDownItems.Add(new ToolStripSeparator());
            shop.DropDownItems.Add(addNote);
            shop.DropDownItems.Add(addFile);
            shop.DropDownItems.Add(new ToolStripSeparator());
            var idSettings = new ToolStripMenuItem("Intelli&Dealer settings");
            var refreshWo = new ToolStripMenuItem("&Refresh work orders");
            idSettings.Click += delegate { OpenIdSettings(); };
            refreshWo.Click += delegate { _orders.Reload(true); FillWoPick(); };
            shop.DropDownItems.Add(idSettings);
            shop.DropDownItems.Add(refreshWo);
            menu.Items.Add(shop);

            var help = new ToolStripMenuItem("&Help");
            var check = new ToolStripMenuItem("&Check for updates");
            _installUpdate = new ToolStripMenuItem("&Install update") { Enabled = false };
            var about = new ToolStripMenuItem("&About Tech Bench");
            check.Click += delegate { CheckUpdates(true); };
            _installUpdate.Click += delegate { InstallReadyUpdate(); };
            about.Click += delegate { ShowAbout(); };
            help.DropDownItems.Add(check);
            help.DropDownItems.Add(_installUpdate);
            help.DropDownItems.Add(new ToolStripSeparator());
            help.DropDownItems.Add(about);
            menu.Items.Add(help);
            MainMenuStrip = menu;

            _syncStatus = UiLayout.WrapText("Sync: starting…");
            _syncStatus.Dock = DockStyle.Bottom;
            _syncStatus.Padding = new Padding(8, 4, 8, 4);
            _syncStatus.Cursor = Cursors.Hand;
            _syncStatus.Click += delegate { OpenSync(); };

            // Two wrapping rows: identity (WO/customer/model/serial) then actions.
            // One row left Serial / Settings / Search this job off the window after sync text grew.
            // Short client (≤600): hide customer/model/serial and the bottom sync line so
            // Search / WO / INLINE 7 / Adapters keep the vertical budget.
            _job = UiLayout.WrapBar(UiLayout.JobBarPad(false));
            _job.BackColor = Color.FromArgb(22, 32, 48);
            _job.Controls.Add(JobLabel("WO"));
            _woPick = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 280,
                DropDownWidth = 480,
                Margin = new Padding(0, 2, 12, 0)
            };
            _woPick.SelectedIndexChanged += delegate { PickWoFromStrip(); };
            _job.Controls.Add(_woPick);
            Label labCustomer = JobLabel("Customer");
            _customer = new TextBox { Width = 140, Margin = new Padding(0, 2, 12, 0) };
            _customer.TextChanged += delegate { PushHeader(); };
            _job.Controls.Add(labCustomer);
            _job.Controls.Add(_customer);
            Label labModel = JobLabel("Model");
            _model = new TextBox { Width = 130, Margin = new Padding(0, 2, 12, 0) };
            _job.Controls.Add(labModel);
            _job.Controls.Add(_model);
            Label labSerial = JobLabel("Serial");
            _serial = new TextBox { Width = 140, Margin = new Padding(0, 2, 12, 0) };
            _job.Controls.Add(labSerial);
            _job.Controls.Add(_serial);
            _jobExtras.Add(labCustomer);
            _jobExtras.Add(_customer);
            _jobExtras.Add(labModel);
            _jobExtras.Add(_model);
            _jobExtras.Add(labSerial);
            _jobExtras.Add(_serial);

            _actions = UiLayout.WrapBar(UiLayout.ActionBarPad(false));
            _actions.BackColor = Color.FromArgb(22, 32, 48);
            _jobSync = new Label
            {
                Text = "Sync: …",
                ForeColor = Color.Khaki,
                AutoSize = true,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 8, 12, 0)
            };
            _actions.Controls.Add(_jobSync);
            _jobSync.Click += delegate { OpenSync(); };
            var useJob = new Button { Text = "Search this job", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, AutoEllipsis = false, Margin = new Padding(0, 0, 12, 0) };
            _actions.Controls.Add(useJob);
            var addCode = new Button { Text = "Add code to KB", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, AutoEllipsis = false, Margin = new Padding(0, 0, 8, 0) };
            _actions.Controls.Add(addCode);
            var addNoteBtn = new Button { Text = "Add note", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, AutoEllipsis = false, Margin = new Padding(0, 0, 8, 0) };
            _actions.Controls.Add(addNoteBtn);
            addNoteBtn.Click += delegate { AddNote(); };

            _search = new SearchControl(kb);
            _inline = new Inline7Control();
            _orders = new WorkOrderControl(kb == null ? "" : kb.Root);

            // Panels, not TabControl: native headers paint "Work orders / INLINE 7 / Adapters"
            // at 192 DPI but those pages are not hit-testable. Big wrap buttons + Visible pages
            // cannot ghost.
            _pSearch = ShellPage("shellPage0", _search);
            _pOrders = ShellPage("shellPage1", _orders);
            var pInline = ShellPage("shellPage2", _inline);
            var pAdapters = ShellPage("shellPage3", BuildAdaptersPanel());
            _pages = new Control[] { _pSearch, _pOrders, pInline, pAdapters };
            _host = new Panel { Name = "shellHost", Dock = DockStyle.Fill };

            _nav = UiLayout.SwitchBar(
                new[] { "Search", "Work orders", "INLINE 7", "Adapters" },
                delegate(int i) { ShowPage(i); });
            ShowPage(0);

            // Last-added docks nearest the edge: menu, switches, identity, then actions.
            Controls.Add(_host);
            Controls.Add(_actions);
            Controls.Add(_job);
            Controls.Add(_nav);
            Controls.Add(menu);
            Controls.Add(_syncStatus);

            if (!string.IsNullOrEmpty(_settings.Model)) _model.Text = _settings.Model;
            if (!string.IsNullOrEmpty(_settings.Serial)) _serial.Text = _settings.Serial;
            ApplyWindow(_settings);

            useJob.Click += delegate
            {
                string q = (_model.Text + " " + _serial.Text).Trim();
                ShowPage(0);
                _search.Prefill(q);
                SyncJob();
            };
            addCode.Click += delegate { AddOrEditCode(); };

            // TextChanged, not Leave: saving a session straight after typing the serial used to write
            // the file without the job tag because focus had never left the box.
            _model.TextChanged += delegate { PushHeader(); };
            _serial.TextChanged += delegate { PushHeader(); };

            // The INLINE 7 tab knows SPNs; the knowledge base knows the shop's own write-ups on them.
            _inline.OpenInSearch = delegate(string query)
            {
                ShowPage(0);
                _search.Prefill(query);
            };
            _inline.LookupCode = delegate(int spn, int fmi)
            {
                return _kb == null ? null : _kb.SpnText(spn, fmi);
            };
            _inline.SessionSaved = delegate(string path)
            {
                try
                {
                    if (string.IsNullOrEmpty(path) || _kb == null) return;
                    string dtc = Path.Combine(Path.GetDirectoryName(path),
                        Path.GetFileNameWithoutExtension(path) + "_dtcs.csv");
                    ShopSync.PublishHistory(_kb.Root, ShopSync.LoadSettings().TechId, dtc);
                    RunSync(false);
                }
                catch { }
            };
            _inline.AfterScreenshot = delegate(string path) { _orders.AttachShot(path); };
            _inline.AfterReport = delegate(string text) { _orders.AttachReportText(text); };
            _orders.LiveReportText = delegate { return _inline.LiveReportText(); };
            _orders.CurrentChanged = delegate(WorkOrder wo) { ApplyWo(wo); };

            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if ((e.Control && e.KeyCode == Keys.F) || e.KeyCode == Keys.F3)
                {
                    ShowPage(0);
                    _search.FocusQuery();
                    e.Handled = true;
                }
                else if (e.Control && e.KeyCode == Keys.N)
                {
                    AddOrEditCode();
                    e.Handled = true;
                }
            };

            _watchDebounce.Interval = 900;
            _watchDebounce.Tick += delegate { _watchDebounce.Stop(); RunSync(false); };
            FormClosing += delegate { PersistSettings(); };
            FormClosed += delegate
            {
                _watchDebounce.Stop();
                _watchDebounce.Dispose();
                foreach (FileSystemWatcher w in _watchers)
                {
                    try { w.Dispose(); }
                    catch { }
                }
                _watchers.Clear();
            };
            SizeChanged += delegate { ApplyChromeBudget(); };
            Shown += delegate
            {
                UiLayout.FitToWorkingArea(this);
                ApplyChromeBudget();
                ApplyWo(_orders.Current);
                SyncJob();
                RunSync(false);
                StartWatchers();
                if (Updater.HasVerifiedPending(AppDomain.CurrentDomain.BaseDirectory))
                    MarkReadyFromPending();
                ThreadPool.QueueUserWorkItem(delegate { CheckUpdates(false); });
            };
        }

        static string Title(string jobTag)
        {
            string head = "Tech Bench " + AppVersion.Number;
            return string.IsNullOrEmpty(jobTag) ? head : (head + "  ·  " + jobTag);
        }

        static Label JobLabel(string text)
        {
            return new Label
            {
                Text = text,
                ForeColor = Color.WhiteSmoke,
                AutoSize = true,
                Margin = new Padding(0, 4, 6, 0)
            };
        }

        void PaintNav()
        {
            UiLayout.MarkSwitch(_nav, _page,
                _shortChrome ? _navNormShort : _navNorm,
                _shortChrome ? _navBoldShort : _navBold);
        }

        void ApplyChromeBudget()
        {
            if (_chromeBusy || _nav == null || _job == null || _actions == null) return;
            bool compact = UiLayout.ShortClient(this);
            if (compact == _shortChrome) return;
            _chromeBusy = true;
            try
            {
                _shortChrome = compact;
                UiLayout.SetSwitchChrome(_nav, compact);
                _job.Padding = UiLayout.JobBarPad(compact);
                _actions.Padding = UiLayout.ActionBarPad(compact);
                for (int i = 0; i < _jobExtras.Count; i++)
                    _jobExtras[i].Visible = !compact;
                _syncStatus.Visible = !compact;
                PaintNav();
                PerformLayout();
            }
            finally { _chromeBusy = false; }
        }

        void ShowPage(int i)
        {
            if (_pages == null || i < 0 || i >= _pages.Length) return;
            _page = i;
            // One Dock.Fill child only. Sibling fill pages stay on Search at 192 DPI.
            _host.SuspendLayout();
            _host.Controls.Clear();
            Control page = _pages[i];
            page.Visible = true;
            page.Dock = DockStyle.Fill;
            _host.Controls.Add(page);
            _host.ResumeLayout(true);
            PaintNav();
        }

        static Panel ShellPage(string name, Control body)
        {
            var p = new Panel { Name = name, Dock = DockStyle.Fill, Visible = false };
            if (body != null)
            {
                body.Dock = DockStyle.Fill;
                p.Controls.Add(body);
            }
            return p;
        }

        void ApplyWindow(AppSettings s)
        {
            if (s == null) return;
            if (s.Width >= MinimumSize.Width && s.Height >= MinimumSize.Height)
            {
                var bounds = new Rectangle(s.X, s.Y, s.Width, s.Height);
                if (OnAnyScreen(bounds))
                {
                    StartPosition = FormStartPosition.Manual;
                    Bounds = bounds;
                }
            }
            if (s.Maximized) WindowState = FormWindowState.Maximized;
            UiLayout.FitToWorkingArea(this);
            ApplyChromeBudget();
        }

        static bool OnAnyScreen(Rectangle bounds)
        {
            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.WorkingArea.IntersectsWith(bounds)) return true;
            }
            return false;
        }

        void PersistSettings()
        {
            _settings.Model = _model.Text.Trim();
            _settings.Serial = _serial.Text.Trim();
            _settings.Maximized = WindowState == FormWindowState.Maximized;
            Rectangle r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _settings.X = r.X;
            _settings.Y = r.Y;
            _settings.Width = r.Width;
            _settings.Height = r.Height;
            _settings.Save();
        }

        void ShowAbout()
        {
            string kb = (_kb == null || string.IsNullOrEmpty(_kb.Root)) ? "(none)" : _kb.Root;
            string bits = Rp1210.HostIs32Bit ? "32-bit (correct for RP1210)" : "64-bit — rebuild with build.bat";
            UiLayout.ShowReadable(this, "About Tech Bench",
                "Tech Bench " + AppVersion.Number + "\r\n\r\n"
                + "Shop tool: knowledge-base search and Cummins INLINE 7 / J1939.\r\n\r\n"
                + "This copy: " + Application.ExecutablePath + "\r\n"
                + "Process: " + bits + "\r\n"
                + "Knowledge base: " + kb + "\r\n"
                + (_kb != null && !string.IsNullOrEmpty(_kb.Status) ? ("Index: " + _kb.Status + "\r\n") : "")
                + "\r\nUpdates download a public latest.json and a hashed TechBench.exe into this folder.\r\n"
                + "The app never stores a GitHub token. Updates apply after you quit, never mid-session.");
        }

        void MarkReadyFromPending()
        {
            _installUpdate.Enabled = true;
            _installUpdate.Text = "&Install pending update";
        }

        void CheckUpdates(bool interactive)
        {
            if (_checking)
            {
                if (interactive)
                    MessageBox.Show(this, "Already checking for updates…",
                        "Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _checking = true;
            string url = Updater.ManifestUrl(_settings);
            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateCheck result;
                try { result = Updater.Check(url); }
                catch (Exception ex)
                {
                    result = new UpdateCheck { Error = ex.Message };
                }
                if (IsDisposed) return;
                try
                {
                    BeginInvoke(new Action(delegate { FinishCheck(result, interactive); }));
                }
                catch { }
            });
        }

        void FinishCheck(UpdateCheck result, bool interactive)
        {
            _checking = false;
            if (!Updater.IsAvailable(result))
            {
                if (!interactive) return;
                MessageBoxIcon icon = Updater.IsCurrent(result)
                    ? MessageBoxIcon.Information
                    : MessageBoxIcon.Warning;
                MessageBox.Show(this, Updater.StatusText(result), "Updates", MessageBoxButtons.OK, icon);
                return;
            }

            _ready = result.Manifest;
            _installUpdate.Enabled = true;
            _installUpdate.Text = "&Install update " + _ready.Version;
            if (!interactive) return;

            string notes = string.IsNullOrEmpty(_ready.Notes) ? "" : ("\n\n" + _ready.Notes);
            DialogResult ask = MessageBox.Show(this,
                "Version " + _ready.Version + " is available (you have " + AppVersion.Number + ")."
                + notes + "\n\nDownload and install after this window closes?",
                "Updates", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ask == DialogResult.Yes) InstallReadyUpdate();
        }

        void InstallReadyUpdate()
        {
            if (!Updater.CanApplyNow(_inline.SessionLive))
            {
                MessageBox.Show(this,
                    "Disconnect the adapter before installing an update.\n\n"
                    + "The new TechBench.exe is swapped by a small script after this window closes, never mid-session.",
                    "Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                if (_ready != null)
                {
                    byte[] bytes = Updater.DownloadExe(_ready.Url);
                    Updater.Stage(exeDir, bytes, _ready.Sha256);
                }
                else if (!Updater.HasVerifiedPending(exeDir))
                {
                    MessageBox.Show(this, "No update is staged. Use Help → Check for updates first.",
                        "Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            PersistSettings();
            Updater.LaunchSwap(exeDir);
            Close();
        }

        Control BuildAdaptersPanel()
        {
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            var box = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                MaxLength = 0,
                WordWrap = false,
                Font = new Font("Consolas", 9.5f)
            };
            var bar = UiLayout.WrapBar(new Padding(0, 0, 0, 8));
            var rescan = new Button { Text = "Rescan", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, AutoEllipsis = false };
            bar.Controls.Add(rescan);
            rescan.Click += delegate { box.Text = DescribeAdapters(); };
            box.Text = DescribeAdapters();
            host.Controls.Add(box);
            host.Controls.Add(bar);
            return host;
        }

        static string DescribeAdapters()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("RP1210 adapters installed on this PC");
            sb.AppendLine("(read from " + Path.Combine(Rp1210Api.WindowsDir(), "RP121032.INI") + ")");
            sb.AppendLine();
            sb.AppendLine("Process: " + (Rp1210.HostIs32Bit ? "32-bit — correct for RP1210" : "64-bit — RP1210 drivers will NOT load; rebuild with build.bat"));
            sb.AppendLine();
            List<Rp1210Api> apis = Rp1210.Adapters();
            if (apis.Count == 0)
            {
                sb.AppendLine("None found.");
                sb.AppendLine();
                sb.AppendLine("Install the adapter vendor's RP1210 drivers (for the INLINE 7 that is the");
                sb.AppendLine("Cummins CIL7 package), then press Rescan.");
                return sb.ToString();
            }
            foreach (Rp1210Api a in apis)
            {
                sb.AppendLine(a.Describe());
                sb.AppendLine();
            }
            sb.AppendLine("The INLINE 7 tab connects with whichever adapter is picked in its Adapter box.");
            sb.AppendLine("Sullair / IR Xe datalinks appear here automatically once their drivers are installed.");
            return sb.ToString();
        }

        void AddOrEditCode()
        {
            if (_kb == null || string.IsNullOrEmpty(_kb.Root))
            {
                MessageBox.Show(this, "No knowledge base folder is configured, so there is nowhere to save.",
                    "Add code", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var seed = new UserCode();
            Hit sel = _search.Selected;
            if (sel != null && sel.Kind == "CODE")
            {
                // Pre-fill from the highlighted entry so editing an existing code is one click.
                foreach (UserCode existing in ShopSync.LoadAllCodes(_kb.Root))
                {
                    string title = (existing.Brand + "  " + existing.Code + "  —  " + existing.Title);
                    if (string.Equals(title, sel.Title, StringComparison.OrdinalIgnoreCase)) { seed = existing; break; }
                }
                if (!seed.IsUsable())
                {
                    seed.Title = sel.Title;
                }
            }
            using (var dlg = new CodeEditForm(seed))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result == null) return;
                try
                {
                    ShopSync.SaveUserCode(_kb.Root, ShopSync.LoadSettings().TechId, dlg.Result);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not write your shop user-codes.json.\n\n" + ex.Message,
                        "Add code", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                RunSync(false);
                ReloadKb();
                ShowPage(0);
                _search.Prefill((dlg.Result.Code + " " + dlg.Result.Title).Trim());
            }
        }

        void AddNote()
        {
            if (_kb == null || string.IsNullOrEmpty(_kb.Root))
            {
                MessageBox.Show(this, "No knowledge base folder is configured, so there is nowhere to save.",
                    "Add note", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var dlg = new NoteEditForm())
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    ShopSync.SaveNote(_kb.Root, ShopSync.LoadSettings().TechId, dlg.NoteTitle, dlg.NoteBody);
                    RunSync(false);
                    ReloadKb();
                    ShowPage(0);
                    string q = dlg.NoteTitle;
                    if (string.IsNullOrWhiteSpace(q))
                    {
                        foreach (string raw in (dlg.NoteBody ?? "").Replace("\r\n", "\n").Split('\n'))
                        {
                            if (raw.Trim().Length > 0) { q = raw.Trim(); break; }
                        }
                    }
                    _search.Prefill(q);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not write the note.\n\n" + ex.Message,
                        "Add note", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        void AddShopFile()
        {
            if (_kb == null || string.IsNullOrEmpty(_kb.Root))
            {
                MessageBox.Show(this, "No knowledge base folder is configured, so there is nowhere to save.",
                    "Add file", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Add a shop file (PDF, photo, chart…)";
                dlg.Filter = "All files (*.*)|*.*|PDF (*.pdf)|*.pdf|Images (*.png;*.jpg)|*.png;*.jpg;*.jpeg";
                dlg.Multiselect = false;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string dest = ShopSync.AddFile(_kb.Root, ShopSync.LoadSettings().TechId, dlg.FileName);
                    RunSync(false);
                    ReloadKb();
                    ShowPage(0);
                    _search.Prefill(Path.GetFileName(dest));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not copy the file into the knowledge base.\n\n" + ex.Message,
                        "Add file", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        void OpenSync()
        {
            using (var dlg = new SyncForm(_kb == null ? "" : _kb.Root, _sync))
            {
                dlg.ShowDialog(this);
                _sync = dlg.LastResult;
                if (_kb != null && !string.IsNullOrEmpty(_kb.Root)) ReloadKb();
                ApplySyncStatus(_sync);
                RestartWatchers();
            }
        }

        void RunSync(bool noisy)
        {
            if (_syncing) return;
            if (_kb == null || string.IsNullOrEmpty(_kb.Root))
            {
                _syncStatus.Text = "Sync: no knowledge base folder";
                if (noisy)
                    MessageBox.Show(this, "No knowledge base folder is configured.", "Shop sync",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _syncing = true;
            try
            {
                _sync = ShopSync.Run(_kb.Root);
                try { ShopSync.ImportHistory(_kb.Root, SessionIo.Folder()); }
                catch { }
                ApplySyncStatus(_sync);
                ReloadKb();
            }
            catch (Exception ex)
            {
                _syncStatus.Text = "Sync failed: " + ex.Message;
                if (noisy)
                    MessageBox.Show(this, ex.Message, "Shop sync", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { _syncing = false; }
        }

        void ApplySyncStatus(ShopSyncResult r)
        {
            if (r == null)
            {
                _syncStatus.Text = "Sync: idle";
                _jobSync.Text = "Sync: idle";
                return;
            }
            _syncStatus.Text = r.Status;
            _syncStatus.ForeColor = r.Conflicts > 0 ? Color.DarkRed : Color.Black;
            _jobSync.ForeColor = r.Conflicts > 0 ? Color.OrangeRed : Color.Khaki;
            string tech = ShopSync.SanitizeTechId(ShopSync.LoadSettings().TechId);
            _jobSync.Text = r.Conflicts > 0
                ? (r.Conflicts + " sync conflict(s) — click")
                : ("Sync · " + tech + " · click");
            Control bar = _jobSync.Parent;
            if (bar != null) bar.PerformLayout();
        }

        void RestartWatchers()
        {
            foreach (FileSystemWatcher w in _watchers)
            {
                try { w.Dispose(); }
                catch { }
            }
            _watchers.Clear();
            StartWatchers();
        }

        void StartWatchers()
        {
            if (_kb == null || string.IsNullOrEmpty(_kb.Root)) return;
            WatchDir(Path.Combine(_kb.Root, ShopSync.ShopRel));
            WatchDir(Path.Combine(_kb.Root, ShopSync.NotesRel));
            WatchDir(Path.Combine(_kb.Root, ShopSync.FilesRel));
            ShopSyncSettings s = ShopSync.LoadSettings();
            if (!ShopSync.IsSharedTree(_kb.Root, s) && Directory.Exists(s.SyncFolder))
                WatchDir(s.SyncFolder);
        }

        void WatchDir(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var w = new FileSystemWatcher(dir);
                w.IncludeSubdirectories = true;
                w.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite | NotifyFilters.Size;
                FileSystemEventHandler ping = delegate
                {
                    try
                    {
                        if (IsHandleCreated && !IsDisposed)
                            BeginInvoke(new Action(OnShopChanged));
                    }
                    catch { }
                };
                w.Changed += ping;
                w.Created += ping;
                w.Deleted += ping;
                w.Renamed += delegate
                {
                    try
                    {
                        if (IsHandleCreated && !IsDisposed)
                            BeginInvoke(new Action(OnShopChanged));
                    }
                    catch { }
                };
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch { }
        }

        void OnShopChanged()
        {
            _watchDebounce.Stop();
            _watchDebounce.Start();
        }

        void ReloadKb()
        {
            try
            {
                var fresh = new KbIndex();
                fresh.Load(_kb.Root);
                _kb = fresh;
                _search.Rebind(fresh);
                _orders.SetKbRoot(fresh.Root);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Saved, but reloading the index failed.\n\n" + ex.Message,
                    "Add code", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void SyncJob()
        {
            WorkOrder wo = _orders.Current;
            string tag;
            if (wo != null && !string.IsNullOrWhiteSpace(wo.Number))
                tag = wo.JobTag();
            else
                tag = (_model.Text + " " + _serial.Text).Trim();
            _inline.JobTag = tag;
            Text = Title(tag);
        }

        void OpenIdSettings()
        {
            using (var dlg = new IdSettingsForm(IdSettings.Load()))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result == null) return;
                dlg.Result.Save();
                _orders.ReloadSettings();
                FillWoPick();
            }
        }

        void FillWoPick()
        {
            string keep = _orders.Current == null ? "" : _orders.Current.Key();
            _filling = true;
            _woPick.Items.Clear();
            _woPick.Items.Add("(no work order)");
            int sel = 0;
            List<WorkOrder> all = _orders.Orders();
            for (int i = 0; i < all.Count; i++)
            {
                _woPick.Items.Add(all[i].ListLabel());
                if (keep.Length > 0 && string.Equals(all[i].Key(), keep, StringComparison.OrdinalIgnoreCase))
                    sel = i + 1;
            }
            _woPick.SelectedIndex = sel;
            _filling = false;
        }

        void PickWoFromStrip()
        {
            if (_filling) return;
            int i = _woPick.SelectedIndex;
            if (i <= 0)
            {
                SyncJob();
                return;
            }
            List<WorkOrder> all = _orders.Orders();
            int idx = i - 1;
            if (idx < 0 || idx >= all.Count) return;
            _orders.SelectKey(all[idx].Key());
        }

        void ApplyWo(WorkOrder wo)
        {
            _filling = true;
            if (wo == null)
            {
                FillWoPick();
                _filling = false;
                SyncJob();
                return;
            }
            if (!string.IsNullOrEmpty(wo.Model)) _model.Text = wo.Model;
            if (!string.IsNullOrEmpty(wo.Serial)) _serial.Text = wo.Serial;
            if (!string.IsNullOrEmpty(wo.Customer)) _customer.Text = wo.Customer;
            FillWoPick();
            _filling = false;
            SyncJob();
        }

        void PushHeader()
        {
            if (_filling) return;
            _orders.ApplyHeader(_model.Text, _serial.Text, _customer.Text);
            SyncJob();
        }
    }
}
