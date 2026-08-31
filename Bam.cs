using System;
using System.Collections.Generic;

namespace J1939Reader
{
    /// <summary>Reassemble J1939 BAM (TP.CM 0xEC00 + TP.DT 0xEB00) into a full PGN, e.g. long DM1.</summary>
    internal sealed class BamAssembler
    {
        /// <summary>J1939 allows 750 ms between BAM packets; drop a transfer that stalls past that.</summary>
        const double StaleSeconds = 2.0;

        sealed class Bam
        {
            public int Pgn;
            public int Total;
            public int Packets;
            public byte[] Buf;
            public bool[] Seen;
            public int Got;
            public DateTime Touched;
        }

        readonly Dictionary<int, Bam> _map = new Dictionary<int, Bam>();
        readonly List<int> _drop = new List<int>();

        /// <returns>true if <paramref name="outFrame"/> is a complete message to handle.</returns>
        public bool Feed(J1939Frame f, out J1939Frame outFrame)
        {
            outFrame = f;
            if (f == null || f.Data == null) return false;

            DropStale();

            if (f.Pgn == 0xEC00 && f.Data.Length >= 8 && f.Data[0] == 0x20)
            {
                int size = f.Data[1] | (f.Data[2] << 8);
                int pkts = f.Data[3];
                int pgn = f.Data[5] | (f.Data[6] << 8) | (f.Data[7] << 16);
                if (size < 1 || size > 1785 || pkts < 1 || pkts > 255) return false;
                _map[f.Sa] = new Bam
                {
                    Pgn = pgn & 0x3FFFF,
                    Total = size,
                    Packets = pkts,
                    Buf = new byte[size],
                    Seen = new bool[pkts],
                    Got = 0,
                    Touched = DateTime.UtcNow
                };
                return false;
            }

            if (f.Pgn == 0xEB00 && f.Data.Length >= 2)
            {
                Bam b;
                if (!_map.TryGetValue(f.Sa, out b)) return false;
                int seq = f.Data[0];
                if (seq < 1 || seq > b.Packets) return false;
                // A repeated sequence number must not count toward completion, or a retransmit makes a
                // partly-filled buffer look finished and we hand up a truncated DM1.
                if (b.Seen[seq - 1]) return false;
                int off = (seq - 1) * 7;
                if (off < 0 || off >= b.Total) return false;
                int n = f.Data.Length - 1;
                if (off + n > b.Total) n = b.Total - off;
                Buffer.BlockCopy(f.Data, 1, b.Buf, off, n);
                b.Seen[seq - 1] = true;
                b.Got++;
                b.Touched = DateTime.UtcNow;
                if (b.Got < b.Packets) return false;
                outFrame = new J1939Frame { Pgn = b.Pgn, Sa = f.Sa, Da = f.Da, Data = b.Buf };
                _map.Remove(f.Sa);
                return true;
            }

            return true;
        }

        void DropStale()
        {
            if (_map.Count == 0) return;
            DateTime now = DateTime.UtcNow;
            _drop.Clear();
            foreach (KeyValuePair<int, Bam> kv in _map)
                if ((now - kv.Value.Touched).TotalSeconds > StaleSeconds) _drop.Add(kv.Key);
            foreach (int sa in _drop) _map.Remove(sa);
        }
    }
}
