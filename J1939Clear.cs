using System;
using System.Collections.Generic;

namespace J1939Reader
{
    /// <summary>
    /// J1939-73 code clear (DM11 / DM3) and the DM1/DM2 refresh that follows.
    /// Shop machines have faults on the engine and on the compressor controller (SA 48);
    /// a broadcast-only clear is not enough for that second module.
    ///
    /// DM11 and DM3 are not sent as data. J1939-73 clears by a Request (PGN 59904) for the DM11 or
    /// DM3 PGN. A module asked directly answers with an Acknowledgment (PGN 59392): ACK when the
    /// codes are erased, NACK / access denied when it will not. A global request gets no answer.
    /// </summary>
    internal static class J1939Clear
    {
        public const int Dm1 = 0xFECA;
        public const int Dm2 = 0xFECB;
        public const int Dm3 = 0xFECC;
        public const int Dm11 = 0xFED3;
        public const int Request = 0xEA00;
        public const int Ack = 0xE800;
        /// <summary>PGN 65110 AT1T1I — DEF level 1761, temp 3031, low-level 5245, inducement 5246.</summary>
        public const int DefTank = 0xFE56;

        public const byte EngineSa = 0;
        public const byte CompressorSa = 48;
        public const byte Broadcast = 255;
        public const byte NullSa = 254;
        public const byte ToolSa = 0xF9;
        public const byte Priority = 6;

        /// <summary>Acknowledgment control byte (J1939-21).</summary>
        public const int AckPositive = 0;
        public const int AckNegative = 1;
        public const int AckDenied = 2;
        public const int AckBusy = 3;

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
            return Destinations(engineSa, null);
        }

        /// <summary>
        /// The shop destinations plus every module that has sent DM1/DM2 on this hookup, so a
        /// separate aftertreatment or DEF module gets its own directed clear and its own ACK.
        /// Global (255) is always last.
        /// </summary>
        public static byte[] Destinations(int engineSa, IEnumerable<int> moduleSas)
        {
            var dests = new List<byte>();
            AddUnique(dests, EngineSa);
            AddUnique(dests, UnicastDest(engineSa, EngineSa));
            AddUnique(dests, CompressorSa);
            if (moduleSas != null)
            {
                foreach (int sa in moduleSas)
                {
                    if (sa < 0 || sa > 253 || sa == ToolSa) continue;
                    AddUnique(dests, (byte)sa);
                }
            }
            AddUnique(dests, Broadcast);
            return dests.ToArray();
        }

        /// <summary>The directed (non-global) part of a destination list.</summary>
        public static List<byte> Directed(byte[] dests)
        {
            var list = new List<byte>();
            if (dests == null) return list;
            foreach (byte d in dests)
                if (d != Broadcast && d != NullSa && d != ToolSa && !list.Contains(d)) list.Add(d);
            return list;
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

        public static J1939Tx RequestTx(int requestedPgn, byte dest)
        {
            return Tx(Request, dest, RequestPayload(requestedPgn), Priority);
        }

        /// <summary>A Request for DM11 or DM3 to each destination, in order.</summary>
        public static List<J1939Tx> ClearRequestFrames(int clearPgn, IList<byte> dests)
        {
            var list = new List<J1939Tx>();
            if (dests == null) return list;
            for (int d = 0; d < dests.Count; d++)
                list.Add(RequestTx(clearPgn, dests[d]));
            return list;
        }

        /// <summary>Request the DEF/SCR tank PGN from every shop dest after a code clear.</summary>
        public static List<J1939Tx> AftertreatmentReadFrames(int engineSa)
        {
            return AftertreatmentReadFrames(Destinations(engineSa));
        }

        public static List<J1939Tx> AftertreatmentReadFrames(IList<byte> dests)
        {
            var list = new List<J1939Tx>();
            if (dests == null) return list;
            for (int d = 0; d < dests.Count; d++)
                list.Add(RequestTx(DefTank, dests[d]));
            return list;
        }

        /// <summary>Request DM1 (active) and DM2 (previously active) from every shop dest.</summary>
        public static List<J1939Tx> RefreshDmFrames(int engineSa)
        {
            return RefreshDmFrames(Destinations(engineSa));
        }

        public static List<J1939Tx> RefreshDmFrames(IList<byte> dests)
        {
            var list = new List<J1939Tx>();
            if (dests == null) return list;
            for (int d = 0; d < dests.Count; d++)
            {
                byte da = dests[d];
                list.Add(RequestTx(Dm1, da));
                list.Add(RequestTx(Dm2, da));
            }
            return list;
        }

        /// <summary>
        /// Decode an Acknowledgment (PGN 59392). Byte 1 control, byte 5 the address being answered
        /// (0xFF on pre-2006 modules), bytes 6-8 the PGN that was requested.
        /// </summary>
        public static bool TryParseAck(J1939Frame f, DateTime now, out AckReply ack)
        {
            ack = null;
            if (f == null || f.Data == null || f.Data.Length < 8) return false;
            if (J1939Decode.NormalizePgn(f.Pgn) != Ack) return false;
            int pgn = f.Data[5] | (f.Data[6] << 8) | (f.Data[7] << 16);
            ack = new AckReply
            {
                Sa = f.Sa,
                Da = f.Da,
                Control = f.Data[0],
                Address = f.Data[4],
                Pgn = J1939Decode.NormalizePgn(pgn),
                Time = now
            };
            return true;
        }

        /// <summary>
        /// DEF / SCR / DPF parameters the shop sees on T4F engines. Used to flag the codes a plain
        /// clear will not hold until the ECM has re-checked the aftertreatment.
        /// </summary>
        public static bool IsAftertreatmentSpn(int spn)
        {
            switch (spn)
            {
                case 1569:  // engine protection / DEF-empty derate
                case 1761:  // DEF tank level
                case 3031:  // DEF tank temp
                case 3216:  // AT1 intake NOx
                case 3226:  // AT1 outlet NOx
                case 3242:  // DPF intake temp
                case 3246:  // DPF outlet temp
                case 3251:  // DPF differential pressure
                case 3361:  // DEF dosing unit
                case 3363:  // DEF tank heater
                case 3364:  // DEF quality
                case 3515:  // DEF line heater
                case 3516:  // DEF concentration / temp
                case 3719:  // DPF soot load
                case 3936:  // DPF system
                case 4094:  // NOx limit exceeded, poor reagent quality
                case 4096:  // NOx limit exceeded, empty reagent tank
                case 4331:  // DEF pressure
                case 4334:  // DEF doser pressure
                case 4364:  // SCR conversion efficiency
                case 4765:  // DOC intake temp
                case 4766:  // DOC outlet temp
                case 5245:  // DEF low-level / inducement timer
                case 5246:  // SCR operator inducement severity
                case 5392:  // DEF pump
                case 5394:  // DEF pump state
                    return true;
                default:
                    return false;
            }
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

        /// <summary>How many frames carry this PGN as data (not as a Request), to any destination.</summary>
        public static int CountPgn(List<J1939Tx> frames, int pgn)
        {
            int n = 0;
            if (frames == null) return 0;
            for (int i = 0; i < frames.Count; i++)
                if (frames[i].Pgn == pgn) n++;
            return n;
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

    /// <summary>One Acknowledgment (PGN 59392) heard on the bus.</summary>
    internal sealed class AckReply
    {
        public int Sa;
        public int Da;
        public int Control;
        public int Address;
        public int Pgn;
        public DateTime Time;

        /// <summary>
        /// True when this answers a request from this tool: sent to the tool, or sent global with
        /// the tool's address in byte 5 (or 0xFF from modules older than J1939-21 2006).
        /// </summary>
        public bool ForTool
        {
            get
            {
                if (Da == J1939Clear.ToolSa) return true;
                if (Da != J1939Clear.Broadcast) return false;
                return Address == J1939Clear.ToolSa || Address == 0xFF;
            }
        }
    }
}
