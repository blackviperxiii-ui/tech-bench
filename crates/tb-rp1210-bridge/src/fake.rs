//! A fake RP1210 adapter with scripted ECMs on it, so the bridge (and the mock DLL built from this
//! same code) can be exercised with no adapter and no engine. Nothing here touches hardware.
//!
//! The default bus is a shop compressor: the engine at SA 0 idling at 800 RPM with a DEF tank fault
//! and one previously active code, and the compressor controller at SA 48 with its own fault. Both
//! answer DM1/DM2 requests and ACK a directed DM11/DM3.

use crate::driver::{Driver, CMD_FILTERS_PASS, CMD_PROTECT_ADDRESS};
use std::collections::VecDeque;
use std::sync::{Arc, Mutex, MutexGuard};
use std::time::{Duration, Instant};
use tb_core::j1939::clear::{
    ACK, ACK_POSITIVE, BROADCAST, DEF_TANK, DM1, DM11, DM2, DM3, REQUEST, TOOL_SA,
};

/// One scripted ECM.
#[derive(Debug, Clone)]
pub struct FakeEcm {
    pub sa: u8,
    pub active: Vec<(u32, u8)>,
    pub prev: Vec<(u32, u8)>,
    /// Codes that are still true, so they re-latch right after a DM11.
    pub still_true: Vec<(u32, u8)>,
    /// ACK control byte for a directed DM11 / DM3; `None` stays silent.
    pub dm11_control: Option<u8>,
    pub dm3_control: Option<u8>,
    pub rpm: Option<f64>,
    pub def: Option<Vec<u8>>,
    pub vin: Option<String>,
    pub software: Option<String>,
    pub component: Option<String>,
    pub hours_raw: Option<u32>,
}

impl FakeEcm {
    pub fn new(sa: u8) -> Self {
        Self {
            sa,
            active: Vec::new(),
            prev: Vec::new(),
            still_true: Vec::new(),
            dm11_control: Some(ACK_POSITIVE as u8),
            dm3_control: Some(ACK_POSITIVE as u8),
            rpm: None,
            def: None,
            vin: None,
            software: None,
            component: None,
            hours_raw: None,
        }
    }

    fn lamps(&self) -> u8 {
        if self.active.iter().any(|c| c.0 == 5246) {
            0x10
        } else if self.active.is_empty() {
            0x00
        } else {
            0x04
        }
    }
}

/// The shop compressor the bridge's fake mode and the mock DLL put on the bus.
pub fn shop_compressor() -> Vec<FakeEcm> {
    let mut engine = FakeEcm::new(0);
    engine.rpm = Some(800.0);
    engine.active = vec![(1761, 9)];
    engine.prev = vec![(100, 1)];
    engine.def = Some(vec![125, 60, 0xFF, 0xFF, 0, 0, 0xFF, 0xFF]);
    engine.vin = Some("FAKE0000000000001*".into());
    engine.software = Some("FAKE-SW-1*".into());
    engine.component = Some("CUMMINS*QSB6.7*FAKE*".into());
    engine.hours_raw = Some(24_690); // 1234.5 h
    let mut controller = FakeEcm::new(48);
    controller.active = vec![(520_200, 3)];
    vec![engine, controller]
}

#[derive(Debug)]
pub struct FakeState {
    pub ecms: Vec<FakeEcm>,
    /// Every buffer handed to RP1210_SendMessage, in order.
    pub sent: Vec<Vec<u8>>,
    /// Every RP1210_SendCommand: (command, data).
    pub commands: Vec<(i16, Vec<u8>)>,
    pub connected: bool,
    /// Send calls left to fail with error 140 (bus off), for tests.
    pub fail_sends: usize,
    rx: VecDeque<Vec<u8>>,
    started: Instant,
    last_fast: Option<Instant>,
    last_slow: Option<Instant>,
}

/// The fake adapter. Clones share one bus, so a test can watch what the bridge sent.
#[derive(Debug, Clone)]
pub struct FakeBus {
    state: Arc<Mutex<FakeState>>,
}

pub const CLIENT_ID: i16 = 1;
const ERR_NOT_CONNECTED: i16 = 129;
const ERR_INVALID_DEVICE: i16 = 134;
const ERR_BUS_OFF: i16 = 140;

impl Default for FakeBus {
    fn default() -> Self {
        Self::new(shop_compressor())
    }
}

impl FakeBus {
    pub fn new(ecms: Vec<FakeEcm>) -> Self {
        Self {
            state: Arc::new(Mutex::new(FakeState {
                ecms,
                sent: Vec::new(),
                commands: Vec::new(),
                connected: false,
                fail_sends: 0,
                rx: VecDeque::new(),
                started: Instant::now(),
                last_fast: None,
                last_slow: None,
            })),
        }
    }

    pub fn state(&self) -> MutexGuard<'_, FakeState> {
        self.state.lock().unwrap_or_else(|e| e.into_inner())
    }
}

/// An RP1210 receive buffer: 4-byte timestamp, PGN (3), priority, SA, DA, data.
fn rx_frame(started: Instant, pgn: i32, sa: u8, da: u8, data: &[u8]) -> Vec<u8> {
    let ms = started.elapsed().as_millis() as u32;
    let mut b = ms.to_be_bytes().to_vec();
    b.push((pgn & 0xFF) as u8);
    b.push(((pgn >> 8) & 0xFF) as u8);
    b.push(((pgn >> 16) & 0xFF) as u8);
    b.push(6);
    b.push(sa);
    b.push(da);
    b.extend_from_slice(data);
    b
}

fn dm_payload(lamps: u8, codes: &[(u32, u8)]) -> Vec<u8> {
    let mut d = vec![lamps, 0xFF];
    if codes.is_empty() {
        d.extend_from_slice(&[0, 0, 0, 0, 0xFF, 0xFF]);
    }
    for &(spn, fmi) in codes {
        d.push((spn & 0xFF) as u8);
        d.push(((spn >> 8) & 0xFF) as u8);
        d.push(((((spn >> 16) & 0x07) << 5) as u8) | (fmi & 0x1F));
        d.push(1);
    }
    while d.len() < 8 {
        d.push(0xFF);
    }
    d
}

impl FakeState {
    fn push(&mut self, pgn: i32, sa: u8, da: u8, data: &[u8]) {
        let f = rx_frame(self.started, pgn, sa, da, data);
        self.rx.push_back(f);
    }

    /// What the ECMs broadcast on their own: EEC1 and coolant every 100 ms, DM1 every second.
    fn broadcast(&mut self) {
        let now = Instant::now();
        if self
            .last_fast
            .is_none_or(|t| now - t >= Duration::from_millis(100))
        {
            self.last_fast = Some(now);
            for e in self.ecms.clone() {
                if let Some(rpm) = e.rpm {
                    let raw = (rpm / 0.125) as u16;
                    let [lo, hi] = raw.to_le_bytes();
                    self.push(
                        0xF004,
                        e.sa,
                        BROADCAST,
                        &[0xF0, 0x7D, 0x7D, lo, hi, 0xFF, 0xFF, 0xFF],
                    );
                }
                self.push(
                    0xFEEE,
                    e.sa,
                    BROADCAST,
                    &[130, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
                );
            }
        }
        if self
            .last_slow
            .is_none_or(|t| now - t >= Duration::from_millis(1000))
        {
            self.last_slow = Some(now);
            for e in self.ecms.clone() {
                self.push(DM1, e.sa, BROADCAST, &dm_payload(e.lamps(), &e.active));
            }
        }
    }

    /// Answer a Request the way the scripted ECMs would.
    fn answer(&mut self, dest: u8, asked: i32) {
        let started = self.started;
        let mut out: Vec<Vec<u8>> = Vec::new();
        for e in &mut self.ecms {
            let direct = dest == e.sa;
            if !direct && dest != BROADCAST {
                continue;
            }
            let reply = |pgn: i32, data: &[u8]| rx_frame(started, pgn, e.sa, BROADCAST, data);
            match asked {
                DM11 | DM3 => {
                    let control = if asked == DM11 {
                        e.dm11_control
                    } else {
                        e.dm3_control
                    };
                    let Some(control) = control else { continue };
                    if i32::from(control) == ACK_POSITIVE {
                        if asked == DM11 {
                            e.active = e.still_true.clone();
                        } else {
                            e.prev.clear();
                        }
                    }
                    // A global request is never acknowledged.
                    if direct {
                        let p = asked;
                        out.push(rx_frame(
                            started,
                            ACK,
                            e.sa,
                            BROADCAST,
                            &[
                                control,
                                0xFF,
                                0xFF,
                                0xFF,
                                TOOL_SA,
                                (p & 0xFF) as u8,
                                ((p >> 8) & 0xFF) as u8,
                                ((p >> 16) & 0xFF) as u8,
                            ],
                        ));
                    }
                }
                DM1 => out.push(reply(DM1, &dm_payload(e.lamps(), &e.active))),
                DM2 => out.push(reply(DM2, &dm_payload(0, &e.prev))),
                DEF_TANK => {
                    if let Some(d) = &e.def {
                        out.push(reply(DEF_TANK, d));
                    }
                }
                0xFEEC => {
                    if let Some(v) = &e.vin {
                        out.push(reply(0xFEEC, v.as_bytes()));
                    }
                }
                0xFEDA => {
                    if let Some(v) = &e.software {
                        out.push(reply(0xFEDA, v.as_bytes()));
                    }
                }
                0xFEEB => {
                    if let Some(v) = &e.component {
                        out.push(reply(0xFEEB, v.as_bytes()));
                    }
                }
                0xFEE5 => {
                    if let Some(h) = e.hours_raw {
                        let mut d = h.to_le_bytes().to_vec();
                        d.extend_from_slice(&[0xFF; 4]);
                        out.push(reply(0xFEE5, &d));
                    }
                }
                _ => {}
            }
        }
        self.rx.extend(out);
    }
}

impl Driver for FakeBus {
    fn connect(&mut self, device: i16, protocol: &str) -> i16 {
        if device != 1 || !protocol.starts_with("J1939") {
            return ERR_INVALID_DEVICE;
        }
        let mut s = self.state();
        s.connected = true;
        s.rx.clear();
        CLIENT_ID
    }

    fn disconnect(&mut self, _client: i16) {
        self.state().connected = false;
    }

    fn send(&mut self, _client: i16, msg: &[u8]) -> i16 {
        let mut s = self.state();
        if !s.connected {
            return ERR_NOT_CONNECTED;
        }
        s.sent.push(msg.to_vec());
        if s.fail_sends > 0 {
            s.fail_sends -= 1;
            return ERR_BUS_OFF;
        }
        if msg.len() >= 9 {
            let pgn = i32::from(msg[0]) | (i32::from(msg[1]) << 8) | (i32::from(msg[2]) << 16);
            if pgn == REQUEST {
                let asked =
                    i32::from(msg[6]) | (i32::from(msg[7]) << 8) | (i32::from(msg[8]) << 16);
                s.answer(msg[5], asked);
            }
        }
        0
    }

    fn read(&mut self, _client: i16, buf: &mut [u8]) -> i16 {
        let mut s = self.state();
        if !s.connected {
            return -ERR_NOT_CONNECTED;
        }
        if s.rx.is_empty() {
            s.broadcast();
        }
        match s.rx.pop_front() {
            None => 0,
            Some(f) => {
                let n = f.len().min(buf.len());
                buf[..n].copy_from_slice(&f[..n]);
                n as i16
            }
        }
    }

    fn command(&mut self, cmd: i16, _client: i16, data: &mut [u8]) -> Option<i16> {
        let mut s = self.state();
        s.commands.push((cmd, data.to_vec()));
        Some(match cmd {
            CMD_FILTERS_PASS | CMD_PROTECT_ADDRESS => 0,
            _ => 0,
        })
    }

    fn error_text(&mut self, code: i16) -> Option<String> {
        Some(
            match code {
                ERR_NOT_CONNECTED => "fake adapter: client not connected",
                ERR_INVALID_DEVICE => "fake adapter: invalid device",
                ERR_BUS_OFF => "fake adapter: bus off",
                _ => return None,
            }
            .to_string(),
        )
    }
}
