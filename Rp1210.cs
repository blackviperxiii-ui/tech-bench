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

        void SafeCommand(short cmd, byte[] data, short len)
        {
            if (_command == null) return;
            try { _command(cmd, _client, data, len); }
            catch (Exception ex) { LastError = "command " + cmd + " failed: " + ex.Message; }
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
            short rc;
            try { rc = _send(_client, m, (short)m.Length, 0, 1); }
            catch (Exception ex) { LastError = "send failed: " + ex.Message; return false; }
            if (rc != 0) { LastError = ErrorText(rc); return false; }
            return true;
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

        static readonly byte[] ToolName = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x81 };

        public void ClaimToolAddress()
        {
            if (!IsConnected) return;
            byte[] cmd = new byte[10];
            cmd[0] = OurSa;
            Buffer.BlockCopy(ToolName, 0, cmd, 1, 8);
            cmd[9] = 0;
            SafeCommand(CmdProtectAddr, cmd, 10);
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
        /// Full J1939 DM11/DM3 plus best-effort UDS 0x14. Hits engine SA 0, the detected engine SA,
        /// compressor controller SA 48, and broadcast. Active faults whose condition is still true
        /// will come back immediately. Blocking — call from the bus worker, never the UI thread.
        /// </summary>
        public string ResetAllFaults()
        {
            return ResetAllFaults(0);
        }

        public string ResetAllFaults(int engineSa)
        {
            if (!IsConnected && Capture == null) return "not connected";
            var sb = new StringBuilder();
            ClaimToolAddress();
            int sent = 0, failed = 0;
            for (int round = 0; round < J1939Clear.Rounds; round++)
            {
                List<J1939Tx> roundFrames = J1939Clear.ClearRoundFrames(engineSa);
                for (int i = 0; i < roundFrames.Count; i++)
                {
                    J1939Tx tx = roundFrames[i];
                    if (SendJ1939(tx.Pgn, tx.Dest, tx.Data, tx.Priority)) sent++;
                    else failed++;
                }
                if (IsConnected) System.Threading.Thread.Sleep(200);
            }
            sb.AppendLine("Sent J1939 DM11 (clear active) and DM3 (clear previously active) x" +
                J1939Clear.Rounds + " to engine, compressor controller (SA " +
                J1939Clear.CompressorSa + "), and broadcast.");
            if (failed > 0)
                sb.AppendLine("Adapter rejected " + failed + " of " + (sent + failed) + " J1939 frames.");
            if (IsConnected)
            {
                string uds = TryUdsClear();
                if (!string.IsNullOrEmpty(uds)) sb.AppendLine(uds);
            }
            RequestDmAfterClear(engineSa);
            List<J1939Tx> aft = J1939Clear.AftertreatmentReadFrames(engineSa);
            for (int i = 0; i < aft.Count; i++)
                SendJ1939(aft[i].Pgn, aft[i].Dest, aft[i].Data, aft[i].Priority);
            sb.AppendLine("Re-requested DM1/DM2 and DEF/SCR tank PGN FE56. This is a code clear, not a DEF dosing reset.");
            return sb.ToString();
        }

        /// <summary>DM3 previously-active only, same shop destinations as reset-all.</summary>
        public string ClearPreviousFaults(int engineSa)
        {
            if (!IsConnected && Capture == null) return "not connected";
            ClaimToolAddress();
            int sent = 0;
            int failed = 0;
            List<J1939Tx> frames = J1939Clear.ClearPreviousFrames(engineSa);
            for (int i = 0; i < frames.Count; i++)
            {
                J1939Tx tx = frames[i];
                if (SendJ1939(tx.Pgn, tx.Dest, tx.Data, tx.Priority)) sent++;
                else failed++;
            }
            int refreshFailed = RequestDmAfterClear(engineSa);
            int refreshCount = J1939Clear.RefreshDmFrames(engineSa).Count;
            sent += refreshCount - refreshFailed;
            failed += refreshFailed;
            if (failed > 0)
            {
                return "Clear previous FAILED: adapter rejected " + failed + " of " + (sent + failed)
                    + " J1939 frames.";
            }
            return "Sent DM3 (clear previously active) to engine, compressor controller (SA " +
                J1939Clear.CompressorSa + "), and broadcast.";
        }

        /// <summary>Request DM1 and DM2 from engine, compressor controller, and broadcast. Returns how many sends failed.</summary>
        public int RequestDmAfterClear(int engineSa)
        {
            int failed = 0;
            List<J1939Tx> frames = J1939Clear.RefreshDmFrames(engineSa);
            for (int i = 0; i < frames.Count; i++)
            {
                J1939Tx tx = frames[i];
                if (!SendJ1939(tx.Pgn, tx.Dest, tx.Data, tx.Priority)) failed++;
            }
            return failed;
        }

        string TryUdsClear()
        {
            try { return TryUdsClearCore(); }
            catch (Exception ex) { return "UDS 0x14 not sent (" + ex.Message + ") — J1939 clear still sent."; }
        }

        string TryUdsClearCore()
        {
            if (_connect == null) return "";
            // Opening a second client while the J1939 session is live is what many adapters refuse,
            // so skip it unless the vendor INI actually advertises ISO15765.
            if (Api != null && !Api.SupportsIso15765)
                return "UDS 0x14 skipped (" + Api.Id + " does not advertise ISO15765) — J1939 clear still sent.";

            // ISO 15765-2 / UDS service 0x14 ClearDiagnosticInformation (all groups). The J1939
            // session is closed first: holding two clients on one device is what upsets the adapter.
            short savedClient = _client;
            short savedDevice = DeviceId;
            string savedProto = Protocol;
            _client = -1;
            if (savedClient >= 0 && _disconnect != null)
            {
                try { _disconnect(savedClient); } catch { }
            }

            string result;
            short iso = -1;
            string used = null;
            try
            {
                string[] protos = { "ISO15765:Baud=250", "ISO15765" };
                foreach (string proto in protos)
                {
                    short id = _connect(IntPtr.Zero, savedDevice, proto, 0, 0, 0);
                    if (id >= 0 && id < 128) { iso = id; used = "dev " + savedDevice + " " + proto; break; }
                }
                if (iso < 0)
                {
                    result = "UDS 0x14 not sent (ISO15765 did not open on this adapter — J1939 clear still sent).";
                }
                else
                {
                    if (_command != null) { try { _command(CmdFiltersPass, iso, null, 0); } catch { } }
                    SendUdsClear(iso);
                    System.Threading.Thread.Sleep(250);
                    try { _disconnect(iso); } catch { }
                    result = "Also sent UDS ClearDiagnosticInformation (0x14) on ISO15765 (" + used + ").";
                }
            }
            finally
            {
                // Always put the J1939 session back, successful UDS or not.
                short re = _connect(IntPtr.Zero, savedDevice, savedProto, 0, 0, 0);
                if (re >= 0 && re < 128)
                {
                    _client = re;
                    SafeCommand(CmdFiltersPass, null, 0);
                    ClaimToolAddress();
                }
            }
            if (!IsConnected)
                result += " NOTE: the J1939 session did not come back — press Connect again.";
            return result;
        }

        void SendUdsClear(short iso)
        {
            if (_send == null) return;
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
                try { _send(iso, msg, 10, 0, 1); } catch { }
                // also PCI single-frame form
                msg[6] = 0x04;
                msg[7] = 0x14;
                msg[8] = 0xFF;
                msg[9] = 0xFF;
                msg[10] = 0xFF;
                try { _send(iso, msg, 11, 0, 1); } catch { }
            }
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
                int pgn = _rx[4] | (_rx[5] << 8) | (_rx[6] << 16);
                int sa = _rx[8];
                int da = _rx[9];
                int dlen = r - 10;
                byte[] data = new byte[dlen];
                if (dlen > 0) Buffer.BlockCopy(_rx, 10, data, 0, dlen);
                frame = new J1939Frame { Pgn = pgn & 0x3FFFF, Sa = sa, Da = da, Data = data };
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
