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
        readonly Inline7Control _inline;
        readonly SearchControl _search;
        readonly TabControl _tabs;
        readonly TabPage _pSearch;
        readonly ToolStripMenuItem _installUpdate;
        readonly AppSettings _settings;
        KbIndex _kb;
        UpdateManifest _ready;
        bool _checking;

        public ShellForm(KbIndex kb)
        {
            _kb = kb;
            _settings = AppSettings.Load();
            Text = Title("");
            ClientSize = new Size(1180, 800);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9.5f);
            string assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
            try
            {
                string ico = Path.Combine(assets, "app.ico");
                if (File.Exists(ico)) Icon = new Icon(ico);
            }
            catch { }

            var menu = new MenuStrip();
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

            var job = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Color.FromArgb(22, 32, 48) };
            var jobFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                Padding = new Padding(10, 9, 10, 6),
                BackColor = Color.Transparent
            };
            jobFlow.Controls.Add(JobLabel("Model"));
            _model = new TextBox { Width = 170, Margin = new Padding(0, 0, 12, 0) };
            jobFlow.Controls.Add(_model);
            jobFlow.Controls.Add(JobLabel("Serial"));
            _serial = new TextBox { Width = 230, Margin = new Padding(0, 0, 12, 0) };
            jobFlow.Controls.Add(_serial);
            var useJob = new Button { Text = "Search this job", AutoSize = true, Margin = new Padding(0, -2, 12, 0) };
            jobFlow.Controls.Add(useJob);
            var addCode = new Button { Text = "Add code to KB", AutoSize = true, Margin = new Padding(0, -2, 12, 0) };
            jobFlow.Controls.Add(addCode);
            jobFlow.Controls.Add(new Label
            {
                Text = "Service passwords are for trained use. INLINE 7: close USB-Link Explorer first.",
                ForeColor = Color.Silver,
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 0)
            });
            job.Controls.Add(jobFlow);

            _tabs = new TabControl { Dock = DockStyle.Fill };
            try
            {
                var il = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(32, 32) };
                string s = Path.Combine(assets, "search32.png");
                string i = Path.Combine(assets, "inline32.png");
                string a = Path.Combine(assets, "app32.png");
                if (File.Exists(s)) il.Images.Add("search", Image.FromFile(s));
                if (File.Exists(i)) il.Images.Add("inline", Image.FromFile(i));
                if (File.Exists(a)) il.Images.Add("app", Image.FromFile(a));
                if (il.Images.Count > 0) _tabs.ImageList = il;
            }
            catch { }

            _search = new SearchControl(kb);
            _inline = new Inline7Control();

            _pSearch = new TabPage("Search");
            _pSearch.Controls.Add(_search);
            if (HasImage("search")) _pSearch.ImageKey = "search";
            var pInline = new TabPage("INLINE 7");
            pInline.Controls.Add(_inline);
            if (HasImage("inline")) pInline.ImageKey = "inline";
            var pAdapters = new TabPage("Adapters");
            if (HasImage("app")) pAdapters.ImageKey = "app";
            pAdapters.Controls.Add(BuildAdaptersPanel());

            _tabs.TabPages.Add(_pSearch);
            _tabs.TabPages.Add(pInline);
            _tabs.TabPages.Add(pAdapters);

            // Last-added docks at the top: menu, then job strip, then tabs fill the rest.
            Controls.Add(_tabs);
            Controls.Add(job);
            Controls.Add(menu);

            if (!string.IsNullOrEmpty(_settings.Model)) _model.Text = _settings.Model;
            if (!string.IsNullOrEmpty(_settings.Serial)) _serial.Text = _settings.Serial;
            ApplyWindow(_settings);

            useJob.Click += delegate
            {
                string q = (_model.Text + " " + _serial.Text).Trim();
                _tabs.SelectedTab = _pSearch;
                _search.Prefill(q);
                SyncJob();
            };
            addCode.Click += delegate { AddOrEditCode(); };

            // TextChanged, not Leave: saving a session straight after typing the serial used to write
            // the file without the job tag because focus had never left the box.
            _model.TextChanged += delegate { SyncJob(); };
            _serial.TextChanged += delegate { SyncJob(); };

            // The INLINE 7 tab knows SPNs; the knowledge base knows the shop's own write-ups on them.
            _inline.OpenInSearch = delegate(string query)
            {
                _tabs.SelectedTab = _pSearch;
                _search.Prefill(query);
            };
            _inline.LookupCode = delegate(int spn, int fmi)
            {
                return _kb == null ? null : _kb.SpnText(spn, fmi);
            };

            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if ((e.Control && e.KeyCode == Keys.F) || e.KeyCode == Keys.F3)
                {
                    _tabs.SelectedTab = _pSearch;
                    _search.FocusQuery();
                    e.Handled = true;
                }
                else if (e.Control && e.KeyCode == Keys.N)
                {
                    AddOrEditCode();
                    e.Handled = true;
                }
            };

            FormClosing += delegate { PersistSettings(); };
            Shown += delegate
            {
                SyncJob();
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

        bool HasImage(string key)
        {
            return _tabs.ImageList != null && _tabs.ImageList.Images.ContainsKey(key);
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
            MessageBox.Show(this,
                "Tech Bench " + AppVersion.Number + "\n\n"
                + "Shop tool: knowledge-base search and Cummins INLINE 7 / J1939.\n\n"
                + "Process: " + bits + "\n"
                + "Knowledge base: " + kb + "\n"
                + (_kb != null && !string.IsNullOrEmpty(_kb.Status) ? ("Index: " + _kb.Status + "\n") : "")
                + "\nUpdates download a public latest.json and a hashed TechBench.exe.\n"
                + "The app never stores a GitHub token. Updates apply after you quit, never mid-session.",
                "About Tech Bench", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void MarkReadyFromPending()
        {
            _installUpdate.Enabled = true;
            _installUpdate.Text = "&Install pending update";
        }

        void CheckUpdates(bool interactive)
        {
            if (_checking && !interactive) return;
            _checking = true;
            string url = Updater.ManifestUrl(_settings);
            UpdateCheck result = null;
            try { result = Updater.Check(url); }
            catch (Exception ex)
            {
                result = new UpdateCheck { Error = ex.Message };
            }

            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(delegate { FinishCheck(result, interactive); }));
                return;
            }
            FinishCheck(result, interactive);
        }

        void FinishCheck(UpdateCheck result, bool interactive)
        {
            _checking = false;
            if (result == null)
            {
                if (interactive)
                    MessageBox.Show(this, "Update check failed.", "Updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!result.Ok)
            {
                if (interactive)
                    MessageBox.Show(this, result.Error, "Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!result.Newer)
            {
                if (interactive)
                    MessageBox.Show(this, "Tech Bench " + AppVersion.Number + " is current.",
                        "Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(0, 0, 0, 8)
            };
            var rescan = new Button { Text = "Rescan", AutoSize = true };
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
                foreach (UserCode existing in UserCodes.Load(_kb.Root))
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
                    List<UserCode> all = UserCodes.Upsert(UserCodes.Load(_kb.Root), dlg.Result);
                    UserCodes.Save(_kb.Root, all);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not write user-codes.json.\n\n" + ex.Message,
                        "Add code", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                ReloadKb();
                _tabs.SelectedTab = _pSearch;
                _search.Prefill((dlg.Result.Code + " " + dlg.Result.Title).Trim());
            }
        }

        void ReloadKb()
        {
            try
            {
                var fresh = new KbIndex();
                fresh.Load(_kb.Root);
                _kb = fresh;
                _search.Rebind(fresh);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Saved, but reloading the index failed.\n\n" + ex.Message,
                    "Add code", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void SyncJob()
        {
            string tag = (_model.Text + " " + _serial.Text).Trim();
            _inline.JobTag = tag;
            Text = Title(tag);
        }
    }
}
