using System;
using System.IO;
using System.Windows.Forms;
using TechBench;

static partial class SelfTest
{
    [SelfTestCase("shell shortcuts and job hand-offs", Order = 99)]
    static void ShellShortcutTests()
    {
        Eq("ctrl+f searches", ShellShortcuts.For(true, Keys.F).ToString(), "FocusSearch");
        Eq("plain f does nothing", ShellShortcuts.For(false, Keys.F).ToString(), "None");
        Eq("f3 searches", ShellShortcuts.For(false, Keys.F3).ToString(), "FocusSearch");
        Eq("ctrl+f3 searches", ShellShortcuts.For(true, Keys.F3).ToString(), "FocusSearch");
        Eq("ctrl+n adds a code", ShellShortcuts.For(true, Keys.N).ToString(), "AddCode");
        Eq("plain n does nothing", ShellShortcuts.For(false, Keys.N).ToString(), "None");
        Eq("f1 opens the cheat sheet", ShellShortcuts.For(false, Keys.F1).ToString(), "CheatSheet");
        Eq("ctrl+f1 opens the cheat sheet", ShellShortcuts.For(true, Keys.F1).ToString(), "CheatSheet");
        Eq("ctrl+s is not a shell key", ShellShortcuts.For(true, Keys.S).ToString(), "None");

        Eq("search this job joins model and serial", JobStrip.SearchQuery(" XAS 185 ", "77 "), "XAS 185  77");
        Eq("search this job serial only", JobStrip.SearchQuery("", "77"), "77");
        Eq("search this job nulls", JobStrip.SearchQuery(null, null), "");

        Eq("launch customer prefers settings", JobStrip.StartupCustomer("Acme", "Old Co"), "Acme");
        Eq("launch customer falls back to the saved job", JobStrip.StartupCustomer("", "Old Co"), "Old Co");
        Eq("launch customer with neither", JobStrip.StartupCustomer(null, null), null);

        Eq("dtc csv beside the session", JobStrip.DtcCsvFor(Path.Combine("sessions", "run-1.json")),
            Path.Combine("sessions", "run-1_dtcs.csv"));
        Eq("dtc csv keeps inner dots", JobStrip.DtcCsvFor(Path.Combine("s", "a.b.json")), Path.Combine("s", "a.b_dtcs.csv"));
        Eq("dtc csv bare name", JobStrip.DtcCsvFor("run.json"), "run_dtcs.csv");
        Eq("dtc csv no session", JobStrip.DtcCsvFor(""), "");

        string shell = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Shell", "ShellForm.cs"));
        Check("shell uses ShellShortcuts and the job hand-offs",
            shell.IndexOf("ShellShortcuts.For(", StringComparison.Ordinal) >= 0
            && shell.IndexOf("JobStrip.SearchQuery(", StringComparison.Ordinal) >= 0
            && shell.IndexOf("JobStrip.StartupCustomer(", StringComparison.Ordinal) >= 0
            && shell.IndexOf("JobStrip.DtcCsvFor(", StringComparison.Ordinal) >= 0
            && shell.IndexOf("Keys.F3", StringComparison.Ordinal) < 0, "ShellForm");
    }
}
