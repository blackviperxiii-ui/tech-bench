// Pure helpers on Ini (Parse, HasSection, Get, GetInt, GetList, GetIntList). No disk.
using System.Collections.Generic;
using J1939Reader;

static partial class SelfTest
{
    [SelfTestCase("Ini parse and lookup helpers", Order = 302)]
    static void IniHelperTests()
    {
        Ini none = Ini.Parse(null);
        Eq("null text has no named section", none.HasSection("A"), false);
        Eq("null text has no sectionless keys", none.HasSection(""), false);
        Eq("null text Get is empty", none.Get("A", "k"), "");
        Ini empty = Ini.Parse("");
        Eq("empty text has no named section", empty.HasSection("A"), false);
        Eq("empty text Get is empty", empty.Get("A", "k"), "");
        Eq("empty text GetInt falls back", empty.GetInt("A", "k", 7), 7);
        Eq("empty text GetList is empty", empty.GetList("A", "k").Count, 0);
        Eq("empty text GetIntList is empty", empty.GetIntList("A", "k").Count, 0);

        Ini c = Ini.Parse("; top comment\n# hash comment\n\n   \n[S]\n; k=skipped\n# k2=skipped\na=1\n\n  ;indented=skipped\nb=2\n");
        Eq("semicolon comment line skipped", c.Get("S", "k"), "");
        Eq("hash comment line skipped", c.Get("S", "k2"), "");
        Eq("indented comment skipped", c.Get("S", "indented"), "");
        Eq("keys around comments and blanks kept", c.Get("S", "a") + "," + c.Get("S", "b"), "1,2");
        Eq("CRLF and CR line endings", Ini.Parse("[S]\r\nx=1\ry=2\r\n").Get("S", "y"), "2");

        Ini q = Ini.Parse("[S]\nq=\"hello world\"\nsp=\"  padded  \"\nlone=\"x\nempty=\"\"\none=\"\nmid=a\"b\"c\neq=a=b\nsp2 =  v  \n");
        Eq("quotes stripped", q.Get("S", "q"), "hello world");
        Eq("space inside quotes kept", q.Get("S", "sp"), "  padded  ");
        Eq("unbalanced quote kept", q.Get("S", "lone"), "\"x");
        Eq("empty quotes give empty", q.Get("S", "empty"), "");
        Eq("single quote char kept", q.Get("S", "one"), "\"");
        Eq("inner quotes kept", q.Get("S", "mid"), "a\"b\"c");
        Eq("value keeps later equals", q.Get("S", "eq"), "a=b");
        Eq("key and value trimmed", q.Get("S", "sp2"), "v");

        Ini ci = Ini.Parse("[VendorInformation]\nName=Cummins\n");
        Eq("section case-insensitive", ci.Get("vendorinformation", "Name"), "Cummins");
        Eq("key case-insensitive", ci.Get("VENDORINFORMATION", "NAME"), "Cummins");
        Eq("HasSection case-insensitive", ci.HasSection("VENDORINFORMATION"), true);
        Eq("HasSection unknown", ci.HasSection("Other"), false);
        Eq("HasSection null is the sectionless bucket", ci.HasSection(null), true);
        Eq("Get null section is sectionless", Ini.Parse("top=1\n[S]\nk=2\n").Get(null, "top"), "1");
        Eq("Get null key is empty", ci.Get("VendorInformation", null), "");
        Eq("Get missing key is empty", ci.Get("VendorInformation", "Nope"), "");
        Eq("Get missing section is empty", ci.Get("Nope", "Name"), "");

        Ini d = Ini.Parse("[A]\nx=1\ny=old\n[B]\nx=b\n[a]\ny=new\nz=3\n");
        Eq("duplicate section keeps first key", d.Get("A", "x"), "1");
        Eq("duplicate section adds key", d.Get("A", "z"), "3");
        Eq("duplicate section later key wins", d.Get("A", "y"), "new");
        Eq("other section untouched", d.Get("B", "x"), "b");
        Eq("keys before any header are in the sectionless bucket", Ini.Parse("top=1\n[S]\nk=2\n").Get("", "top"), "1");
        Eq("key lines with empty key ignored", Ini.Parse("[S]\n=v\nnoequals\nk=1\n").Get("S", ""), "");
        Eq("header without closing bracket", Ini.Parse("[Open\nk=1\n").Get("Open", "k"), "1");
        Eq("header name trimmed", Ini.Parse("[ Spaced ]\nk=1\n").Get("Spaced", "k"), "1");

        Ini n = Ini.Parse("[S]\ni=42\nneg= -5 \nbad=abc\nblank=\nbig=99999999999\n");
        Eq("GetInt parses", n.GetInt("S", "i", -1), 42);
        Eq("GetInt parses negative with spaces", n.GetInt("S", "neg", 0), -5);
        Eq("GetInt non-number falls back", n.GetInt("S", "bad", 9), 9);
        Eq("GetInt blank falls back", n.GetInt("S", "blank", 8), 8);
        Eq("GetInt overflow falls back", n.GetInt("S", "big", 6), 6);
        Eq("GetInt missing key falls back", n.GetInt("S", "none", 5), 5);
        Eq("GetInt missing section falls back", n.GetInt("Z", "i", 4), 4);

        Ini l = Ini.Parse("[S]\nlist= a , ,b,, c ,\nrates=250, 500,250,x,,500, 1000\nnone=\n");
        Eq("GetList trims and drops empties", string.Join("|", l.GetList("S", "list").ToArray()), "a|b|c");
        Eq("GetList blank value is empty", l.GetList("S", "none").Count, 0);
        Eq("GetList missing key is empty", l.GetList("S", "nope").Count, 0);
        Eq("GetIntList parses, skips non-numbers, dedupes in order", JoinInts(l.GetIntList("S", "rates")), "250|500|1000");
        Eq("GetIntList missing key is empty", l.GetIntList("S", "nope").Count, 0);
    }

    static string JoinInts(List<int> values)
    {
        var parts = new List<string>();
        foreach (int v in values) parts.Add(v.ToString());
        return string.Join("|", parts.ToArray());
    }
}
