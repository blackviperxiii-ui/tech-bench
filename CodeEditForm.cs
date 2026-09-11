using System;
using System.Drawing;
using System.Windows.Forms;

namespace TechBench
{
    /// <summary>
    /// Add or edit an entry in data\user-codes.json from the bench, so what a tech works out on a
    /// machine ends up in the knowledge base instead of in their head.
    /// </summary>
    internal sealed class CodeEditForm : Form
    {
        readonly TextBox _brand, _code, _title, _severity, _controller;
        readonly TextBox _description, _causes, _checks, _reset, _safety;

        public UserCode Result { get; private set; }

        public CodeEditForm(UserCode seed)
        {
            Text = "Knowledge base — add / edit a code";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(680, 620);
            MinimumSize = new Size(560, 520);

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Padding = new Padding(12),
                AutoScroll = true
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _brand = Row(table, "Brand", "DOOSAN / IR / SULLAIR — free text");
            _code = Row(table, "Code", "The code as it shows on the controller, e.g. F68");
            _title = Row(table, "Title", "One line: what the code means");
            _severity = Row(table, "Severity", "high / medium / low");
            _controller = Row(table, "Controller", "ifix / xe / etc.");
            _description = Multi(table, "Description", 70, "");
            _causes = Multi(table, "Likely causes", 90, "One per line");
            _checks = Multi(table, "Checks", 90, "One per line");
            _reset = Multi(table, "Reset notes", 60, "");
            _safety = Multi(table, "Safety", 60, "");

            if (seed != null)
            {
                _brand.Text = seed.Brand;
                _code.Text = seed.Code;
                _title.Text = seed.Title;
                _severity.Text = seed.Severity;
                _controller.Text = seed.Controller;
                _description.Text = seed.Description;
                _causes.Text = string.Join(Environment.NewLine, seed.Causes.ToArray());
                _checks.Text = string.Join(Environment.NewLine, seed.Checks.ToArray());
                _reset.Text = seed.Reset;
                _safety.Text = seed.Safety;
            }

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(12, 8, 12, 12)
            };
            var save = new Button { Text = "Save to knowledge base", AutoSize = true, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            save.Click += delegate { Collect(); };
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            AcceptButton = save;
            CancelButton = cancel;

            var note = new Label
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Height = 34,
                Padding = new Padding(12, 0, 12, 0),
                ForeColor = Color.DimGray,
                Text = "Writes data\\shop\\{you}\\user-codes.json so other techs can sync it. "
                     + "The index reloads when you save."
            };

            Controls.Add(table);
            Controls.Add(note);
            Controls.Add(buttons);
        }

        void Collect()
        {
            var c = new UserCode
            {
                Brand = _brand.Text.Trim(),
                Code = _code.Text.Trim(),
                Title = _title.Text.Trim(),
                Severity = _severity.Text.Trim(),
                Controller = _controller.Text.Trim(),
                Description = _description.Text.Trim(),
                Reset = _reset.Text.Trim(),
                Safety = _safety.Text.Trim()
            };
            c.Causes.AddRange(UserCodes.SplitLines(_causes.Text));
            c.Checks.AddRange(UserCodes.SplitLines(_checks.Text));
            if (!c.IsUsable())
            {
                MessageBox.Show(this, "Give it at least a code or a title.", "Add code",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.None;
                return;
            }
            Result = c;
        }

        TextBox Row(TableLayoutPanel table, string label, string hint)
        {
            table.Controls.Add(Caption(label), 0, table.RowCount);
            var box = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 6) };
            if (hint.Length > 0) new ToolTip().SetToolTip(box, hint);
            table.Controls.Add(box, 1, table.RowCount);
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowCount++;
            return box;
        }

        TextBox Multi(TableLayoutPanel table, string label, int height, string hint)
        {
            table.Controls.Add(Caption(label), 0, table.RowCount);
            var box = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Height = height,
                MaxLength = 0,
                Margin = new Padding(0, 2, 0, 6)
            };
            if (hint.Length > 0) new ToolTip().SetToolTip(box, hint);
            table.Controls.Add(box, 1, table.RowCount);
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowCount++;
            return box;
        }

        static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = false, Height = 22, Margin = new Padding(0, 5, 8, 0) };
        }
    }
}
