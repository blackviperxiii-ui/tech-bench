//! `tb-rp1210-bridge [--fake]`
//!
//! Reads one JSON request per line on stdin, `{"id": 1, "cmd": "snapshot"}`, and writes one
//! response per line on stdout, `{"id": 1, "ok": true, "data": ...}` or
//! `{"id": 1, "ok": false, "error": "..."}`. EOF on stdin or `{"cmd": "quit"}` releases TSC1,
//! disconnects and exits. `--fake` puts the built-in fake adapter (scripted ECMs, no hardware)
//! in place of the vendor DLLs.

use serde_json::{json, Value};
use std::io::{BufRead, Write};
use std::sync::mpsc;
use std::time::Duration;
use tb_core::rp1210::{windows_dir, Rp1210Api};
use tb_rp1210_bridge::bus::{Bus, CYCLE_MS};
use tb_rp1210_bridge::driver::Driver;
use tb_rp1210_bridge::fake::FakeBus;

fn main() {
    let fake = std::env::args().any(|a| a == "--fake");
    let (adapters, bus): (Vec<Rp1210Api>, Bus) = if fake {
        let api = Rp1210Api {
            id: "FAKE".into(),
            name: "Fake adapter (scripted ECMs, no hardware)".into(),
            dll_path: "(built in)".into(),
            devices: Vec::new(),
            j1939_protocols: vec!["J1939:Baud=250".into()],
            supports_iso15765: false,
        };
        (vec![api], Bus::new(Box::new(FakeBus::default())))
    } else {
        (
            Rp1210Api::adapters(&windows_dir()),
            Bus::with_loader(Box::new(load_vendor_dll)),
        )
    };
    run(adapters, bus);
}

#[cfg(windows)]
fn load_vendor_dll(api: &Rp1210Api) -> Result<Box<dyn Driver>, String> {
    tb_rp1210_bridge::dll::DllDriver::load(&api.dll_path).map(|d| Box::new(d) as Box<dyn Driver>)
}

#[cfg(not(windows))]
fn load_vendor_dll(_api: &Rp1210Api) -> Result<Box<dyn Driver>, String> {
    Err("RP1210 adapters need the Windows build of Tech Bench.".into())
}

fn run(adapters: Vec<Rp1210Api>, mut bus: Bus) {
    let (tx, rx) = mpsc::channel::<String>();
    std::thread::spawn(move || {
        for line in std::io::stdin().lock().lines() {
            let Ok(line) = line else { break };
            if tx.send(line).is_err() {
                break;
            }
        }
    });
    let stdout = std::io::stdout();
    loop {
        loop {
            let line = match rx.try_recv() {
                Ok(l) => l,
                Err(mpsc::TryRecvError::Empty) => break,
                Err(mpsc::TryRecvError::Disconnected) => return shutdown(&mut bus),
            };
            if line.trim().is_empty() {
                continue;
            }
            let (id, reply, quit) = match serde_json::from_str::<Value>(&line) {
                Err(e) => (Value::Null, Err(format!("bad JSON: {e}")), false),
                Ok(req) => {
                    let id = req.get("id").cloned().unwrap_or(Value::Null);
                    if req.get("cmd").and_then(Value::as_str) == Some("quit") {
                        (id, Ok(json!(true)), true)
                    } else {
                        (id, bus.handle(&req, &adapters), false)
                    }
                }
            };
            let out = match reply {
                Ok(data) => json!({"id": id, "ok": true, "data": data}),
                Err(error) => json!({"id": id, "ok": false, "error": error}),
            };
            let mut w = stdout.lock();
            let _ = writeln!(w, "{out}");
            let _ = w.flush();
            if quit {
                return shutdown(&mut bus);
            }
        }
        bus.cycle();
        std::thread::sleep(Duration::from_millis(CYCLE_MS));
    }
}

fn shutdown(bus: &mut Bus) {
    let _ = bus.handle(&json!({"cmd": "tsc1", "rpm": 0}), &[]);
    bus.request_disconnect();
    bus.cycle();
}
