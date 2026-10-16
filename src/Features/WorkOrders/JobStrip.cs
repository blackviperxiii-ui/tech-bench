using System;
using System.Collections.Generic;
using System.IO;

namespace TechBench
{
    /// <summary>
    /// What the job strip and window title show: the job tag, the sync chip and the work-order pick list. No UI.
    /// </summary>
    internal static class JobStrip
    {
        public const string NoWorkOrder = "(no work order)";
        public const string SyncIdle = "Sync: idle";

        public static string WindowTitle(string version, string jobTag)
        {
            string head = "Tech Bench " + version;
            return string.IsNullOrEmpty(jobTag) ? head : (head + "  ·  " + jobTag);
        }

        /// <summary>The open work order's tag, else the typed model and serial.</summary>
        public static string JobTag(WorkOrder wo, string model, string serial)
        {
            if (wo != null && !string.IsNullOrWhiteSpace(wo.Number))
                return wo.JobTag();
            return ((model ?? "") + " " + (serial ?? "")).Trim();
        }

        /// <summary>What "Search this job" searches for: the typed model and serial.</summary>
        public static string SearchQuery(string model, string serial)
        {
            return ((model ?? "") + " " + (serial ?? "")).Trim();
        }

        /// <summary>Customer shown at launch: settings.json first, else the work-order page's saved job.</summary>
        public static string StartupCustomer(string saved, string savedJobCustomer)
        {
            return !string.IsNullOrEmpty(saved) ? saved : savedJobCustomer;
        }

        /// <summary>The DTC csv INLINE 7 writes beside a saved session file, or "" for no session.</summary>
        public static string DtcCsvFor(string sessionPath)
        {
            if (string.IsNullOrEmpty(sessionPath)) return "";
            return Path.Combine(Path.GetDirectoryName(sessionPath),
                Path.GetFileNameWithoutExtension(sessionPath) + "_dtcs.csv");
        }

        public static string SyncChip(int conflicts, string tech)
        {
            return conflicts > 0
                ? (conflicts + " sync conflict(s) — click")
                : ("Sync · " + tech + " · click");
        }

        /// <summary>Pick-list rows: the no-work-order row, then each order. selected is the row matching keepKey, else 0.</summary>
        public static List<string> PickItems(IList<WorkOrder> all, string keepKey, out int selected)
        {
            string keep = keepKey ?? "";
            var items = new List<string> { NoWorkOrder };
            selected = 0;
            if (all == null) return items;
            for (int i = 0; i < all.Count; i++)
            {
                items.Add(all[i].ListLabel());
                if (keep.Length > 0 && string.Equals(all[i].Key(), keep, StringComparison.OrdinalIgnoreCase))
                    selected = i + 1;
            }
            return items;
        }

        /// <summary>The work order under a pick-list row, or null for the no-work-order row or a stale index.</summary>
        public static WorkOrder Picked(IList<WorkOrder> all, int row)
        {
            int idx = row - 1;
            if (all == null || idx < 0 || idx >= all.Count) return null;
            return all[idx];
        }
    }
}
