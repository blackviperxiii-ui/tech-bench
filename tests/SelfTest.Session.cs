// Pure helpers: SessionIo.BaseName and Names.SaLabel / OverrideCount. No Desktop or disk writes.
using System.Collections.Generic;
using J1939Reader;

static partial class SelfTest
{
    [SelfTestCase("Session base name and SA label helpers", Order = 303)]
    static void SessionBaseNameTests()
    {
        // Stamp() embeds the clock, so assert the prefix and total length (yyyyMMdd_HHmmss is 15 chars).
        Eq("null tag is bare session name", SessionIo.BaseName(null).StartsWith("session_"), true);
        Eq("null tag length is session_ + stamp", SessionIo.BaseName(null).Length, "session_".Length + 15);
        Eq("empty tag is bare session name", SessionIo.BaseName("").StartsWith("session_"), true);
        Eq("whitespace tag is bare session name", SessionIo.BaseName("  \t ").StartsWith("session_"), true);
        Eq("whitespace tag length is session_ + stamp", SessionIo.BaseName("  \t ").Length, "session_".Length + 15);
        Eq("symbol-only tag falls back to blank", SessionIo.BaseName("!!!").StartsWith("session_"), true);
        Eq("symbol-only tag length is session_ + stamp", SessionIo.BaseName("!!!").Length, "session_".Length + 15);
        Eq("underscore-only tag falls back to blank", SessionIo.BaseName("___").StartsWith("session_"), true);

        string job = SessionIo.BaseName("HP450 12345");
        Eq("job tag prefix", job.StartsWith("HP450_12345_session_"), true);
        Eq("job tag length", job.Length, "HP450_12345_session_".Length + 15);

        Eq("punctuation becomes underscores", SessionIo.BaseName("a-b.c/d").StartsWith("a_b_c_d_session_"), true);
        Eq("outer whitespace trimmed", SessionIo.BaseName("  ab  ").StartsWith("ab_session_"), true);
        Eq("leading and trailing underscores trimmed", SessionIo.BaseName("--ab--").StartsWith("ab_session_"), true);
        Eq("inner run of symbols kept as underscores", SessionIo.BaseName("a!!b").StartsWith("a__b_session_"), true);

        string cut = SessionIo.BaseName(new string('A', 45));
        Eq("long tag cut to 40", cut.StartsWith(new string('A', 40) + "_session_"), true);
        Eq("long tag length", cut.Length, 40 + "_session_".Length + 15);
        Eq("exactly 40 kept", SessionIo.BaseName(new string('B', 40)).StartsWith(new string('B', 40) + "_session_"), true);

        Names.Reset();
        Eq("SaLabel 0 is ECM 1", Names.SaLabel(0), "SA 0 ECM 1");
        Eq("SaLabel unnamed is bare", Names.SaLabel(77), "SA 77");
        Eq("OverrideCount after Reset", Names.OverrideCount, 0);
        var sa = new Dictionary<int, string>();
        sa[77] = "Shop ECU";
        Names.Load(null, null, sa);
        Eq("OverrideCount after Load", Names.OverrideCount, 1);
        Eq("SaLabel uses override", Names.SaLabel(77), "SA 77 Shop ECU");
        Names.Reset();
        Eq("OverrideCount back to 0", Names.OverrideCount, 0);
        Eq("SaLabel override cleared", Names.SaLabel(77), "SA 77");
    }
}
