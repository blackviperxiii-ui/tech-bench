// Pure helpers on WorkOrder / WorkOrderClock and UserCode / UserCodes (no UI, no disk).
using System;
using System.IO;
using TechBench;

static partial class SelfTest
{
    [SelfTestCase("WorkOrder key, tag, list label, clock line", Order = 300)]
    static void WorkOrderHelperTests()
    {
        var wo = new WorkOrder { Number = " 1234 ", Segment = " 2 " };
        Eq("Key trims and joins segment", wo.Key(), "1234-2");
        wo.Segment = "  ";
        Eq("Key with blank segment is the number", wo.Key(), "1234");
        wo.Number = "   ";
        wo.Segment = "2";
        Eq("Key with blank number is empty even with a segment", wo.Key(), "");

        var tag = new WorkOrder { Number = "1234", Segment = "1", Model = "HP450", Serial = "S77" };
        Eq("JobTag WO + unit", tag.JobTag(), "WO 1234-1  HP450 S77");
        tag.Serial = "";
        Eq("JobTag model only is trimmed", tag.JobTag(), "WO 1234-1  HP450");
        tag.Number = "";
        Eq("JobTag without a number is just the unit", tag.JobTag(), "HP450");
        tag.Model = "";
        Eq("JobTag with nothing is empty", tag.JobTag(), "");
        Eq("JobTag number only", new WorkOrder { Number = "88" }.JobTag(), "WO 88");

        Eq("ListLabel empty WO", new WorkOrder().ListLabel(), "(no number)");
        var lab = new WorkOrder { Number = "1234", Segment = "1", Customer = " Acme ", Model = "HP450", Serial = "S77" };
        Eq("ListLabel idle file WO", lab.ListLabel(), "1234-1  Acme  HP450 S77");
        lab.ClockState = "logged-on";
        Eq("ListLabel logged on", lab.ListLabel(), "1234-1  Acme  HP450 S77  [logged on]");
        lab.ClockState = "logged-off";
        Eq("ListLabel logged off", lab.ListLabel(), "1234-1  Acme  HP450 S77  [logged off]");
        lab.ClockState = "idle";
        lab.Source = "SHARED";
        Eq("ListLabel shared is case-insensitive", lab.ListLabel(), "1234-1  Acme  HP450 S77  (shared)");
        Eq("ListLabel blank customer is skipped", new WorkOrder { Number = "5", Customer = "  " }.ListLabel(), "5");

        var ev = new WorkOrderClock { Kind = "logon", WhenUtc = "2026-05-01T14:30:00.0000000Z", Detail = "at bench" };
        Eq("clock line shop", ev.ToString(), "2026-05-01T14:30:00.0000000Z  logon  [shop]  at bench");

        // Reload selection: a saved work order that no longer exists must not leave the list with
        // nothing selected for the rest of the session.
        Eq("reload keeps a current WO that still exists",
            WorkOrderControl.PickAfterReload(true, true, false, true, 3), WorkOrderControl.ReloadPick.Keep);
        Eq("reload restores the saved WO when it exists",
            WorkOrderControl.PickAfterReload(true, true, true, true, 3), WorkOrderControl.ReloadPick.Keep);
        Eq("reload falls back to the first WO when the saved one is gone",
            WorkOrderControl.PickAfterReload(true, false, true, true, 3), WorkOrderControl.ReloadPick.First);
        Eq("reload selects nothing when the current WO vanished",
            WorkOrderControl.PickAfterReload(true, false, false, true, 3), WorkOrderControl.ReloadPick.None);
        Eq("reload selects the first WO on launch with nothing saved",
            WorkOrderControl.PickAfterReload(false, false, false, false, 2), WorkOrderControl.ReloadPick.First);
        Eq("reload selects nothing after Clear job",
            WorkOrderControl.PickAfterReload(false, false, false, true, 2), WorkOrderControl.ReloadPick.None);
        Eq("reload selects nothing from an empty list",
            WorkOrderControl.PickAfterReload(true, false, true, true, 0), WorkOrderControl.ReloadPick.None);
    }

    [SelfTestCase("UserCode usable, key and path", Order = 301)]
    static void UserCodeHelperTests()
    {
        Eq("empty code is not usable", new UserCode().IsUsable(), false);
        Eq("whitespace-only is not usable", new UserCode { Code = "  ", Title = "\t" }.IsUsable(), false);
        Eq("code alone is usable", new UserCode { Code = "F68" }.IsUsable(), true);
        Eq("title alone is usable", new UserCode { Title = "Low pressure" }.IsUsable(), true);
        Eq("brand and description alone are not usable", new UserCode { Brand = "doosan", Description = "x" }.IsUsable(), false);

        Eq("KeyOf trims and lowercases", UserCodes.KeyOf(new UserCode { Brand = " Doosan ", Code = " F68-A " }), "doosan\u0001f68-a");
        Eq("KeyOf blank brand", UserCodes.KeyOf(new UserCode { Code = "X1" }), "\u0001x1");
        Eq("KeyOf is equal across case", UserCodes.KeyOf(new UserCode { Brand = "CUMMINS", Code = "f1" }) == UserCodes.KeyOf(new UserCode { Brand = "cummins", Code = "F1" }), true);
        Eq("KeyOf differs by brand", UserCodes.KeyOf(new UserCode { Brand = "a", Code = "1" }) == UserCodes.KeyOf(new UserCode { Brand = "b", Code = "1" }), false);

        string s = Path.DirectorySeparatorChar.ToString();
        Eq("PathFor under data", UserCodes.PathFor("kb"), "kb" + s + "data" + s + "user-codes.json");
        Eq("PathFor null root", UserCodes.PathFor(null), "data" + s + "user-codes.json");
    }

    [SelfTestCase("WorkOrder media path confinement", Order = 302)]
    static void WorkOrderMediaPathTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "tb-media-" + Guid.NewGuid().ToString("N"));
        string media = Path.Combine(root, "media");
        Directory.CreateDirectory(media);
        string pic = Path.Combine(media, "shot.jpg");
        File.WriteAllText(pic, "x");
        try
        {
            string ok = WorkOrderStore.ResolveMediaPath(root, "shot.jpg");
            Check("basename resolves under media",
                ok != null && string.Equals(Path.GetFullPath(pic), ok, StringComparison.OrdinalIgnoreCase),
                ok ?? "(null)");
            Eq("parent escape rejected", WorkOrderStore.ResolveMediaPath(root, ".." + Path.DirectorySeparatorChar + "secret.txt"), null);
            Eq("nested relative rejected", WorkOrderStore.ResolveMediaPath(root, "sub" + Path.DirectorySeparatorChar + "shot.jpg"), null);
            Eq("blank name rejected", WorkOrderStore.ResolveMediaPath(root, "  "), null);
            Eq("blank folder rejected", WorkOrderStore.ResolveMediaPath("", "shot.jpg"), null);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
