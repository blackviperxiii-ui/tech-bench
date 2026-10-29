//! The RP1210 client (port of the session half of `Rp1210.cs`): connect over the adapter's
//! declared devices and J1939 protocols, claim the tool address, send and read J1939 frames.
//!
//! Every frame goes through [`tb_core::j1939::guard::check`] right before the driver's
//! `RP1210_SendMessage`. Nothing reaches the bus that the guard refuses.

use crate::driver::{Driver, Loader, NoDriver, CMD_FILTERS_PASS, CMD_PROTECT_ADDRESS};
use tb_core::j1939::clear::{address_claim_tx, J1939Tx, ADDRESS_CLAIM, TOOL_NAME, TOOL_SA};
use tb_core::j1939::decode::normalize_pgn;
use tb_core::j1939::guard::{self, SendPolicy};
use tb_core::j1939::J1939Frame;
use tb_core::rp1210::Rp1210Api;

// RP1210C errors that mean the adapter no longer owns the tool's J1939 address.
const ADDRESS_ERRORS: [i16; 4] = [146, 152, 153, 157];

pub struct Rp1210 {
    driver: Box<dyn Driver>,
    loader: Option<Loader>,
    /// The DLL path `driver` was loaded from, when a loader is in use.
    loaded_for: Option<String>,
    client: i16,
    rx: Vec<u8>,
    pub api: Option<Rp1210Api>,
    pub device_id: i16,
    pub protocol: String,
    pub last_error: String,
    /// Outcome of the last Protect_J1939_Address, for the log.
    pub claim_status: String,
    /// Frames the guard refused, with why: for the log and for tests.
    pub refused: Vec<String>,
}

fn ok(rc: i16) -> bool {
    (0..128).contains(&rc)
}

impl Rp1210 {
    /// One fixed driver for every adapter (the fake adapter, or a test double).
    pub fn new(driver: Box<dyn Driver>) -> Self {
        Self::build(driver, None)
    }

    /// Load each adapter's own driver on connect (the vendor DLLs).
    pub fn with_loader(loader: Loader) -> Self {
        Self::build(Box::new(NoDriver), Some(loader))
    }

    fn build(driver: Box<dyn Driver>, loader: Option<Loader>) -> Self {
        Self {
            driver,
            loader,
            loaded_for: None,
            client: -1,
            rx: vec![0; 2048],
            api: None,
            device_id: 0,
            protocol: String::new(),
            last_error: String::new(),
            claim_status: String::new(),
            refused: Vec::new(),
        }
    }

    pub fn is_connected(&self) -> bool {
        ok(self.client)
    }

    pub fn error_text(&mut self, code: i16) -> String {
        if ok(code) {
            return "OK".into();
        }
        let e = code.saturating_abs();
        match self.driver.error_text(e) {
            Some(m) => format!("{m} ({e})"),
            None => format!("error {e}"),
        }
    }

    /// Try each declared device with each J1939 protocol until one connects.
    pub fn connect(&mut self, api: &Rp1210Api) -> bool {
        self.disconnect();
        if let Some(loader) = self.loader.as_mut() {
            if self.loaded_for.as_deref() != Some(api.dll_path.as_str()) {
                match loader(api) {
                    Ok(d) => {
                        self.driver = d;
                        self.loaded_for = Some(api.dll_path.clone());
                    }
                    Err(e) => {
                        self.last_error = e;
                        return false;
                    }
                }
            }
        }
        let mut fails = Vec::new();
        for dev in api.device_ids() {
            for proto in &api.j1939_protocols {
                let id = self.driver.connect(dev as i16, proto);
                if ok(id) {
                    self.client = id;
                    self.device_id = dev as i16;
                    self.protocol = proto.clone();
                    self.api = Some(api.clone());
                    self.driver.command(CMD_FILTERS_PASS, id, &mut []);
                    self.claim_tool_address();
                    self.last_error.clear();
                    return true;
                }
                let why = self.error_text(id);
                fails.push(format!("{why} [dev {dev} {proto}]"));
            }
        }
        self.last_error = if fails.is_empty() {
            "No device on this adapter accepted a J1939 connection.".into()
        } else {
            fails.join("; ")
        };
        false
    }

    pub fn disconnect(&mut self) {
        if self.is_connected() {
            self.driver.disconnect(self.client);
        }
        self.client = -1;
    }

    /// RP1210 Protect_J1939_Address for SA 0xF9. A driver without SendCommand gets a hand-made
    /// address claim, which is reported as a failure: it does not protect the address.
    pub fn claim_tool_address(&mut self) -> bool {
        if !self.is_connected() {
            return false;
        }
        let mut cmd = [0u8; 10];
        cmd[0] = TOOL_SA;
        cmd[1..9].copy_from_slice(&TOOL_NAME);
        cmd[9] = 0; // BLOCK_UNTIL_DONE
        match self
            .driver
            .command(CMD_PROTECT_ADDRESS, self.client, &mut cmd)
        {
            None => {
                let claim = address_claim_tx();
                self.send_raw(&claim.rp1210);
                self.claim_status = format!("Address claim for SA {TOOL_SA} FAILED: driver has no RP1210_SendCommand (announced by hand; not protected).");
                false
            }
            Some(rc) if ok(rc) => {
                self.claim_status = format!("Tool address SA {TOOL_SA} (0x{TOOL_SA:02X}) claimed.");
                true
            }
            Some(rc) => {
                let why = self.error_text(rc);
                self.claim_status = format!("Address claim for SA {TOOL_SA} FAILED: {why}. The adapter may refuse to send code-clear requests — close other J1939 tools and Connect again.");
                false
            }
        }
    }

    fn send_raw(&mut self, m: &[u8]) -> i16 {
        self.driver.send(self.client, m)
    }

    /// Send one frame if the guard allows it. False when refused, not connected, or the adapter
    /// rejected it; `last_error` says which.
    pub fn send_j1939(&mut self, tx: &J1939Tx, policy: SendPolicy) -> bool {
        if let Err(r) = guard::check(tx, policy) {
            self.last_error = r.0.clone();
            self.refused.push(r.0);
            return false;
        }
        if !self.is_connected() {
            self.last_error = "adapter not connected".into();
            return false;
        }
        let mut rc = self.send_raw(&tx.rp1210);
        // The adapter drops a claimed address on bus-off or when another tool takes it, then
        // refuses every request. Claim it back once instead of failing the whole clear.
        if rc != 0
            && ADDRESS_ERRORS.contains(&rc.saturating_abs())
            && tx.pgn != ADDRESS_CLAIM
            && self.claim_tool_address()
        {
            rc = self.send_raw(&tx.rp1210);
        }
        if rc != 0 {
            self.last_error = self.error_text(rc);
            return false;
        }
        true
    }

    /// One frame from the adapter, if one is waiting.
    pub fn read(&mut self) -> Option<J1939Frame> {
        if !self.is_connected() {
            return None;
        }
        let r = self.driver.read(self.client, &mut self.rx);
        if r < 0 {
            self.last_error = self.error_text(r);
            return None;
        }
        let r = r as usize;
        if r < 10 {
            return None;
        }
        let b = &self.rx;
        let pgn = normalize_pgn(i32::from(b[4]) | (i32::from(b[5]) << 8) | (i32::from(b[6]) << 16));
        Some(J1939Frame {
            pgn,
            sa: i32::from(b[8]),
            da: i32::from(b[9]),
            data: b[10..r].to_vec(),
        })
    }
}
