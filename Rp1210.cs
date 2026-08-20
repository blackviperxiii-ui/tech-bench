using System;
using System.Runtime.InteropServices;
using System.Text;

namespace J1939Reader
{
    internal sealed class Rp1210 : IDisposable
    {
        const string Dll = @"C:\Windows\SysWOW64\CIL7R32.DLL";

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        static extern short RP1210_ClientConnect(IntPtr hwnd, short nDeviceId, string proto, int tx, int rx, short pkt);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        static extern short RP1210_ClientDisconnect(short id);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        static extern short RP1210_SendMessage(short id, byte[] m, short n, short notify, short block);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        static extern short RP1210_ReadMessage(short id, byte[] buf, short n, short block);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        static extern short RP1210_SendCommand(short cmd, short id, byte[] d, short n);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        static extern short RP1210_GetErrorMsg(short e, StringBuilder s);

        public const short CmdFiltersPass = 3;
        public const short CmdProtectAddr = 19;
        const byte OurSa = 0xF9;

        short _client = -1;
        readonly byte[] _rx = new byte[2048];

        public bool IsConnected { get { return _client >= 0 && _client < 128; } }
        public string LastError { get; private set; }
        public short DeviceId { get; private set; }
        public string Protocol { get; private set; }

        public static string ErrorText(short code)
        {
            if (code >= 0 && code < 128) return "OK";
            short e = code < 0 ? (short)(-code) : code;
            var sb = new StringBuilder(256);
            try { RP1210_GetErrorMsg(e, sb); } catch { }
            string m = sb.ToString();
            return string.IsNullOrWhiteSpace(m) ? ("error " + e) : m + " (" + e + ")";
        }

        public bool Connect()
        {
            Disconnect();
            // Device 2 (BT/virtual) worked with Explorer still running; 1 is USB INLINE 7
            short[] devices = { 2, 1, 111 };
            string[] protos = { "J1939:Baud=250", "J1939" };
            var fails = new StringBuilder();
            foreach (short dev in devices)
            {
                foreach (string proto in protos)
                {
                    short id = RP1210_ClientConnect(IntPtr.Zero, dev, proto, 0, 0, 0);
                    if (id >= 0 && id < 128)
                    {
                        _client = id;
                        DeviceId = dev;
                        Protocol = proto;
                        RP1210_SendCommand(CmdFiltersPass, _client, null, 0);
                        ClaimToolAddress();
                        LastError = null;
                        return true;
                    }
                    fails.Append(ErrorText(id) + " [dev " + dev + " " + proto + "]; ");
                }
            }
            LastError = fails.ToString();
            return false;
        }

        public void Disconnect()
        {
            if (IsConnected)
            {
                try { RP1210_ClientDisconnect(_client); } catch { }
            }
            _client = -1;
        }

        public void Dispose() { Disconnect(); }

        public bool SendJ1939(int pgn, byte dest, byte[] data)
        {
            return SendJ1939(pgn, dest, data, 6);
        }

        public bool SendJ1939(int pgn, byte dest, byte[] data, byte priority)
        {
            if (!IsConnected) return false;
            int dlen = data == null ? 0 : data.Length;
            byte[] m = new byte[6 + dlen];
            m[0] = (byte)(pgn & 0xFF);
            m[1] = (byte)((pgn >> 8) & 0xFF);
            m[2] = (byte)((pgn >> 16) & 0xFF);
            m[3] = (byte)(priority & 0x07);
            m[4] = OurSa;
            m[5] = dest;
            if (dlen > 0) Buffer.BlockCopy(data, 0, m, 6, dlen);
            short rc = RP1210_SendMessage(_client, m, (short)m.Length, 0, 1);
            if (rc != 0) { LastError = ErrorText(rc); return false; }
            return true;
        }

        /// <summary>
        /// SAE J1939 TSC1 (PGN 0) to engine SA 0. rpm 0 = release control.
        /// Must be repeated ~every 50 ms while holding or the ECM drops the request.
        /// </summary>
        public bool SendTsc1(int rpm)
        {
            byte[] d = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
            if (rpm <= 0)
            {
                d[0] = 0x00; // override disabled
            }
            else
            {
                d[0] = 0x01; // speed control
                int raw = (int)(rpm / 0.125);
                if (raw > 65534) raw = 65534;
                d[1] = (byte)(raw & 0xFF);
                d[2] = (byte)((raw >> 8) & 0xFF);
            }
            return SendJ1939(0x0000, 0, d, 3);
        }

        static readonly byte[] ToolName = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x81 };

        public void ClaimToolAddress()
        {
            if (!IsConnected) return;
            byte[] cmd = new byte[10];
            cmd[0] = OurSa;
            Buffer.BlockCopy(ToolName, 0, cmd, 1, 8);
            cmd[9] = 0;
            RP1210_SendCommand(CmdProtectAddr, _client, cmd, 10);
            SendJ1939(0xEE00, 255, ToolName, 6);
        }

        public void SendDm13(bool stopBroadcast)
        {
            // SAE J1939-73 DM13 PGN 57088 (0xDF00): J1939 network #1 stop/start broadcast
            byte[] d = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
            d[0] = stopBroadcast ? (byte)0x04 : (byte)0x01;
            SendJ1939(0xDF00, 255, d, 6);
        }

        /// <summary>
        /// Full J1939 DM11/DM3 plus best-effort UDS 0x14. Active faults whose condition is still true will come back immediately.
        /// </summary>
        public string ResetAllFaults()
        {
            if (!IsConnected) return "not connected";
            var sb = new StringBuilder();
            ClaimToolAddress();
            byte[] zeros = new byte[8];
            byte[] ffs = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
            int[] pgms = { 0xFED3, 0xFECC }; // DM11 active, DM3 previously active
            byte[] dests = { 0, 255 };
            for (int round = 0; round < 3; round++)
            {
                foreach (int pgn in pgms)
                {
                    foreach (byte da in dests)
                    {
                        SendJ1939(pgn, da, zeros, 6);
                        SendJ1939(pgn, da, ffs, 6);
                        RequestPgn(pgn, da);
                    }
                }
                System.Threading.Thread.Sleep(200);
            }
            sb.AppendLine("Sent J1939 DM11 (clear active) and DM3 (clear previously active) x3 to engine and broadcast.");
            string uds = TryUdsClear();
            if (!string.IsNullOrEmpty(uds)) sb.AppendLine(uds);
            RequestPgn(0xFECA, 0);
            RequestPgn(0xFECA, 255);
            RequestPgn(0xFECB, 0);
            RequestPgn(0xFECB, 255);
            return sb.ToString();
        }

        string TryUdsClear()
        {
            // ISO 15765-2 / UDS service 0x14 ClearDiagnosticInformation (all groups)
            short[] devices = { DeviceId, 2, 1, 141 };
            string[] protos = { "ISO15765:Baud=250", "ISO15765:Baud=250,Target=Cummins", "ISO15765" };
            short iso = -1;
            string used = null;
            foreach (short dev in devices)
            {
                foreach (string proto in protos)
                {
                    short id = RP1210_ClientConnect(IntPtr.Zero, dev, proto, 0, 0, 0);
                    if (id >= 0 && id < 128)
                    {
                        iso = id;
                        used = "dev " + dev + " " + proto;
                        break;
                    }
                }
                if (iso >= 0) break;
            }
            if (iso < 0 || iso >= 128)
                return "UDS 0x14 not sent (ISO15765 did not open on this adapter — J1939 clear still sent).";

            RP1210_SendCommand(CmdFiltersPass, iso, null, 0);
            // Physical request to ECM 0 from tool F9, and functional 0x33
            uint[] ids = { 0x18DA00F9, 0x18DB33F9 };
            foreach (uint canId in ids)
            {
                byte[] msg = new byte[11];
                msg[0] = 0x00; // 29-bit
                msg[1] = (byte)((canId >> 24) & 0xFF);
                msg[2] = (byte)((canId >> 16) & 0xFF);
                msg[3] = (byte)((canId >> 8) & 0xFF);
                msg[4] = (byte)(canId & 0xFF);
                msg[5] = 0x00;
                msg[6] = 0x14;
                msg[7] = 0xFF;
                msg[8] = 0xFF;
                msg[9] = 0xFF;
                RP1210_SendMessage(iso, msg, 10, 0, 1);
                // also PCI single-frame form
                msg[6] = 0x04;
                msg[7] = 0x14;
                msg[8] = 0xFF;
                msg[9] = 0xFF;
                msg[10] = 0xFF;
                RP1210_SendMessage(iso, msg, 11, 0, 1);
            }
            System.Threading.Thread.Sleep(250);
            try { RP1210_ClientDisconnect(iso); } catch { }
            return "Also sent UDS ClearDiagnosticInformation (0x14) on ISO15765 (" + used + ").";
        }

        public bool RequestPgn(int pgn, byte dest)
        {
            byte[] data = new byte[] {
                (byte)(pgn & 0xFF),
                (byte)((pgn >> 8) & 0xFF),
                (byte)((pgn >> 16) & 0xFF)
            };
            return SendJ1939(0xEA00, dest, data);
        }

        public bool Read(out J1939Frame frame)
        {
            frame = null;
            if (!IsConnected) return false;
            short r = RP1210_ReadMessage(_client, _rx, (short)_rx.Length, 0);
            if (r < 0) { LastError = ErrorText(r); return false; }
            if (r < 10) return false;
            int pgn = _rx[4] | (_rx[5] << 8) | (_rx[6] << 16);
            int sa = _rx[8];
            int da = _rx[9];
            int dlen = r - 10;
            byte[] data = new byte[dlen];
            if (dlen > 0) Buffer.BlockCopy(_rx, 10, data, 0, dlen);
            frame = new J1939Frame { Pgn = pgn & 0x3FFFF, Sa = sa, Da = da, Data = data };
            return true;
        }
    }

    internal sealed class J1939Frame
    {
        public int Pgn;
        public int Sa;
        public int Da; // dest
        public byte[] Data;
    }
}
