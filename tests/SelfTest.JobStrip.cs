using System;
using System.Collections.Generic;
using System.IO;
using TechBench;

static partial class SelfTest
{
    [SelfTestCase("job strip text", Order = 98)]
    static void JobStripTests()
    {
        Eq("title without a job", JobStrip.WindowTitle("1.2.34", ""), "Tech Bench 1.2.34");
        Eq("title with a job", JobStrip.WindowTitle("1.2.34", "WO 555-01"), "Tech Bench 1.2.34  ·  WO 555-01");
        Eq("title with null job", JobStrip.WindowTitle("1.2.34", null), "Tech Bench 1.2.34");

        var wo = new WorkOrder { Number = "555", Segment = "01", Model = "C185", Serial = "S123", Customer = "Acme" };
        Eq("tag from the open work order", JobStrip.JobTag(wo, "typed", "typed"), "WO 555-01  C185 S123");
        Eq("blank work order falls back to model and serial", JobStrip.JobTag(new WorkOrder { Number = "  " }, "XAS 185", "77"), "XAS 185 77");
        Eq("no work order, model only", JobStrip.JobTag(null, "XAS 185", ""), "XAS 185");
        Eq("no work order, nothing typed", JobStrip.JobTag(null, null, null), "");

        Eq("sync chip clean", JobStrip.SyncChip(0, "jeremy"), "Sync · jeremy · click");
        Eq("sync chip conflicts", JobStrip.SyncChip(3, "jeremy"), "3 sync conflict(s) — click");
        Eq("sync idle text", JobStrip.SyncIdle, "Sync: idle");

        var all = new List<WorkOrder>
        {
            wo,
            new WorkOrder { Number = "600", ClockState = "logged-on" }
        };
        int sel;
        List<string> rows = JobStrip.PickItems(all, "555-01", out sel);
        Eq("pick list rows", string.Join("|", rows.ToArray()), "(no work order)|555-01  Acme  C185 S123|600  [logged on]");
        Eq("pick list keeps the open order", sel, 1);
        JobStrip.PickItems(all, "600", out sel);
        Eq("pick list match ignores case and finds row 2", sel, 2);
        JobStrip.PickItems(new List<WorkOrder> { new WorkOrder { Number = "ab" } }, "AB", out sel);
        Eq("pick list match ignores case", sel, 1);
        JobStrip.PickItems(all, "999", out sel);
        Eq("unknown key selects no work order", sel, 0);
        JobStrip.PickItems(all, null, out sel);
        Eq("no key selects no work order", sel, 0);
        Eq("null list has only the no-work-order row", JobStrip.PickItems(null, "555-01", out sel).Count, 1);

        Eq("row 1 is the first order", JobStrip.Picked(all, 1).Number, "555");
        Eq("row 2 is the second order", JobStrip.Picked(all, 2).Number, "600");
        Eq("row 0 is no order", JobStrip.Picked(all, 0) == null, true);
        Eq("stale row is ignored", JobStrip.Picked(all, 3) == null, true);
        Eq("null list picks nothing", JobStrip.Picked(null, 1) == null, true);

        string shell = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Shell", "ShellForm.cs"));
        Check("shell uses JobStrip",
            shell.IndexOf("JobStrip.PickItems(", StringComparison.Ordinal) >= 0
            && shell.IndexOf("JobStrip.Picked(", StringComparison.Ordinal) >= 0
            && shell.IndexOf("JobStrip.JobTag(", StringComparison.Ordinal) >= 0
            && shell.IndexOf("JobStrip.SyncChip(", StringComparison.Ordinal) >= 0
            && shell.IndexOf("\"(no work order)\"", StringComparison.Ordinal) < 0, "ShellForm");
    }
}
