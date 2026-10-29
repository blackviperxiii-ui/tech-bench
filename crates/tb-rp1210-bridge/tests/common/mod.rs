//! Shared helpers: drive a Bus until a condition holds.

#![allow(dead_code)]

use serde_json::{json, Value};
use std::time::{Duration, Instant};
use tb_core::rp1210::Rp1210Api;
use tb_rp1210_bridge::bus::Bus;

pub fn fake_api(dll: &str) -> Rp1210Api {
    Rp1210Api {
        id: "FAKE".into(),
        name: "Fake adapter".into(),
        dll_path: dll.into(),
        devices: Vec::new(),
        j1939_protocols: vec!["J1939:Baud=250".into()],
        supports_iso15765: false,
    }
}

pub fn call(bus: &mut Bus, req: Value, apis: &[Rp1210Api]) -> Value {
    bus.handle(&req, apis)
        .unwrap_or_else(|e| panic!("{req} failed: {e}"))
}

/// Run the worker loop until `ok` holds on a snapshot, or panic after `secs`.
pub fn run_until(bus: &mut Bus, secs: u64, what: &str, ok: impl Fn(&Value) -> bool) -> Value {
    let end = Instant::now() + Duration::from_secs(secs);
    loop {
        bus.cycle();
        let s = bus.handle(&json!({"cmd": "snapshot"}), &[]).unwrap();
        if ok(&s) {
            return s;
        }
        assert!(Instant::now() < end, "timed out waiting for {what}: {s}");
        std::thread::sleep(Duration::from_millis(5));
    }
}

/// Run the worker loop for `ms`.
pub fn run_for(bus: &mut Bus, ms: u64) {
    let end = Instant::now() + Duration::from_millis(ms);
    while Instant::now() < end {
        bus.cycle();
        std::thread::sleep(Duration::from_millis(5));
    }
}

/// PGN of a sent RP1210 buffer.
pub fn pgn_of(m: &[u8]) -> i32 {
    i32::from(m[0]) | (i32::from(m[1]) << 8) | (i32::from(m[2]) << 16)
}
