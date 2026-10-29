// Source pins for INLINE 7 confirms, Safety/TSC1 lifecycle, crash exit, sync confirms.
// Behavior of clears/TSC1 on the wire stays in SelfTest.cs; these refuse silent UI-gate drops.
// (IntelliDealer SignOff / IdSettings pins do not apply on the beta track.)
using System;
using System.IO;
using TechBench;

static partial class SelfTest
{
    [SelfTestCase("safety UI gates (source pins)", Order = 275)]
    static void SafetyUiGateTests()
    {
        string root = FindRepoRoot();
        string inline = File.ReadAllText(Path.Combine(root, "src", "Features", "Inline7", "Inline7Control.cs"));
        string program = File.ReadAllText(Path.Combine(root, "src", "Shell", "Program.cs"));
        string bus = File.ReadAllText(Path.Combine(root, "src", "Features", "Adapters", "BusWorker.cs"));
        string sync = File.ReadAllText(Path.Combine(root, "src", "Features", "Sync", "SyncForm.cs"));
        string rp = File.ReadAllText(Path.Combine(root, "src", "Features", "Adapters", "Rp1210.cs"));

        Check("Clear previous uses YesNo + Button2",
            ContainsConfirmGate(inline, "void DoClearPrevious()"),
            "DoClearPrevious");
        Check("Reset all uses YesNo + Button2",
            ContainsConfirmGate(inline, "void DoResetAll()"),
            "DoResetAll");
        Check("Clear after repair uses YesNo + Button2",
            ContainsConfirmGate(inline, "void DoClearAfterRepair()"),
            "DoClearAfterRepair");
        Check("SetTsc refuses when Safety is off",
            inline.IndexOf("if (_chkSafe == null || !_chkSafe.Checked)", StringComparison.Ordinal) >= 0
            && inline.IndexOf("void SetTsc(int rpm)", StringComparison.Ordinal) >= 0,
            "SetTsc Safety");
        Check("Safety OFF releases TSC1 hold",
            inline.IndexOf("_chkSafe.CheckedChanged", StringComparison.Ordinal) >= 0
            && inline.IndexOf("AllowTscHold", StringComparison.Ordinal) >= 0
            && inline.IndexOf("SetTsc(0)", StringComparison.Ordinal) >= 0,
            "Safety CheckedChanged");
        Check("Disconnect clears Quiet bus toggle",
            inline.IndexOf("_chkQuietBus.Checked = false", StringComparison.Ordinal) >= 0
            && inline.IndexOf("void DoDisconnect()", StringComparison.Ordinal) >= 0,
            "DoDisconnect Quiet");

        Check("BusWorker refuses TSC1 without AllowTscHold",
            bus.IndexOf("AllowTscHold", StringComparison.Ordinal) >= 0
            && bus.IndexOf("TSC1 refused — Safety toggle is off", StringComparison.Ordinal) >= 0,
            "BusWorker AllowTscHold");

        int crashShow = program.IndexOf("form.ShowDialog()", StringComparison.Ordinal);
        int crashExit = program.IndexOf("Environment.Exit(1)", StringComparison.Ordinal);
        Check("Crash exits process after dialog",
            crashShow >= 0 && crashExit > crashShow,
            "Program.Crash Exit");

        Check("Sync conflict resolve uses YesNo + Button2",
            ContainsConfirmGate(sync, "void Resolve"),
            "SyncForm.Resolve");

        Check("ClaimToolAddress fails without SendCommand",
            rp.IndexOf("driver has no RP1210_SendCommand", StringComparison.Ordinal) >= 0
            && rp.IndexOf("return false;", StringComparison.Ordinal) >= 0
            && rp.IndexOf("announced by hand; not protected", StringComparison.Ordinal) >= 0,
            "ClaimToolAddress");
    }

    static bool ContainsConfirmGate(string src, string methodHint)
    {
        int i = src.IndexOf(methodHint, StringComparison.Ordinal);
        if (i < 0) return false;
        string window = src.Substring(i, Math.Min(2000, src.Length - i));
        return window.IndexOf("MessageBoxButtons.YesNo", StringComparison.Ordinal) >= 0
            && window.IndexOf("MessageBoxDefaultButton.Button2", StringComparison.Ordinal) >= 0;
    }
}
