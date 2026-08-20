using System;
using System.Collections.Generic;

namespace J1939Reader
{
    /// <summary>Reassemble J1939 BAM (TP.CM 0xEC00 + TP.DT 0xEB00) into a full PGN, e.g. long DM1.</summary>
    internal sealed class BamAssembler
    {
        sealed class Bam
        {
            public int Pgn;
            public int Total;
            public int Packets;
            public byte[] Buf;
            public int Got;
        }

        readonly Dictionary<int, Bam> _map = new Dictionary<int, Bam>();

        /// <returns>true if <paramref name="outFrame"/> is a complete message to handle.</returns>
        public bool Feed(J1939Frame f, out J1939Frame outFrame)
        {
            outFrame = f;
            if (f == null || f.Data == null) return false;

            if (f.Pgn == 0xEC00 && f.Data.Length >= 8 && f.Data[0] == 0x20)
            {
                int size = f.Data[1] | (f.Data[2] << 8);
                int pkts = f.Data[3];
                int pgn = f.Data[5] | (f.Data[6] << 8) | (f.Data[7] << 16);
                if (size < 1 || size > 1785 || pkts < 1) return false;
                _map[f.Sa] = new Bam { Pgn = pgn & 0x3FFFF, Total = size, Packets = pkts, Buf = new byte[size], Got = 0 };
                return false;
            }

            if (f.Pgn == 0xEB00 && f.Data.Length >= 2)
            {
                Bam b;
                if (!_map.TryGetValue(f.Sa, out b)) return false;
                int seq = f.Data[0];
                int off = (seq - 1) * 7;
                if (off < 0 || off >= b.Total) return false;
                int n = f.Data.Length - 1;
                if (off + n > b.Total) n = b.Total - off;
                Buffer.BlockCopy(f.Data, 1, b.Buf, off, n);
                b.Got++;
                if (b.Got < b.Packets) return false;
                outFrame = new J1939Frame { Pgn = b.Pgn, Sa = f.Sa, Da = f.Da, Data = b.Buf };
                _map.Remove(f.Sa);
                return true;
            }

            return true;
        }
    }
}
