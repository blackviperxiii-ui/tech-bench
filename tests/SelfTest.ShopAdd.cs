using System;
using System.Collections.Generic;
using System.IO;
using TechBench;

static partial class SelfTest
{
    [SelfTestCase("shop add prefill", Order = 252)]
    static void ShopAddTests()
    {
        var saved = new List<UserCode>
        {
            new UserCode { Brand = "doosan", Code = "F68", Title = "Low oil pressure", Description = "saved" },
            new UserCode { Brand = "cummins", Code = "1761", Title = "DEF level low" }
        };

        UserCode hit = ShopAdd.CodeSeed(saved, "CODE", "DOOSAN  F68  —  low oil pressure");
        Eq("code seed finds the saved entry ignoring case", hit.Description, "saved");
        Eq("code seed keeps the saved code", hit.Code, "F68");

        UserCode miss = ShopAdd.CodeSeed(saved, "CODE", "Doosan  F99  —  Unknown");
        Eq("unmatched code hit seeds its title", miss.Title, "Doosan  F99  —  Unknown");
        Eq("unmatched code hit has no code", miss.Code, "");

        UserCode manual = ShopAdd.CodeSeed(saved, "MANUAL", "Doosan  F68  —  Low oil pressure");
        Eq("non-code hit seeds nothing", manual.Title, "");
        Eq("null saved list seeds the hit title", ShopAdd.CodeSeed(null, "CODE", "X  1  —  Y").Title, "X  1  —  Y");

        Eq("code query joins code and title", ShopAdd.CodeQuery(new UserCode { Code = "F68", Title = "Low oil" }), "F68 Low oil");
        Eq("code query with title only", ShopAdd.CodeQuery(new UserCode { Title = "Low oil" }), "Low oil");
        Eq("code query null", ShopAdd.CodeQuery(null), "");

        Eq("note query uses the title", ShopAdd.NoteQuery("Belt chart", "line one"), "Belt chart");
        Eq("note query falls back to first non-blank line", ShopAdd.NoteQuery("", "\r\n  \r\n  Check 4-pin  \r\nmore"), "Check 4-pin");
        Eq("note query with nothing", ShopAdd.NoteQuery(null, null), null);

        string shell = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Shell", "ShellForm.cs"));
        Check("shell uses ShopAdd for prefill",
            shell.IndexOf("ShopAdd.NoteQuery(", StringComparison.Ordinal) >= 0
            && shell.IndexOf("ShopAdd.CodeSeed(", StringComparison.Ordinal) >= 0, "ShellForm");
    }
}
