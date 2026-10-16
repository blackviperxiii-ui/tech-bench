using System;
using System.Collections.Generic;

namespace TechBench
{
    /// <summary>
    /// What the Add code / Add note / Add file actions pre-fill and search for. No UI, no disk.
    /// </summary>
    internal static class ShopAdd
    {
        public const string NoKbMessage = "No knowledge base folder is configured, so there is nowhere to save.";

        /// <summary>Add code seed: the highlighted CODE hit's saved entry, or a blank code titled after the hit.</summary>
        public static UserCode CodeSeed(IEnumerable<UserCode> saved, string hitKind, string hitTitle)
        {
            var seed = new UserCode();
            if (hitKind != "CODE") return seed;
            if (saved != null)
            {
                foreach (UserCode existing in saved)
                {
                    if (existing == null) continue;
                    string title = existing.Brand + "  " + existing.Code + "  —  " + existing.Title;
                    if (string.Equals(title, hitTitle, StringComparison.OrdinalIgnoreCase)) { seed = existing; break; }
                }
            }
            if (!seed.IsUsable()) seed.Title = hitTitle;
            return seed;
        }

        public static string CodeQuery(UserCode code)
        {
            if (code == null) return "";
            return (code.Code + " " + code.Title).Trim();
        }

        /// <summary>The note title, else its first non-blank line.</summary>
        public static string NoteQuery(string title, string body)
        {
            if (!string.IsNullOrWhiteSpace(title)) return title;
            foreach (string raw in (body ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                if (raw.Trim().Length > 0) return raw.Trim();
            }
            return title;
        }
    }
}
