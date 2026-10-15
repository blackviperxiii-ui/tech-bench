using System.Collections.Generic;
using System.Text;

namespace J1939Reader
{
    /// <summary>Text for the shell's Adapters page. Pure: callers pass what the PC reported.</summary>
    internal static class AdapterSummary
    {
        public static string Text(string iniPath, bool hostIs32Bit, List<Rp1210Api> apis)
        {
            var sb = new StringBuilder();
            sb.AppendLine("RP1210 adapters installed on this PC");
            sb.AppendLine("(read from " + iniPath + ")");
            sb.AppendLine();
            sb.AppendLine("Process: " + (hostIs32Bit ? "32-bit — correct for RP1210" : "64-bit — RP1210 drivers will NOT load; rebuild with build.bat"));
            sb.AppendLine();
            if (apis == null || apis.Count == 0)
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
    }
}
