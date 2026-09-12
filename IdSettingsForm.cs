using System;
using System.Drawing;
using System.Windows.Forms;

namespace TechBench
{
    /// <summary>
    /// Shop → IntelliDealer: Azure API Gateway credentials and the shop share folder.
    /// </summary>
    internal sealed class IdSettingsForm : Form
    {
        readonly TextBox _techId, _techNo, _company, _location;
        readonly TextBox _gateway, _key, _client, _secret, _token;
        readonly TextBox _share, _assigned;
        readonly TextBox _assignedPath, _logon, _logoff, _signoff, _media;

        public IdSettings Result { get; private set; }

        public IdSettingsForm(IdSettings seed)
        {
            Text = "IntelliDealer / Mobile Tech";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(740, 640);
            MinimumSize = new Size(640, 520);

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Padding = new Padding(12),
                AutoScroll = true
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            table.RowCount = 1;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label hint = UiLayout.WrapText(
                "Copy credentials from IntelliDealer → Configuration → API Gateway. "
                + "Until they are here, work orders are file-backed and sign-off is not posted to the DMS.");
            hint.ForeColor = Color.DimGray;
            table.SetColumnSpan(hint, 2);
            table.Controls.Add(hint, 0, 0);

            _techId = Row(table, "Tech name", "Windows user by default — also used as the shop-folder shard");
            _techNo = Row(table, "ID tech number", "Employee / technician number in IntelliDealer");
            _company = Row(table, "Company", "");
            _location = Row(table, "Location", "Branch / store");
            _gateway = Row(table, "Gateway URL", "https://….azure-api.net  (Azure API Gateway)");
            _key = Row(table, "Subscription key", "Ocp-Apim-Subscription-Key");
            _client = Row(table, "OAuth client id", "Optional");
            _secret = Row(table, "OAuth secret", "Optional");
            _secret.UseSystemPasswordChar = true;
            _token = Row(table, "Token URL", "Optional client-credentials token URL");
            _share = Row(table, "Shop share folder", "USB / network / extra OneDrive folder other techs use");
            _assigned = Row(table, "Assigned WO file", "Optional id-work-orders.json or .csv");
            _assignedPath = Row(table, "Assigned path", "GET path with {tech}");
            _logon = Row(table, "Log on path", "POST  {wo} {tech}");
            _logoff = Row(table, "Log off path", "POST");
            _signoff = Row(table, "Sign off path", "POST — never faked");
            _media = Row(table, "Multimedia path", "POST file  {wo}");

            if (seed != null)
            {
                _techId.Text = seed.TechId;
                _techNo.Text = seed.TechNumber;
                _company.Text = seed.Company;
                _location.Text = seed.Location;
                _gateway.Text = seed.GatewayUrl;
                _key.Text = seed.SubscriptionKey;
                _client.Text = seed.ClientId;
                _secret.Text = seed.ClientSecret;
                _token.Text = seed.TokenUrl;
                _share.Text = seed.ShareFolder;
                _assigned.Text = seed.AssignedFile;
                _assignedPath.Text = seed.AssignedPath;
                _logon.Text = seed.LogOnPath;
                _logoff.Text = seed.LogOffPath;
                _signoff.Text = seed.SignOffPath;
                _media.Text = seed.MultimediaPath;
            }

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(12, 8, 12, 12)
            };
            var browseShare = new Button { Text = "Browse share…", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
            var browseFile = new Button { Text = "Browse WO file…", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
            var save = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            browseShare.Click += delegate { BrowseFolder(_share); };
            browseFile.Click += delegate { BrowseFile(_assigned); };
            save.Click += delegate { Collect(); };
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(browseFile);
            buttons.Controls.Add(browseShare);
            AcceptButton = save;
            CancelButton = cancel;

            Controls.Add(table);
            Controls.Add(buttons);
            Shown += delegate { UiLayout.FitToWorkingArea(this); };
        }

        TextBox Row(TableLayoutPanel table, string label, string tip)
        {
            int r = table.RowCount;
            table.RowCount = r + 1;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 8, 0) }, 0, r);
            var box = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
            if (!string.IsNullOrEmpty(tip))
            {
                var tt = new ToolTip();
                tt.SetToolTip(box, tip);
            }
            table.Controls.Add(box, 1, r);
            return box;
        }

        void BrowseFolder(TextBox box)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Folder other techs can read (USB, network, or OneDrive).";
                if (!string.IsNullOrWhiteSpace(box.Text) && System.IO.Directory.Exists(box.Text))
                    dlg.SelectedPath = box.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) box.Text = dlg.SelectedPath;
            }
        }

        void BrowseFile(TextBox box)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "Work orders (*.json;*.csv)|*.json;*.csv|All files (*.*)|*.*";
                dlg.Title = "Assigned work-order list";
                if (dlg.ShowDialog(this) == DialogResult.OK) box.Text = dlg.FileName;
            }
        }

        void Collect()
        {
            Result = new IdSettings
            {
                TechId = IdSettings.Sanitize(_techId.Text),
                TechNumber = _techNo.Text.Trim(),
                Company = _company.Text.Trim(),
                Location = _location.Text.Trim(),
                GatewayUrl = _gateway.Text.Trim(),
                SubscriptionKey = _key.Text.Trim(),
                ClientId = _client.Text.Trim(),
                ClientSecret = _secret.Text.Trim(),
                TokenUrl = _token.Text.Trim(),
                ShareFolder = _share.Text.Trim(),
                AssignedFile = _assigned.Text.Trim(),
                AssignedPath = string.IsNullOrWhiteSpace(_assignedPath.Text) ? "/service/technicians/{tech}/workorders" : _assignedPath.Text.Trim(),
                LogOnPath = string.IsNullOrWhiteSpace(_logon.Text) ? "/service/workorders/{wo}/logon" : _logon.Text.Trim(),
                LogOffPath = string.IsNullOrWhiteSpace(_logoff.Text) ? "/service/workorders/{wo}/logoff" : _logoff.Text.Trim(),
                SignOffPath = string.IsNullOrWhiteSpace(_signoff.Text) ? "/service/workorders/{wo}/signoff" : _signoff.Text.Trim(),
                MultimediaPath = string.IsNullOrWhiteSpace(_media.Text) ? "/service/workorders/{wo}/attachments" : _media.Text.Trim()
            };
        }
    }
}
