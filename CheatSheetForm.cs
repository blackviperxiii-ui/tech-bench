using System.Drawing;
using System.Windows.Forms;

namespace TechBench
{
    /// <summary>TB2b: Help / F1 cheat sheet (RTF loaded in a later card).</summary>
    internal sealed class CheatSheetForm : Form
    {
        public CheatSheetForm()
        {
            Text = "Tech Bench — quick reference";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(640, 480);
        }
    }
}
