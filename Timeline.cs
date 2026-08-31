using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace J1939Reader
{
    /// <summary>
    /// One fault's life on this hookup. "Red Stop came on" is a symptom; "5246 latched 4 s after
    /// crank at 180 RPM" is a diagnosis, and that needs the transition recorded when it happens.
    /// </summary>
    internal sealed class DtcEvent
    {
        public int Spn;
        public int Fmi;
        public string Name = "";
        public string FmiText = "";
        public int Occ;
        public DateTime FirstSeen;
        public DateTime LastSeen;
        public double RpmAtFirst = double.NaN;
        public double RpmAtLast = double.NaN;
        public bool Active;
        public int Cycles = 1;   // how many times it went inactive then came back

        public string Key { get { return Spn + "/" + Fmi; } }

        public string Duration()
        {
            TimeSpan t = LastSeen - FirstSeen;
            if (t.TotalSeconds < 1) return "<1s";
            if (t.TotalMinutes < 1) return t.TotalSeconds.ToString("0") + "s";
            return ((int)t.TotalMinutes) + "m" + t.Seconds.ToString("00") + "s";
        }

        public override string ToString()
        {
            string rpm = double.IsNaN(RpmAtFirst) ? "—" : RpmAtFirst.ToString("0");
            return string.Format("{0}  {1,-6} SPN {2,-6} FMI {3,-3} {4,-5} @{5,-5} RPM  {6}{7}",
                FirstSeen.ToString("HH:mm:ss"),
                Active ? "ON" : "cleared",
                Spn, Fmi,
                Duration(),
                rpm,
                Name.Length > 0 ? Name : FmiText,
                Cycles > 1 ? "  (x" + Cycles + ")" : "");
        }
    }

    /// <summary>Tracks DM1 comings and goings for one module.</summary>
    internal sealed class DtcTimeline
    {
        readonly Dictionary<string, DtcEvent> _byKey = new Dictionary<string, DtcEvent>();
        readonly List<DtcEvent> _order = new List<DtcEvent>();

        public int Count { get { return _order.Count; } }

        public void Clear()
        {
            _byKey.Clear();
            _order.Clear();
        }

        /// <summary>
        /// Fold a fresh DM1 list in. Codes missing from <paramref name="active"/> are marked cleared
        /// but kept, because "it was there and went away when I wiggled the connector" is the finding.
        /// </summary>
        public void Update(List<Dtc> active, DateTime now, double rpm)
        {
            var seen = new Dictionary<string, bool>();
            if (active != null)
            {
                foreach (Dtc d in active)
                {
                    string key = d.Spn + "/" + d.Fmi;
                    seen[key] = true;
                    DtcEvent e;
                    if (_byKey.TryGetValue(key, out e))
                    {
                        if (!e.Active)
                        {
                            e.Active = true;
                            e.Cycles++;
                            e.RpmAtFirst = rpm;
                            e.FirstSeen = now;
                        }
                        e.LastSeen = now;
                        e.RpmAtLast = rpm;
                        e.Occ = d.Occ;
                    }
                    else
                    {
                        e = new DtcEvent
                        {
                            Spn = d.Spn,
                            Fmi = d.Fmi,
                            Name = d.Name ?? "",
                            FmiText = d.FmiText ?? "",
                            Occ = d.Occ,
                            FirstSeen = now,
                            LastSeen = now,
                            RpmAtFirst = rpm,
                            RpmAtLast = rpm,
                            Active = true
                        };
                        _byKey[key] = e;
                        _order.Add(e);
                    }
                }
            }
            foreach (DtcEvent e in _order)
                if (e.Active && !seen.ContainsKey(e.Key)) e.Active = false;
        }

        /// <summary>Newest first — the last thing that changed is what you want to see.</summary>
        public List<DtcEvent> Recent()
        {
            var list = new List<DtcEvent>(_order);
            list.Sort(delegate(DtcEvent a, DtcEvent b)
            {
                int c = b.LastSeen.CompareTo(a.LastSeen);
                if (c != 0) return c;
                return b.FirstSeen.CompareTo(a.FirstSeen);
            });
            return list;
        }

        public List<DtcEvent> InOrder()
        {
            return new List<DtcEvent>(_order);
        }

        public string Csv(string jobTag)
        {
            var sb = new StringBuilder();
            sb.AppendLine("first_seen,last_seen,job,spn,fmi,state,cycles,occurrences,rpm_at_first,rpm_at_last,name,fmi_text");
            foreach (DtcEvent e in _order)
                sb.AppendLine(string.Join(",", new[]
                {
                    e.FirstSeen.ToString("o"),
                    e.LastSeen.ToString("o"),
                    Csvq(jobTag),
                    e.Spn.ToString(CultureInfo.InvariantCulture),
                    e.Fmi.ToString(CultureInfo.InvariantCulture),
                    e.Active ? "active" : "cleared",
                    e.Cycles.ToString(CultureInfo.InvariantCulture),
                    e.Occ.ToString(CultureInfo.InvariantCulture),
                    double.IsNaN(e.RpmAtFirst) ? "" : e.RpmAtFirst.ToString("0", CultureInfo.InvariantCulture),
                    double.IsNaN(e.RpmAtLast) ? "" : e.RpmAtLast.ToString("0", CultureInfo.InvariantCulture),
                    Csvq(e.Name),
                    Csvq(e.FmiText)
                }));
            return sb.ToString();
        }

        static string Csvq(string s)
        {
            if (s == null) s = "";
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
