using System;
using System.Drawing;
using System.Windows.Forms;

namespace TechBench
{
    /// <summary>A free-form shop note that lives as a .txt in data\shop\{tech}\notes.</summary>
    internal sealed class NoteEditForm : Form
    {
        readonly TextBox _title;
        readonly TextBox _body;

        public string NoteTitle { get; private set; }
        public string NoteBody { get; private set; }

        public NoteEditForm()
        {
            Text = "Add a tech note";
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(480, 320);
            MinimumSize = new Size(400, 280);

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Padding = new Padding(12),
                RowCount = 2
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            table.Controls.Add(new Label { Text = "Title", AutoSize = false, Height = 22, Margin = new Padding(0, 5, 8, 0) }, 0, 0);
            _title = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 6) };
            table.Controls.Add(_title, 1, 0);

            table.Controls.Add(new Label { Text = "Note", AutoSize = false, Height = 22, Margin = new Padding(0, 5, 8, 0) }, 0, 1);
            _body = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                MaxLength = 0,
                AcceptsReturn = true
            };
            table.Controls.Add(_body, 1, 1);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Padding = new Padding(12, 8, 12, 12)
            };
            var save = new Button { Text = "Save note", AutoSize = true, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            save.Click += delegate { Collect(); };
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            AcceptButton = save;
            CancelButton = cancel;

            var hint = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                Padding = new Padding(12, 0, 12, 0),
                ForeColor = Color.DimGray,
                Text = "Saved under data\\shop\\{you}\\notes so other techs pick it up on sync."
            };

            Controls.Add(table);
            Controls.Add(hint);
            Controls.Add(buttons);
        }

        void Collect()
        {
            NoteTitle = _title.Text.Trim();
            NoteBody = _body.Text;
            if (NoteTitle.Length == 0 && string.IsNullOrWhiteSpace(NoteBody))
            {
                MessageBox.Show(this, "Give the note a title or some text.", "Add note",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.None;
            }
        }
    }
}
