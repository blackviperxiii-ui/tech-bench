using System.Drawing;
using System.IO;
using System.Windows.Forms;
using J1939Reader;

namespace TechBench
{
    /// <summary>Shell Adapters page: the installed RP1210 adapters, with Rescan.</summary>
    internal sealed class AdaptersPage : Panel
    {
        readonly TextBox _box;

        public AdaptersPage()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(12);
            _box = new TextBox
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
            rescan.Click += delegate { Rescan(); };
            Rescan();
            Controls.Add(_box);
            Controls.Add(bar);
        }

        void Rescan()
        {
            _box.Text = AdapterSummary.Text(
                Path.Combine(Rp1210Api.WindowsDir(), "RP121032.INI"),
                Rp1210.HostIs32Bit,
                Rp1210.Adapters());
        }
    }
}
