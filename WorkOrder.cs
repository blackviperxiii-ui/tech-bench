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
        public bool PostedToIntelliDealer;

        public override string ToString()
        {
            string posted = PostedToIntelliDealer ? "ID" : "shop";
            return WhenUtc + "  " + Kind + "  [" + posted + "]  " + Detail;
        }
    }

    /// <summary>
    /// One WO-keyed job packet: header, notes, report, media names, shop/ID clock.
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
        public string ApiMessage = "";
        public bool ApiLogOn;
        public bool ApiSignOff;
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
            else if (ApiSignOff) sb.Append("  [signed off in ID]");
            if (string.Equals(Source, "shared", StringComparison.OrdinalIgnoreCase)) sb.Append("  (shared)");
            else if (string.Equals(Source, "gateway", StringComparison.OrdinalIgnoreCase)) sb.Append("  (ID)");
            return sb.ToString();
        }

        public void Touch()
        {
            UpdatedUtc = DateTime.UtcNow.ToString("o");
        }

        public WorkOrderClock AddClock(string kind, string detail, bool posted)
        {
            var ev = new WorkOrderClock
            {
                Kind = kind ?? "",
                WhenUtc = DateTime.UtcNow.ToString("o"),
                Detail = detail ?? "",
                PostedToIntelliDealer = posted
            };
            if (Clock == null) Clock = new List<WorkOrderClock>();
            Clock.Add(ev);
            Touch();
            return ev;
        }
    }
}
