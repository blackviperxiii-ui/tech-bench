using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace J1939Reader
{
    internal sealed class HistoryEntry
    {
        public DateTime When;
        public string Job = "";
        public string State = "";
        public int Spn;
        public int Fmi;
        public int Occ;
        public string Name = "";
        public string File = "";

        public string Key { get { return Spn + "/" + Fmi; } }
    }

    /// <summary>One fault seen across past visits for a job.</summary>
    internal sealed class HistoryFault
    {
        public int Spn;
        public int Fmi;
        public string Name = "";
        public int Visits;
        public DateTime First;
        public DateTime Last;

        public override string ToString()
        {
            string n = Name.Length > 0 ? "  " + Name : "";
            return string.Format("SPN {0,-6} FMI {1,-3} seen on {2} visit{3}   last {4:yyyy-MM-dd}{5}",
                Spn, Fmi, Visits, Visits == 1 ? "" : "s", Last, n);
        }
    }

    /// <summary>
    /// Reads back the DTC CSVs written by earlier sessions so a unit that has been here before says
    /// so. The job strip already stamps model/serial onto every saved file; this makes that pay off.
    /// </summary>
    internal sealed class History
    {
        public readonly List<HistoryEntry> Entries = new List<HistoryEntry>();
        public int FilesRead;
        public string Error = "";

        public static History Load()
        {
            return Load(SessionIo.Folder());
        }

        public static History Load(string folder)
        {
            var h = new History();
            try
            {
                if (!Directory.Exists(folder)) return h;
                string[] files = Directory.GetFiles(folder, "*_dtcs.csv");
                Array.Sort(files);
                foreach (string f in files)
                {
                    try
                    {
                        h.ReadFile(f);
                        h.FilesRead++;
                    }
                    catch { }
                }
            }
            catch (Exception ex) { h.Error = ex.Message; }
            return h;
        }

        void ReadFile(string path)
        {
            string[] lines = File.ReadAllLines(path);
            if (lines.Length < 2) return;
            List<string> header = SplitCsv(lines[0]);
            int iTime = header.IndexOf("time");
            int iJob = header.IndexOf("job");
            int iState = header.IndexOf("state");
            int iSpn = header.IndexOf("spn");
            int iFmi = header.IndexOf("fmi");
            int iOcc = header.IndexOf("occurrences");
            int iName = header.IndexOf("name");
            if (iSpn < 0 || iFmi < 0) return;
            string file = Path.GetFileName(path);
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length == 0) continue;
                List<string> c = SplitCsv(lines[i]);
                int spn, fmi;
                if (!int.TryParse(At(c, iSpn), NumberStyles.Integer, CultureInfo.InvariantCulture, out spn)) continue;
                if (!int.TryParse(At(c, iFmi), NumberStyles.Integer, CultureInfo.InvariantCulture, out fmi)) continue;
                DateTime when;
                if (!DateTime.TryParse(At(c, iTime), CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces, out when))
                    when = SafeWriteTime(path);
                int occ;
                int.TryParse(At(c, iOcc), NumberStyles.Integer, CultureInfo.InvariantCulture, out occ);
                Entries.Add(new HistoryEntry
                {
                    When = when,
                    Job = At(c, iJob),
                    State = At(c, iState),
                    Spn = spn,
                    Fmi = fmi,
                    Occ = occ,
                    Name = At(c, iName),
                    File = file
                });
            }
        }

        static DateTime SafeWriteTime(string path)
        {
            try { return File.GetLastWriteTime(path); }
            catch { return DateTime.MinValue; }
        }

        static string At(List<string> cells, int i)
        {
            return i >= 0 && i < cells.Count ? cells[i] : "";
        }

        /// <summary>Quote-aware split; job tags and fault names contain commas.</summary>
        public static List<string> SplitCsv(string line)
        {
            var cells = new List<string>();
            if (line == null) return cells;
            var sb = new StringBuilder();
            bool inQ = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQ)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQ = false;
                    }
                    else sb.Append(c);
                }
                else if (c == '"') inQ = true;
                else if (c == ',') { cells.Add(sb.ToString()); sb.Length = 0; }
                else sb.Append(c);
            }
            cells.Add(sb.ToString());
            return cells;
        }

        /// <summary>
        /// Loose match so "HP450 12345" finds sessions tagged "HP450  12345" or just the serial. Any
        /// whitespace-separated token of 3+ characters has to appear in the stored job tag.
        /// </summary>
        public static bool JobMatches(string storedJob, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            string j = (storedJob ?? "").ToLowerInvariant();
            bool any = false;
            foreach (string tok in query.ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (tok.Length < 3) continue;
                any = true;
                if (j.IndexOf(tok, StringComparison.Ordinal) < 0) return false;
            }
            return any;
        }

        public List<HistoryFault> ForJob(string job)
        {
            var byKey = new Dictionary<string, HistoryFault>();
            var visitsByKey = new Dictionary<string, Dictionary<string, bool>>();
            foreach (HistoryEntry e in Entries)
            {
                if (!JobMatches(e.Job, job)) continue;
                HistoryFault f;
                if (!byKey.TryGetValue(e.Key, out f))
                {
                    f = new HistoryFault
                    {
                        Spn = e.Spn,
                        Fmi = e.Fmi,
                        Name = e.Name,
                        First = e.When,
                        Last = e.When
                    };
                    byKey[e.Key] = f;
                    visitsByKey[e.Key] = new Dictionary<string, bool>();
                }
                if (e.Name.Length > 0) f.Name = e.Name;
                if (e.When < f.First) f.First = e.When;
                if (e.When > f.Last) f.Last = e.When;
                visitsByKey[e.Key][e.File] = true;
            }
            var list = new List<HistoryFault>();
            foreach (KeyValuePair<string, HistoryFault> kv in byKey)
            {
                kv.Value.Visits = visitsByKey[kv.Key].Count;
                list.Add(kv.Value);
            }
            list.Sort(delegate(HistoryFault a, HistoryFault b)
            {
                int c = b.Visits.CompareTo(a.Visits);
                if (c != 0) return c;
                return b.Last.CompareTo(a.Last);
            });
            return list;
        }

        /// <summary>Distinct job tags seen, most recent first.</summary>
        public List<string> Jobs()
        {
            var last = new Dictionary<string, DateTime>();
            foreach (HistoryEntry e in Entries)
            {
                if (e.Job.Trim().Length == 0) continue;
                DateTime prev;
                if (!last.TryGetValue(e.Job, out prev) || e.When > prev) last[e.Job] = e.When;
            }
            var jobs = new List<string>(last.Keys);
            jobs.Sort(delegate(string a, string b) { return last[b].CompareTo(last[a]); });
            return jobs;
        }

        public string Summary(string job)
        {
            List<HistoryFault> faults = ForJob(job);
            var sb = new StringBuilder();
            if (Error.Length > 0) sb.AppendLine("(could not read session folder: " + Error + ")");
            if (FilesRead == 0)
            {
                sb.AppendLine("No saved sessions yet.");
                sb.AppendLine();
                sb.AppendLine("Press Save on the INLINE 7 tab and this unit's faults become searchable history.");
                return sb.ToString();
            }
            if (string.IsNullOrWhiteSpace(job))
            {
                sb.AppendLine("Type a model/serial in the job strip to see that unit's history.");
                sb.AppendLine(FilesRead + " saved session file(s) indexed, " + Jobs().Count + " job(s).");
                return sb.ToString();
            }
            if (faults.Count == 0)
            {
                sb.AppendLine("Nothing on file for \"" + job + "\".");
                sb.AppendLine(FilesRead + " session file(s) indexed.");
                return sb.ToString();
            }
            sb.AppendLine("This unit has been here before — " + faults.Count + " distinct fault(s) on file:");
            sb.AppendLine();
            foreach (HistoryFault f in faults)
            {
                sb.AppendLine("  " + f);
                if (f.Visits > 1)
                    sb.AppendLine("      repeat offender — first seen " + f.First.ToString("yyyy-MM-dd"));
            }
            return sb.ToString();
        }
    }
}
