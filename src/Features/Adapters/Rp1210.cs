using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace J1939Reader
{
    /// <summary>
    /// RP1210 client. The DLL is resolved and loaded at runtime rather than bound by DllImport so
    /// that (a) a bench PC without the drivers gets a message instead of a DllNotFoundException, and
    /// (b) any installed vendor adapter can be selected, not just the one hardcoded Cummins path.
    /// </summary>
    internal sealed class Rp1210 : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        delegate short DClientConnect(IntPtr hwnd, short nDeviceId, string proto, int tx, int rx, short pkt);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate short DClientDisconnect(short id);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate short DSendMessage(short id, byte[] m, short n, short notify, short block);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate short DReadMessage(short id, byte[] buf, short n, short block);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate short DSendCommand(short cmd, short id, byte[] d, short n);
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        delegate short DGetErrorMsg(short e, StringBuilder s);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, BestFitMapping = false)]
        static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, BestFitMapping = false)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool FreeLibrary(IntPtr module);

        public const short CmdFiltersPass = 3;
        public const short CmdProtectAddr = 19;
        const byte OurSa = J1939Clear.ToolSa;

        // RP1210C errors that mean the adapter no longer owns the tool's J1939 address.
        const int ErrAddressClaimFailed = 146;
        const int ErrCouldNotTxAddressClaimed = 152;
        const int ErrAddressLost = 153;
        const int ErrAddressNeverClaimed = 157;

        /// <summary>
        /// When set, SendJ1939 records each constructed RP1210 payload and succeeds with no adapter.
        /// SelfTest uses this. Production leaves it null.
        /// </summary>
        public List<J1939Tx> Capture;

        /// <summary>
        /// SelfTest seam. While Capture is set and the adapter is not connected, the next
        /// FailSendsForTest SendJ1939 calls return false. They are still recorded.
        /// </summary>
        internal int FailSendsForTest;

        IntPtr _module = IntPtr.Zero;
        DClientConnect _connect;
        DClientDisconnect _disconnect;
        DSendMessage _send;
        DReadMessage _read;
        DSendCommand _command;
        DGetErrorMsg _errMsg;

        short _client = -1;
        readonly byte[] _rx = new byte[2048];
        readonly object _gate = new object();

        public Rp1210Api Api { get; private set; }
        public bool IsConnected { get { return _client >= 0 && _client < 128; } }
        public bool IsLoaded { get { return _module != IntPtr.Zero; } }
        public string LastError { get; private set; }
        /// <summary>Outcome of the last Protect_J1939_Address, for the log. Requests go out from this address.</summary>
        public string ClaimStatus { get; private set; }
        public short DeviceId { get; private set; }
        public string Protocol { get; private set; }

        /// <summary>Adapters this PC actually has installed, INLINE 7 first.</summary>
        public static List<Rp1210Api> Adapters()
        {
            List<Rp1210Api> apis = Rp1210Api.Installed();
            if (apis.Count == 0)
            {
                Rp1210Api legacy = Rp1210Api.LegacyInline7();
                if (legacy.DllPath.Length > 0) apis.Add(legacy);
            }
            apis.Sort(delegate(Rp1210Api a, Rp1210Api b)
            {
                int ra = a.LooksLikeInline7() ? 0 : 1;
                int rb = b.LooksLikeInline7() ? 0 : 1;
                if (ra != rb) return ra - rb;
                return string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
            });
            return apis;
        }

        public static bool HostIs32Bit { get { return IntPtr.Size == 4; } }

        public string ErrorText(short code)
        {
            if (code >= 0 && code < 128) return "OK";
            short e = code < 0 ? (short)(-code) : code;
            if (_errMsg != null)
            {
                var sb = new StringBuilder(256);
                try
                {
                    _errMsg(e, sb);
                    string m = sb.ToString();
                    if (!string.IsNullOrWhiteSpace(m)) return m + " (" + e + ")";
                }
                catch { }
            }
            return "error " + e;
        }

        public bool Load(Rp1210Api api, out string why)
        {
            Unload();
            why = null;
            if (api == null) { why = "No RP1210 adapter selected."; return false; }
            if (!HostIs32Bit)
            {
                why = "This build is 64-bit. RP1210 drivers are 32-bit only — rebuild with /platform:x86 (build.bat already does).";
                return false;
            }
            string dll = api.DllPath;
            if (string.IsNullOrEmpty(dll) || !SafeExists(dll))
            {
                why = "Driver DLL for " + api.Id + " not found.\n\nLooked in " +
                      string.Join(", ", Rp1210Api.DllSearchDirs().ToArray()) +
                      ".\n\nInstall the adapter's RP1210 drivers on this PC.";
                return false;
            }
            IntPtr h = LoadLibrary(dll);
            if (h == IntPtr.Zero)
            {
                why = "Windows could not load " + dll + " (error " + Marshal.GetLastWin32Error() +
                      "). Its own dependencies may be missing — reinstall the adapter drivers.";
                return false;
            }
            _module = h;
            _connect = (DClientConnect)Bind("RP1210_ClientConnect", typeof(DClientConnect));
            _disconnect = (DClientDisconnect)Bind("RP1210_ClientDisconnect", typeof(DClientDisconnect));
            _send = (DSendMessage)Bind("RP1210_SendMessage", typeof(DSendMessage));
            _read = (DReadMessage)Bind("RP1210_ReadMessage", typeof(DReadMessage));
            _command = (DSendCommand)Bind("RP1210_SendCommand", typeof(DSendCommand));
            _errMsg = (DGetErrorMsg)Bind("RP1210_GetErrorMsg", typeof(DGetErrorMsg));
            if (_connect == null || _disconnect == null || _send == null || _read == null)
            {
                why = dll + " loaded but is missing the RP1210 entry points. It may not be an RP1210 driver.";
                Unload();
                return false;
            }
            Api = api;
            return true;
        }

        static bool SafeExists(string path)
        {
            try { return File.Exists(path); }
            catch { return false; }
        }

        Delegate Bind(string name, Type type)
        {
            IntPtr p = GetProcAddress(_module, name);
            if (p == IntPtr.Zero) return null;
            try { return Marshal.GetDelegateForFunctionPointer(p, type); }
            catch { return null; }
        }

        void Unload()
        {
            _connect = null; _disconnect = null; _send = null;
            _read = null; _command = null; _errMsg = null;
            if (_module != IntPtr.Zero)
            {
                try { FreeLibrary(_module); } catch { }
                _module = IntPtr.Zero;
            }
        }

        public bool Connect(Rp1210Api api)
        {
            Disconnect();
            string why;
            if (api != Api || !IsLoaded)
            {
                if (!Load(api, out why)) { LastError = why; return false; }
            }
            var fails = new StringBuilder();
            try
            {
                foreach (int dev in Api.DeviceIds())
                {
                    foreach (string proto in Api.J1939Protocols)
                    {
                        short id = _connect(IntPtr.Zero, (short)dev, proto, 0, 0, 0);
                        if (id >= 0 && id < 128)
                        {
                            _client = id;
                            DeviceId = (short)dev;
                            Protocol = proto;
                            SafeCommand(CmdFiltersPass, null, 0);
                            ClaimToolAddress();
                            LastError = null;
                            return true;
                        }
                        fails.Append(ErrorText(id) + " [dev " + dev + " " + proto + "]; ");
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = "RP1210 driver call failed: " + ex.Message;
                return false;
            }
            LastError = fails.Length > 0 ? fails.ToString() : "No device on this adapter accepted a J1939 connection.";
            return false;
        }

        public void Disconnect()
        {
            if (IsConnected && _disconnect != null)
            {
                try { _disconnect(_client); } catch { }
            }
            _client = -1;
        }

        public void Dispose()
        {
            Disconnect();
            Unload();
        }

        /// <summary>RP1210_SendCommand; returns the driver's code, or short.MinValue if it threw / is missing.</summary>
        short SafeCommand(short cmd, byte[] data, short len)
        {
            if (_command == null) return short.MinValue;
            try { return _command(cmd, _client, data, len); }
            catch (Exception ex)
            {
                LastError = "command " + cmd + " failed: " + ex.Message;
                return short.MinValue;
            }
        }

        static bool IsAddressError(short rc)
        {
            int e = rc < 0 ? -rc : rc;
            return e == ErrAddressClaimFailed || e == ErrCouldNotTxAddressClaimed
                || e == ErrAddressLost || e == ErrAddressNeverClaimed;
        }

        public bool SendJ1939(J1939Tx tx)
        {
            if (tx == null) return false;
            return SendJ1939(tx.Pgn, tx.Dest, tx.Data, tx.Priority);
        }

        public bool SendJ1939(int pgn, byte dest, byte[] data)
        {
            return SendJ1939(pgn, dest, data, 6);
        }

        public bool SendJ1939(int pgn, byte dest, byte[] data, byte priority)
        {
            byte[] m = J1939Clear.Rp1210Message(pgn, dest, data, priority);
            if (Capture != null)
                Capture.Add(J1939Clear.Tx(pgn, dest, data, priority));
            if (!IsConnected || _send == null)
            {
                if (Capture != null && FailSendsForTest > 0)
                {
                    FailSendsForTest--;
                    return false;
                }
                return Capture != null;
            }
            short rc = SendRaw(m);
            // The adapter drops a claimed address on bus-off or when another tool takes it, and then
            // refuses every request. Claim it back once instead of failing the whole code clear.
            if (rc != 0 && rc != short.MinValue && IsAddressError(rc) && pgn != 0xEE00)
            {
                if (ClaimToolAddress()) rc = SendRaw(m);
            }
            if (rc == short.MinValue) return false;
            if (rc != 0) { LastError = ErrorText(rc); return false; }
            return true;
        }

        short SendRaw(byte[] m)
        {
            try { return _send(_client, m, (short)m.Length, 0, 1); }
            catch (Exception ex) { LastError = "send failed: " + ex.Message; return short.MinValue; }
        }

        /// <summary>
        /// SAE J1939 TSC1 (PGN 0) to the engine that is broadcasting EEC1. rpm 0 = release control.
        /// Must be repeated ~every 50 ms while holding or the ECM drops the request.
        /// Destination is the engine source address — industrial controllers are often not SA 0.
        /// </summary>
        public bool SendTsc1(int rpm)
        {
            return SendTsc1(rpm, 0);
        }

        public bool SendTsc1(int rpm, int engineSa)
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
            return SendJ1939(0x0000, Tsc1Dest(engineSa), d, 3);
        }

        /// <summary>TSC1 destination: the detected engine SA, falling back to ECM 1 (0).</summary>
        public static byte Tsc1Dest(int engineSa)
        {
            return J1939Clear.UnicastDest(engineSa, J1939Clear.EngineSa);
        }

        /// <summary>
        /// J1939 NAME, LSB first: function 129 (off-board diagnostic-service tool), industry group 0,
        /// arbitrary-address capable. The old NAME had function 0, which announced this laptop to the
        /// bus as an engine.
        /// </summary>
        internal static readonly byte[] ToolName = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x81, 0x00, 0x80 };

        /// <summary>
        /// RP1210 Protect_J1939_Address for SA 0xF9, blocking until the claim settles. The adapter
        /// refuses to send from an address it has not claimed, so a failed claim is why a code clear
        /// "does nothing" — the result is kept in ClaimStatus for the log.
        /// </summary>
        public bool ClaimToolAddress()
        {
            if (!IsConnected) return false;
            if (_command == null)
            {
                // No SendCommand export: announce the address by hand and hope the driver lets it send.
                SendRaw(J1939Clear.Rp1210Message(0xEE00, 255, ToolName, 6));
                ClaimStatus = "Driver has no RP1210_SendCommand; announced SA " + OurSa + " by hand.";
                return true;
            }
            byte[] cmd = new byte[10];
            cmd[0] = OurSa;
            Buffer.BlockCopy(ToolName, 0, cmd, 1, 8);
            cmd[9] = 0; // BLOCK_UNTIL_DONE
            short rc = SafeCommand(CmdProtectAddr, cmd, 10);
            if (rc >= 0 && rc < 128)
            {
                ClaimStatus = "Tool address SA " + OurSa + " (0x" + OurSa.ToString("X2") + ") claimed.";
                return true;
            }
            if (rc == short.MinValue)
            {
                ClaimStatus = "Address claim for SA " + OurSa + " FAILED: " + (LastError ?? "driver call failed")
                    + ". The adapter may refuse to send code-clear requests.";
                return false;
            }
            ClaimStatus = "Address claim for SA " + OurSa + " FAILED: " + ErrorText(rc)
                + ". The adapter may refuse to send code-clear requests — close other J1939 tools and Connect again.";
            return false;
        }

        public void SendDm13(bool stopBroadcast)
        {
            // SAE J1939-73 DM13 PGN 57088 (0xDF00): J1939 network #1 stop/start broadcast
            byte[] d = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
            d[0] = stopBroadcast ? (byte)0x04 : (byte)0x01;
            SendJ1939(0xDF00, 255, d, 6);
        }

        /// <summary>Request DM1 and DM2 from engine, compressor controller, and broadcast. Returns how many sends failed.</summary>
        public int RequestDm(int engineSa)
        {
            int failed = 0;
            List<J1939Tx> frames = J1939Clear.RefreshDmFrames(engineSa);
            for (int i = 0; i < frames.Count; i++)
                if (!SendJ1939(frames[i])) failed++;
            return failed;
        }

        public bool RequestPgn(int pgn, byte dest)
        {
            return SendJ1939(J1939Clear.Request, dest, J1939Clear.RequestPayload(pgn));
        }

        public bool Read(out J1939Frame frame)
        {
            frame = null;
            if (!IsConnected || _read == null) return false;
            short r;
            lock (_gate)
            {
                try { r = _read(_client, _rx, (short)_rx.Length, 0); }
                catch (Exception ex) { LastError = "read failed: " + ex.Message; return false; }
                if (r < 0) { LastError = ErrorText(r); return false; }
                if (r < 10) return false;
                int pgn = J1939Decode.NormalizePgn(_rx[4] | (_rx[5] << 8) | (_rx[6] << 16));
                int sa = _rx[8];
                int da = _rx[9];
                int dlen = r - 10;
                byte[] data = new byte[dlen];
                if (dlen > 0) Buffer.BlockCopy(_rx, 10, data, 0, dlen);
                frame = new J1939Frame { Pgn = pgn, Sa = sa, Da = da, Data = data };
            }
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
