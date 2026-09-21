using System;
using System.Collections.Generic;

namespace J1939Reader
{
    /// <summary>
    /// J1939-73 code clear (DM11 / DM3) and the DM1/DM2 refresh that follows.
    /// Shop machines have faults on the engine and on the compressor controller (SA 48);
    /// a broadcast-only clear is not enough for that second module.
    /// </summary>
    internal static class J1939Clear
    {
        public const int Dm1 = 0xFECA;
        public const int Dm2 = 0xFECB;
        public const int Dm3 = 0xFECC;
        public const int Dm11 = 0xFED3;
        public const int Request = 0xEA00;
        /// <summary>PGN 65110 AT1T1I — DEF level 1761, temp 3031, low-level 5245, inducement 5246.</summary>
        public const int DefTank = 0xFE56;

        public const byte EngineSa = 0;
        public const byte CompressorSa = 48;
        public const byte Broadcast = 255;
        public const byte ToolSa = 0xF9;
        public const byte Priority = 6;
        public const int Rounds = 3;

        /// <summary>
        /// Unicast destination: a real module SA, or <paramref name="fallback"/> when unknown / global.
        /// </summary>
        public static byte UnicastDest(int sa, byte fallback)
        {
            if (sa < 0 || sa > 253) return fallback;
            return (byte)sa;
        }

        /// <summary>
        /// Engine (SA 0), the detected engine SA if it is different, compressor controller (SA 48),
        /// then global (255). Directed Request (PGN 0xEA00) is PDU1, so SA 48 is not optional.
        /// </summary>
        public static byte[] Destinations(int engineSa)
        {
            var dests = new List<byte>();
            AddUnique(dests, EngineSa);
            AddUnique(dests, UnicastDest(engineSa, EngineSa));
            AddUnique(dests, CompressorSa);
            AddUnique(dests, Broadcast);
            return dests.ToArray();
        }

        static void AddUnique(List<byte> dests, byte d)
        {
            if (!dests.Contains(d)) dests.Add(d);
        }

        public static byte[] RequestPayload(int pgn)
        {
            return new byte[]
            {
                (byte)(pgn & 0xFF),
                (byte)((pgn >> 8) & 0xFF),
                (byte)((pgn >> 16) & 0xFF)
            };
        }

        public static byte[] Zeros8()
        {
            return new byte[8];
        }

        public static byte[] Ff8()
        {
            return new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        }

        public static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == b) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>RP1210 J1939 send buffer: PGN (3) + priority + tool SA + dest + data.</summary>
        public static byte[] Rp1210Message(int pgn, byte dest, byte[] data, byte priority)
        {
            int dlen = data == null ? 0 : data.Length;
            byte[] m = new byte[6 + dlen];
            m[0] = (byte)(pgn & 0xFF);
            m[1] = (byte)((pgn >> 8) & 0xFF);
            m[2] = (byte)((pgn >> 16) & 0xFF);
            m[3] = (byte)(priority & 0x07);
            m[4] = ToolSa;
            m[5] = dest;
            if (dlen > 0) Buffer.BlockCopy(data, 0, m, 6, dlen);
            return m;
        }

        public static J1939Tx Tx(int pgn, byte dest, byte[] data, byte priority)
        {
            byte[] payload = data == null ? new byte[0] : (byte[])data.Clone();
            return new J1939Tx
            {
                Pgn = pgn,
                Dest = dest,
                Priority = priority,
                Data = payload,
                Rp1210 = Rp1210Message(pgn, dest, payload, priority)
            };
        }

        /// <summary>One round of DM11 + DM3 (zeros, 0xFF, then directed Request) to every shop dest.</summary>
        public static List<J1939Tx> ClearRoundFrames(int engineSa)
        {
            var list = new List<J1939Tx>();
            byte[] dests = Destinations(engineSa);
            byte[] zeros = Zeros8();
            byte[] ffs = Ff8();
            int[] pgms = { Dm11, Dm3 };
            for (int i = 0; i < pgms.Length; i++)
            {
                int pgn = pgms[i];
                for (int d = 0; d < dests.Length; d++)
                {
                    byte da = dests[d];
                    list.Add(Tx(pgn, da, zeros, Priority));
                    list.Add(Tx(pgn, da, ffs, Priority));
                    list.Add(Tx(Request, da, RequestPayload(pgn), Priority));
                }
            }
            return list;
        }

        /// <summary>DM3 previously-active only: zeros + directed Request, then the caller refreshes DM1/DM2.</summary>
        public static List<J1939Tx> ClearPreviousFrames(int engineSa)
        {
            var list = new List<J1939Tx>();
            byte[] dests = Destinations(engineSa);
            byte[] zeros = Zeros8();
            for (int d = 0; d < dests.Length; d++)
            {
                byte da = dests[d];
                list.Add(Tx(Dm3, da, zeros, Priority));
                list.Add(Tx(Request, da, RequestPayload(Dm3), Priority));
            }
            return list;
        }

        /// <summary>Request the DEF/SCR tank PGN from every shop dest after a code clear.</summary>
        public static List<J1939Tx> AftertreatmentReadFrames(int engineSa)
        {
            var list = new List<J1939Tx>();
            byte[] dests = Destinations(engineSa);
            for (int d = 0; d < dests.Length; d++)
                list.Add(Tx(Request, dests[d], RequestPayload(DefTank), Priority));
            return list;
        }

        /// <summary>Request DM1 (active) and DM2 (previously active) from every shop dest.</summary>
        public static List<J1939Tx> RefreshDmFrames(int engineSa)
        {
            var list = new List<J1939Tx>();
            byte[] dests = Destinations(engineSa);
            for (int d = 0; d < dests.Length; d++)
            {
                byte da = dests[d];
                list.Add(Tx(Request, da, RequestPayload(Dm1), Priority));
                list.Add(Tx(Request, da, RequestPayload(Dm2), Priority));
            }
            return list;
        }

        public static int Count(List<J1939Tx> frames, int pgn, byte dest, byte[] data)
        {
            int n = 0;
            if (frames == null) return 0;
            for (int i = 0; i < frames.Count; i++)
            {
                J1939Tx tx = frames[i];
                if (tx.Pgn == pgn && tx.Dest == dest && SameBytes(tx.Data, data)) n++;
            }
            return n;
        }

        public static int CountRequest(List<J1939Tx> frames, int requestedPgn, byte dest)
        {
            return Count(frames, Request, dest, RequestPayload(requestedPgn));
        }
    }

    /// <summary>One constructed J1939 send (plan or captured from Rp1210.SendJ1939).</summary>
    internal sealed class J1939Tx
    {
        public int Pgn;
        public byte Dest;
        public byte Priority;
        public byte[] Data;
        public byte[] Rp1210;

        public int RequestedPgn
        {
            get
            {
                if (Pgn != J1939Clear.Request || Data == null || Data.Length < 3) return -1;
                return Data[0] | (Data[1] << 8) | (Data[2] << 16);
            }
        }
    }
}
