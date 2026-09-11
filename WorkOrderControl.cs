using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TechBench
{
    /// <summary>
    /// Work-order list, packet editor (notes / photos / report), shop share, and ID clock buttons.
    /// </summary>
    internal sealed class WorkOrderControl : UserControl
    {
        readonly ListBox _list;
        readonly TextBox _number, _segment, _customer, _customerNo;
        readonly TextBox _model, _serial, _stock, _desc, _notes, _report, _clock;
        readonly ListBox _media;
        readonly Label _status;
        readonly Button _logOn, _logOff, _signOff;
        string _kbRoot;
        IdSettings _settings;
        WorkOrder _current;
        bool _filling;
        readonly List<WorkOrder> _orders = new List<WorkOrder>();

        public Action<WorkOrder> CurrentChanged;
        public Func<string> LiveReportText;

        public WorkOrder Current { get { return _current; } }

        public WorkOrderControl(string kbRoot)
        {
            _kbRoot = kbRoot;
            _settings = IdSettings.Load();
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9.5f);

            _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            _list.SelectedIndexChanged += delegate { PickFromList(); };
            var leftBar = UiLayout.WrapBar(new Padding(4, 4, 4, 4));
            leftBar.Controls.Add(Btn("New", delegate { NewOrder(); }));
            leftBar.Controls.Add(Btn("Refresh", delegate { Reload(true); }));
            leftBar.Controls.Add(Btn("From ID", delegate { PullAssigned(); }));
            var left = new Panel { Dock = DockStyle.Fill };
            left.Controls.Add(_list);
            left.Controls.Add(leftBar);

            var right = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 9,
                Padding = new Padding(8),
                AutoScroll = true
            };
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (int i = 0; i < 9; i++)
                right.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _number = Field(right, 0, 0, "WO number");
            _segment = Field(right, 0, 2, "Segment");
            _customer = Field(right, 1, 0, "Customer");
            _customerNo = Field(right, 1, 2, "Customer #");
            _model = Field(right, 2, 0, "Model");
            _serial = Field(right, 2, 2, "Serial");
            _stock = Field(right, 3, 0, "Stock");
            _desc = Field(right, 3, 2, "Complaint");

            right.Controls.Add(new Label { Text = "Notes", AutoSize = true, Margin = new Padding(0, 8, 8, 0) }, 0, 4);
            _notes = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 90, AcceptsReturn = true };
            right.SetColumnSpan(_notes, 3);
            right.Controls.Add(_notes, 1, 4);

            right.Controls.Add(new Label { Text = "Report", AutoSize = true, Margin = new Padding(0, 8, 8, 0) }, 0, 5);
            _report = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 9f),
                Height = 110
            };
            right.SetColumnSpan(_report, 3);
            right.Controls.Add(_report, 1, 5);

            right.Controls.Add(new Label { Text = "Pictures", AutoSize = true, Margin = new Padding(0, 8, 8, 0) }, 0, 6);
            _media = new ListBox { Dock = DockStyle.Fill, Height = 70, IntegralHeight = false };
            right.SetColumnSpan(_media, 3);
            right.Controls.Add(_media, 1, 6);
            _media.DoubleClick += delegate { OpenMedia(); };

            right.Controls.Add(new Label { Text = "Clock", AutoSize = true, Margin = new Padding(0, 8, 8, 0) }, 0, 7);
            _clock = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Height = 70,
                Font = new Font("Consolas", 9f)
            };
            right.SetColumnSpan(_clock, 3);
            right.Controls.Add(_clock, 1, 7);

            _status = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 8, 0, 0)
            };
            right.SetColumnSpan(_status, 4);
            right.Controls.Add(_status, 0, 8);

            var bar = UiLayout.WrapBar(new Padding(8, 6, 8, 4));
            bar.Controls.Add(Btn("Save", delegate { SaveCurrent(); }));
            bar.Controls.Add(Btn("Add photo", delegate { AddPhoto(); }));
            bar.Controls.Add(Btn("Attach report", delegate { AttachLiveReport(); }));
            bar.Controls.Add(Btn("Share with shop", delegate { ShareCurrent(); }));
            _logOn = Btn("Log on", delegate { ShopClock("logon"); });
            _logOff = Btn("Log off", delegate { ShopClock("logoff"); });
            _signOff = Btn("Sign off", delegate { SignOff(); });
            bar.Controls.Add(_logOn);
            bar.Controls.Add(_logOff);
            bar.Controls.Add(_signOff);
            bar.Controls.Add(Btn("Settings", delegate { OpenSettings(); }));

            var rightHost = new Panel { Dock = DockStyle.Fill };
            rightHost.Controls.Add(right);
            rightHost.Controls.Add(bar);

            var split = UiLayout.Split(Orientation.Vertical, 280, 180, 280, left, rightHost);
            Controls.Add(split);
            Reload(false);
        }

        public void SetKbRoot(string kbRoot)
        {
            _kbRoot = kbRoot;
        }

        public void ReloadSettings()
        {
            _settings = IdSettings.Load();
            Reload(false);
        }

        public WorkOrder SelectKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return _current;
            for (int i = 0; i < _orders.Count; i++)
            {
                if (string.Equals(_orders[i].Key(), key, StringComparison.OrdinalIgnoreCase))
                {
                    _list.SelectedIndex = i;
                    return _current;
                }
            }
            return _current;
        }

        public List<WorkOrder> Orders()
        {
            return new List<WorkOrder>(_orders);
        }

        public void ApplyHeader(string model, string serial, string customer)
        {
            if (_current == null) return;
            _current.Model = model ?? "";
            _current.Serial = serial ?? "";
            if (!string.IsNullOrWhiteSpace(customer)) _current.Customer = customer;
            if (!_filling)
            {
                _filling = true;
                _model.Text = _current.Model;
                _serial.Text = _current.Serial;
                _customer.Text = _current.Customer;
                _filling = false;
            }
        }

        public void AttachShot(string path)
        {
            if (_current == null || string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                WorkOrderStore.AttachFile(_current, path, _settings, _kbRoot);
                ShowCurrent();
                Status("Attached shot " + Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                Status("Could not attach shot: " + ex.Message);
            }
        }

        public void AttachReportText(string text)
        {
            if (_current == null || string.IsNullOrEmpty(text)) return;
            _current.ReportText = text;
            try
            {
                WorkOrderStore.AttachText(_current, "diagnostic-report.txt", text, _settings, _kbRoot);
                WorkOrderStore.Save(_current, _settings, _kbRoot);
                ShowCurrent();
                Status("Diagnostic report attached to WO " + _current.Key());
            }
            catch (Exception ex)
            {
                Status("Could not attach report: " + ex.Message);
            }
        }

        public void Reload(bool fromDisk)
        {
            string keep = _current == null ? "" : _current.Key();
            if (fromDisk) _settings = IdSettings.Load();
            _orders.Clear();
            _orders.AddRange(WorkOrderStore.ListAll(_settings, _kbRoot));
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (WorkOrder wo in _orders) _list.Items.Add(wo.ListLabel());
            _list.EndUpdate();
            if (keep.Length > 0) SelectKey(keep);
            else if (_orders.Count > 0) _list.SelectedIndex = 0;
            else
            {
                _current = null;
                ClearFields();
                RaiseChanged();
            }
            PaintGatewayStatus();
        }

        Button Btn(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, Margin = new Padding(0, 0, 8, 4) };
            b.Click += click;
            return b;
        }

        TextBox Field(TableLayoutPanel t, int row, int col, string label)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 8, 0) }, col, row);
            var box = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 8, 0) };
            t.Controls.Add(box, col + 1, row);
            return box;
        }

        void PickFromList()
        {
            if (_filling) return;
            Flush();
            if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _orders.Count)
            {
                _current = null;
                ClearFields();
                RaiseChanged();
                return;
            }
            _current = _orders[_list.SelectedIndex];
            ShowCurrent();
            RaiseChanged();
        }

        void ShowCurrent()
        {
            _filling = true;
            WorkOrder wo = _current;
            if (wo == null) { _filling = false; ClearFields(); return; }
            _number.Text = wo.Number;
            _segment.Text = wo.Segment;
            _customer.Text = wo.Customer;
            _customerNo.Text = wo.CustomerNo;
            _model.Text = wo.Model;
            _serial.Text = wo.Serial;
            _stock.Text = wo.Stock;
            _desc.Text = wo.Description;
            _notes.Text = wo.Notes;
            _report.Text = wo.ReportText ?? "";
            _media.Items.Clear();
            if (wo.Media != null)
                foreach (string m in wo.Media) _media.Items.Add(m);
            _clock.Text = ClockText(wo);
            _filling = false;
            PaintGatewayStatus();
        }

        void ClearFields()
        {
            _filling = true;
            _number.Text = _segment.Text = _customer.Text = _customerNo.Text = "";
            _model.Text = _serial.Text = _stock.Text = _desc.Text = "";
            _notes.Text = _report.Text = _clock.Text = "";
            _media.Items.Clear();
            _filling = false;
        }

        void Flush()
        {
            if (_current == null) return;
            _current.Number = _number.Text.Trim();
            _current.Segment = _segment.Text.Trim();
            _current.Customer = _customer.Text.Trim();
            _current.CustomerNo = _customerNo.Text.Trim();
            _current.Model = _model.Text.Trim();
            _current.Serial = _serial.Text.Trim();
            _current.Stock = _stock.Text.Trim();
            _current.Description = _desc.Text.Trim();
            _current.Notes = _notes.Text;
        }

        void SaveCurrent()
        {
            Flush();
            if (_current == null)
            {
                Status("Nothing to save — New, or pick a work order.");
                return;
            }
            try
            {
                WorkOrderStore.Save(_current, _settings, _kbRoot);
                Status("Saved WO " + _current.Key());
                Reload(false);
                SelectKey(_current.Key());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Work order", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void NewOrder()
        {
            Flush();
            var wo = new WorkOrder
            {
                Number = "LOCAL-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"),
                AssignedTech = _settings.TechId,
                Source = "file",
                Description = ""
            };
            _orders.Add(wo);
            _list.Items.Add(wo.ListLabel());
            _list.SelectedIndex = _orders.Count - 1;
            _number.Focus();
            Status("New local work order. Replace LOCAL-… with the real WO number when you have it.");
        }

        void AddPhoto()
        {
            if (_current == null) { Status("Pick a work order first."); return; }
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "Pictures (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*";
                dlg.Title = "Attach photo or screenshot";
                dlg.Multiselect = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    foreach (string f in dlg.FileNames)
                    {
                        string dest = WorkOrderStore.AttachFile(_current, f, _settings, _kbRoot);
                        if (_settings.GatewayConfigured() && dest != null)
                        {
                            IdCallResult posted = IdGateway.PostMultimedia(_settings, _current, dest);
                            _current.ApiMessage = posted.Message;
                            if (posted.Posted)
                                _current.AddClock("multimedia", Path.GetFileName(dest), true);
                            else
                                _current.AddClock("multimedia", posted.Message, false);
                        }
                    }
                    WorkOrderStore.Save(_current, _settings, _kbRoot);
                    ShowCurrent();
                    Status("Attached " + dlg.FileNames.Length + " file(s).");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Photo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        void AttachLiveReport()
        {
            if (_current == null) { Status("Pick a work order first."); return; }
            string text = LiveReportText == null ? _current.ReportText : LiveReportText();
            if (string.IsNullOrEmpty(text))
            {
                Status("No diagnostic report yet — open INLINE 7, then Report or Attach report.");
                return;
            }
            AttachReportText(text);
        }

        void ShareCurrent()
        {
            Flush();
            if (_current == null) { Status("Pick a work order first."); return; }
            try
            {
                string dest = WorkOrderStore.Share(_current, _settings, _kbRoot);
                Status("Shared WO " + _current.Key() + " → " + dest);
                Reload(false);
                SelectKey(_current.Key());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Share", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        void ShopClock(string kind)
        {
            Flush();
            if (_current == null) { Status("Pick a work order first."); return; }
            bool logon = kind == "logon";
            _current.ClockState = logon ? "logged-on" : "logged-off";
            _current.ApiLogOn = false;
            IdCallResult api = logon ? IdGateway.LogOn(_settings, _current) : IdGateway.LogOff(_settings, _current);
            _current.ApiMessage = api.Message;
            if (api.Posted)
            {
                _current.ApiLogOn = logon;
                _current.AddClock(kind, api.Message, true);
            }
            else
            {
                _current.AddClock("shop-" + kind,
                    "Shop record only. " + api.Message, false);
            }
            try { WorkOrderStore.Save(_current, _settings, _kbRoot); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Clock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ShowCurrent();
            RaiseChanged();
            Status(api.Posted ? api.Message : ("Shop " + kind + " saved. " + api.Message));
        }

        void SignOff()
        {
            Flush();
            if (_current == null) { Status("Pick a work order first."); return; }
            IdCallResult api = IdGateway.SignOff(_settings, _current);
            _current.ApiMessage = api.Message;
            if (!api.Posted)
            {
                _current.ApiSignOff = false;
                _current.AddClock("signoff-blocked", api.Message, false);
                try { WorkOrderStore.Save(_current, _settings, _kbRoot); }
                catch { }
                ShowCurrent();
                MessageBox.Show(this, api.Message, "Sign off", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _current.ApiSignOff = true;
            _current.ClockState = "signed-off";
            _current.AddClock("signoff", api.Message, true);
            try { WorkOrderStore.Save(_current, _settings, _kbRoot); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Sign off", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ShowCurrent();
            RaiseChanged();
            Status(api.Message);
        }

        void PullAssigned()
        {
            IdCallResult api = IdGateway.FetchAssigned(_settings);
            if (!api.Posted)
            {
                MessageBox.Show(this, api.Message, "Assigned work orders", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Status(api.Message);
                return;
            }
            int n = 0;
            foreach (WorkOrder wo in api.WorkOrders)
            {
                try
                {
                    WorkOrderStore.Save(wo, _settings, _kbRoot);
                    n++;
                }
                catch { }
            }
            Reload(false);
            Status("Saved " + n + " assigned work order(s) from IntelliDealer.");
        }

        void OpenSettings()
        {
            using (var dlg = new IdSettingsForm(_settings))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result == null) return;
                dlg.Result.Save();
                _settings = IdSettings.Load();
                Reload(false);
            }
        }

        void OpenMedia()
        {
            if (_current == null || _media.SelectedItem == null || string.IsNullOrEmpty(_current.Folder)) return;
            string path = Path.Combine(_current.Folder, "media", _media.SelectedItem.ToString());
            if (!File.Exists(path)) return;
            try { System.Diagnostics.Process.Start(path); }
            catch (Exception ex) { Status(ex.Message); }
        }

        static string ClockText(WorkOrder wo)
        {
            if (wo == null || wo.Clock == null || wo.Clock.Count == 0)
                return "No clock events yet. Log on writes a shop timestamp. Sign off waits for IntelliDealer.";
            var sb = new System.Text.StringBuilder();
            foreach (WorkOrderClock ev in wo.Clock)
                sb.AppendLine(ev.ToString());
            return sb.ToString();
        }

        void PaintGatewayStatus()
        {
            bool live = _settings != null && _settings.GatewayConfigured();
            _signOff.Enabled = true;
            string line;
            if (live)
                line = "API Gateway: configured (" + _settings.GatewayUrl + "). Sign-off still requires a 2xx from ID.";
            else
                line = "API Gateway: not configured — file-backed WOs, shop share, and shop clock only. Sign-off is not posted to IntelliDealer.";
            if (_current != null && !string.IsNullOrWhiteSpace(_current.ApiMessage))
                line = line + "  " + _current.ApiMessage;
            _status.Text = line;
        }

        void Status(string text)
        {
            _status.Text = text;
        }

        void RaiseChanged()
        {
            if (CurrentChanged != null) CurrentChanged(_current);
        }
    }
}
