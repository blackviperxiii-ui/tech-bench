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
        readonly ShopWatcher _shopWatcher;
        KbIndex _kb;
        UpdateManifest _ready;
        bool _checking;
        ShopSyncResult _sync;
        bool _syncing;
        bool _filling;
        CheatSheetForm _cheatSheet;

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
            var shopSettings = new ToolStripMenuItem("Shop s&ettings…");
            var refreshWo = new ToolStripMenuItem("&Refresh work orders");
            shopSettings.Click += delegate { OpenShopSettings(); };
            refreshWo.Click += delegate { _orders.Reload(true); FillWoPick(); };
            shop.DropDownItems.Add(shopSettings);
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
            var bodies = new Dictionary<ShellPageId, Control>
            {
                { ShellPageId.Search, _search },
                { ShellPageId.WorkOrders, _orders },
                { ShellPageId.Inline7, _inline },
                { ShellPageId.Adapters, new AdaptersPage() }
            };
            _pages = new Control[ShellPages.Count];
            for (int i = 0; i < _pages.Length; i++)
            {
                ShellPageInfo info = ShellPages.At(i);
                _pages[i] = ShellPage(info.PanelName, bodies[info.Id]);
            }
            _host = new Panel { Name = "shellHost", Dock = DockStyle.Fill };

            _nav = UiLayout.SwitchBar(ShellPages.Titles(), delegate(int i) { ShowPage(i); });
            // Startup page restore must not persist: ShowPage(0) would wipe a saved shell page.
            SettingsWriteGate.BeginLoad();
            try
            {
                ShowPage(0, false);
                // The shell page has nothing to do with the adapter, so it restores on a PC without
                // one too. Only the INLINE 7 inner page waits for the saved adapter to match.
                int shell = AdapterPrefs.ClampShell(AdapterPrefs.Load().ShellPage);
                if (shell != 0) ShowPage(shell, false);
            }
            finally { SettingsWriteGate.EndLoad(); }

            // Last-added docks nearest the edge: menu, switches, identity, then actions.
            Controls.Add(_host);
            Controls.Add(_actions);
            Controls.Add(_job);
            Controls.Add(_nav);
            Controls.Add(menu);
            Controls.Add(_syncStatus);

            SettingsWriteGate.BeginLoad();
            _filling = true;
            try
            {
                if (!string.IsNullOrEmpty(_settings.Model)) _model.Text = _settings.Model;
                if (!string.IsNullOrEmpty(_settings.Serial)) _serial.Text = _settings.Serial;
                string jobCustomer = JobStrip.StartupCustomer(_settings.Customer, _orders.SavedJobCustomer);
                if (!string.IsNullOrEmpty(jobCustomer)) _customer.Text = jobCustomer;
            }
            finally
            {
                _filling = false;
                SettingsWriteGate.EndLoad();
            }
            ApplyWindow(_settings);

            useJob.Click += delegate
            {
                string q = JobStrip.SearchQuery(_model.Text, _serial.Text);
                ShowPage(ShellPageId.Search);
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
                ShowPage(ShellPageId.Search);
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
                    string dtc = JobStrip.DtcCsvFor(path);
                    ShopSync.PublishHistory(_kb.Root, ShopSync.LoadSettings().TechId, dtc);
                    RunSync(false);
                }
                catch { }
            };
            _inline.AfterScreenshot = delegate(string path) { _orders.AttachShot(path); };
            _inline.AfterReport = delegate(string text) { _orders.AttachReportText(text); };
            _orders.LiveReportText = delegate { return _inline.LiveReportText(); };
            _orders.CurrentChanged = delegate(WorkOrder wo) { ApplyWo(wo); };
            _orders.JobStripCleared = delegate
            {
                _filling = true;
                _customer.Text = "";
                _filling = false;
            };

            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                switch (ShellShortcuts.For(e.Control, e.KeyCode))
                {
                    case ShellShortcut.FocusSearch:
                        ShowPage(ShellPageId.Search);
                        _search.FocusQuery();
                        e.Handled = true;
                        break;
                    case ShellShortcut.AddCode:
                        AddOrEditCode();
                        e.Handled = true;
                        break;
                    case ShellShortcut.CheatSheet:
                        ShowCheatSheet();
                        e.Handled = true;
                        break;
                }
            };

            _shopWatcher = new ShopWatcher(this, delegate { RunSync(false); });
            FormClosing += delegate { PersistSettings(); };
            FormClosed += delegate { _shopWatcher.Dispose(); };
            SizeChanged += delegate { ApplyChromeBudget(); };
            Shown += delegate
            {
                UiLayout.FitToWorkingArea(this);
                ApplyChromeBudget();
                ApplyWo(_orders.Current);
                SyncJob();
                RunSync(false);
                _shopWatcher.Start(_kb == null ? "" : _kb.Root);
                if (Updater.HasVerifiedPending(AppDomain.CurrentDomain.BaseDirectory))
                    MarkReadyFromPending();
                ThreadPool.QueueUserWorkItem(delegate { CheckUpdates(false); });
            };
        }

        static string Title(string jobTag)
        {
            return JobStrip.WindowTitle(AppVersion.Number, jobTag);
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
            ShowPage(i, true);
        }

        void ShowPage(ShellPageId id)
        {
            ShowPage((int)id, true);
        }

        void ShowPage(int i, bool persistShellPage)
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
            if (persistShellPage && _inline != null)
                SettingsStore.MergeAdapterPrefs(_inline.CurrentAdapterId(), i, null);
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
            var areas = new List<Rectangle>();
            foreach (Screen screen in Screen.AllScreens) areas.Add(screen.WorkingArea);
            Rectangle? saved = WindowPlacement.SavedBounds(s, MinimumSize, areas);
            if (saved.HasValue)
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = saved.Value;
            }
            if (s.Maximized) WindowState = FormWindowState.Maximized;
            UiLayout.FitToWorkingArea(this);
            ApplyChromeBudget();
        }

        void PersistSettings()
        {
            _settings.Model = _model.Text.Trim();
            _settings.Serial = _serial.Text.Trim();
            // Job customer and last WO are owned by WorkOrderControl; window save must not clobber them.
            _settings.Customer = "";
            _settings.LastWo = "";
            WindowPlacement.Capture(_settings, WindowState == FormWindowState.Maximized,
                WindowState == FormWindowState.Normal ? Bounds : RestoreBounds);
            SettingsStore.SaveAppSettings(_settings);
        }

        void ShowCheatSheet()
        {
            if (_cheatSheet == null || _cheatSheet.IsDisposed)
            {
                _cheatSheet = new CheatSheetForm();
                _cheatSheet.Owner = this;
                _cheatSheet.FormClosed += delegate { _cheatSheet = null; };
                _cheatSheet.Show(this);
            }
            if (_cheatSheet.WindowState == FormWindowState.Minimized)
                _cheatSheet.WindowState = FormWindowState.Normal;
            _cheatSheet.Show();
            _cheatSheet.BringToFront();
            _cheatSheet.Activate();
            _cheatSheet.Focus();
        }

        void ShowAbout()
        {
            string kb = (_kb == null || string.IsNullOrEmpty(_kb.Root)) ? "(none)" : _kb.Root;
            string bits = Rp1210.HostIs32Bit ? "32-bit (correct for RP1210)" : "64-bit — rebuild with build.bat";
            string feed = Updater.DescribeManifestSource(_settings);
            UiLayout.ShowReadable(this, "About Tech Bench",
                "Tech Bench " + AppVersion.Number + "\r\n\r\n"
                + "Shop tool: knowledge-base search and Cummins INLINE 7 / J1939.\r\n\r\n"
                + "This copy: " + Application.ExecutablePath + "\r\n"
                + "Process: " + bits + "\r\n"
                + "Knowledge base: " + kb + "\r\n"
                + (_kb != null && !string.IsNullOrEmpty(_kb.Status) ? ("Index: " + _kb.Status + "\r\n") : "")
                + feed + "\r\n"
                + "\r\nUpdates read an anonymous manifest (built-in URL, update-url.txt, or update-feed-path.txt shop share) and a hashed TechBench.exe into this folder.\r\n"
                + "The app never stores a GitHub token. Updates apply after you quit, never mid-session.");
        }

        void MarkReadyFromPending()
        {
            _installUpdate.Enabled = true;
            _installUpdate.Text = UpdatePrompt.PendingMenuText;
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
                string status = UpdatePrompt.StatusWithFeed(Updater.StatusText(result),
                    Updater.DescribeManifestSource(_settings));
                MessageBox.Show(this, status, UpdatePrompt.Title, MessageBoxButtons.OK, icon);
                return;
            }

            _ready = result.Manifest;
            _installUpdate.Enabled = true;
            _installUpdate.Text = UpdatePrompt.InstallMenuText(_ready.Version);
            if (!interactive) return;

            DialogResult ask = MessageBox.Show(this,
                UpdatePrompt.AvailableQuestion(_ready.Version, AppVersion.Number, _ready.Notes),
                UpdatePrompt.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
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
                    Updater.DownloadAndStage(exeDir, _ready);
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

        void AddOrEditCode()
        {
            if (_kb == null || string.IsNullOrEmpty(_kb.Root))
            {
                MessageBox.Show(this, ShopAdd.NoKbMessage,
                    "Add code", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Hit sel = _search.Selected;
            // Pre-fill from the highlighted entry so editing an existing code is one click.
            UserCode seed = sel != null && sel.Kind == "CODE"
                ? ShopAdd.CodeSeed(ShopSync.LoadAllCodes(_kb.Root), sel.Kind, sel.Title)
                : new UserCode();
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
                ShowPage(ShellPageId.Search);
                _search.Prefill(ShopAdd.CodeQuery(dlg.Result));
            }
        }

        void AddNote()
        {
            if (_kb == null || string.IsNullOrEmpty(_kb.Root))
            {
                MessageBox.Show(this, ShopAdd.NoKbMessage,
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
                    ShowPage(ShellPageId.Search);
                    _search.Prefill(ShopAdd.NoteQuery(dlg.NoteTitle, dlg.NoteBody));
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
                MessageBox.Show(this, ShopAdd.NoKbMessage,
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
                    ShowPage(ShellPageId.Search);
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
                _shopWatcher.Restart(_kb == null ? "" : _kb.Root);
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
                _syncStatus.Text = JobStrip.SyncIdle;
                _jobSync.Text = JobStrip.SyncIdle;
                return;
            }
            _syncStatus.Text = r.Status;
            _syncStatus.ForeColor = r.Conflicts > 0 ? Color.DarkRed : Color.Black;
            _jobSync.ForeColor = r.Conflicts > 0 ? Color.OrangeRed : Color.Khaki;
            string tech = ShopSync.SanitizeTechId(ShopSync.LoadSettings().TechId);
            _jobSync.Text = JobStrip.SyncChip(r.Conflicts, tech);
            Control bar = _jobSync.Parent;
            if (bar != null) bar.PerformLayout();
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
            string tag = JobStrip.JobTag(_orders.Current, _model.Text, _serial.Text);
            _inline.JobTag = tag;
            Text = Title(tag);
        }

        void OpenShopSettings()
        {
            using (var dlg = new ShopSettingsForm(ShopSettings.Load()))
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
            int sel;
            foreach (string row in JobStrip.PickItems(_orders.Orders(), keep, out sel))
                _woPick.Items.Add(row);
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
            WorkOrder picked = JobStrip.Picked(_orders.Orders(), i);
            if (picked == null) return;
            _orders.SelectKey(picked.Key());
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
