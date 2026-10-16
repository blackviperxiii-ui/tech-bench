using System;
using System.Collections.Generic;
using System.Text;

namespace TechBench
{
    public sealed class WorkOrderClock
    {
        public string Kind = "";
        public string WhenUtc = "";
        public string Detail = "";

        public override string ToString()
        {
            return WhenUtc + "  " + Kind + "  [shop]  " + Detail;
        }
    }

    /// <summary>
    /// One WO-keyed job packet: header, notes, report, media names, shop clock.
    /// </summary>
    public sealed class WorkOrder
    {
        public string Number = "";
        public string Segment = "";
        public string Customer = "";
        public string CustomerNo = "";
        public string Model = "";
        public string Serial = "";
        public string Stock = "";
        public string Description = "";
        public string AssignedTech = "";
        public string Notes = "";
        public string ReportText = "";
        public string Source = "file";
        public string ClockState = "idle";
        public string UpdatedUtc = "";
        public string Folder = "";
        public List<string> Media = new List<string>();
        public List<WorkOrderClock> Clock = new List<WorkOrderClock>();

        public string Key()
        {
            string n = (Number ?? "").Trim();
            string seg = (Segment ?? "").Trim();
            if (n.Length == 0) return "";
            return seg.Length == 0 ? n : (n + "-" + seg);
        }

        public string JobTag()
        {
            var sb = new StringBuilder();
            string k = Key();
            if (k.Length > 0) sb.Append("WO ").Append(k);
            string unit = ((Model ?? "") + " " + (Serial ?? "")).Trim();
            if (unit.Length > 0)
            {
                if (sb.Length > 0) sb.Append("  ");
                sb.Append(unit);
            }
            return sb.ToString();
        }

        public string ListLabel()
        {
            var sb = new StringBuilder();
            string k = Key();
            sb.Append(k.Length > 0 ? k : "(no number)");
            if (!string.IsNullOrWhiteSpace(Customer)) sb.Append("  ").Append(Customer.Trim());
            string unit = ((Model ?? "") + " " + (Serial ?? "")).Trim();
            if (unit.Length > 0) sb.Append("  ").Append(unit);
            if (ClockState == "logged-on") sb.Append("  [logged on]");
            else if (ClockState == "logged-off") sb.Append("  [logged off]");
            if (string.Equals(Source, "shared", StringComparison.OrdinalIgnoreCase)) sb.Append("  (shared)");
            return sb.ToString();
        }

        public void Touch()
        {
            UpdatedUtc = DateTime.UtcNow.ToString("o");
        }

        public WorkOrderClock AddClock(string kind, string detail)
        {
            var ev = new WorkOrderClock
            {
                Kind = kind ?? "",
                WhenUtc = DateTime.UtcNow.ToString("o"),
                Detail = detail ?? ""
            };
            if (Clock == null) Clock = new List<WorkOrderClock>();
            Clock.Add(ev);
            Touch();
            return ev;
        }
    }
}
