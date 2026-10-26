//! The bridge's worker over the fake adapter: connect, live readings, a full code clear, the TSC1
//! Safety gate, and the send guard at the driver boundary. No hardware involved.

mod common;

use common::*;
use serde_json::json;
use tb_core::j1939::clear::{tx, REQUEST, TSC1};
use tb_core::j1939::guard::SendPolicy;
use tb_rp1210_bridge::bus::Bus;
use tb_rp1210_bridge::fake::FakeBus;

fn connected() -> (Bus, FakeBus, Vec<tb_core::rp1210::Rp1210Api>) {
    let fake = FakeBus::default();
    let mut bus = Bus::new(Box::new(fake.clone()));
    let apis = vec![fake_api("(built in)")];
    assert_eq!(
        call(&mut bus, json!({"cmd": "connect"}), &apis),
        json!(true)
    );
    run_until(&mut bus, 5, "engine RPM and DM1", |s| {
        s["rpm"] == json!(800.0) && s["active"].as_array().is_some_and(|a| !a.is_empty())
    });
    (bus, fake, apis)
}

#[test]
fn connects_and_reads_the_engine() {
    let (mut bus, fake, apis) = connected();
    let s = call(&mut bus, json!({"cmd": "snapshot"}), &apis);
    assert_eq!(s["connected"], json!(true));
    assert_eq!(s["engineSa"], json!(0));
    assert_eq!(s["coolantC"], json!(90.0), "FEEE 130 = 90 C");
    assert_eq!(s["active"][0]["spn"], json!(1761));
    assert_eq!(s["active"][0]["fmi"], json!(9));
    assert_eq!(
        s["faultModules"],
        json!([0, 48]),
        "engine and compressor controller"
    );
    assert_eq!(s["defText"], json!("50.0%  temp 20 C"));
    assert_eq!(s["adapterName"], json!("Fake adapter  (FAKE)"));
    assert_eq!(s["protocol"], json!("J1939:Baud=250"));

    // The tool address was protected with SendCommand 19 and the tool NAME.
    let claims: Vec<_> = fake
        .state()
        .commands
        .iter()
        .filter(|c| c.0 == 19)
        .map(|c| c.1.clone())
        .collect();
    assert_eq!(
        claims,
        vec![vec![0xF9, 0, 0, 0, 0, 0, 0x81, 0, 0x80, 0]],
        "Protect_J1939_Address for SA F9"
    );
    let log = call(&mut bus, json!({"cmd": "log"}), &apis);
    assert_eq!(log[0], json!("Connected  device 1  J1939:Baud=250"));
    assert_eq!(log[1], json!("Tool address SA 249 (0xF9) claimed."));

    call(&mut bus, json!({"cmd": "pingIdentity"}), &apis);
    let s = run_until(&mut bus, 5, "identity", |s| s["hours"] != json!(""));
    assert_eq!(s["vin"], json!("FAKE0000000000001"));
    assert_eq!(s["sw"], json!("FAKE-SW-1"));
    assert_eq!(s["compId"], json!("CUMMINS  ·  QSB6.7  ·  FAKE"));
    assert_eq!(s["hours"], json!("1234.5 h"));

    // Everything sent so far is a Request.
    assert!(fake.state().sent.iter().all(|m| pgn_of(m) == REQUEST));

    call(&mut bus, json!({"cmd": "disconnect"}), &apis);
    let s = run_until(&mut bus, 2, "disconnect", |s| {
        s["connected"] == json!(false)
    });
    assert_eq!(s["rpm"], json!(null), "disconnected snapshot is blanked");
    assert_eq!(s["active"], json!([]));
}

#[test]
fn reset_all_clears_engine_and_controller() {
    let (mut bus, fake, apis) = connected();
    let before = fake.state().sent.len();
    let report = call(&mut bus, json!({"cmd": "clear", "kind": "all"}), &apis);
    let report = report.as_str().unwrap();
    assert!(
        report.contains("RESULT: cleared — no active codes came back."),
        "{report}"
    );
    assert!(
        report.contains("Asked directly: SA 0, SA 48, then everyone (SA 255)."),
        "{report}"
    );
    assert!(
        report.contains("SA 48 Compressor controller\r\n  DM11 clear active: ACK (cleared)"),
        "{report}"
    );
    let sent: Vec<Vec<u8>> = fake.state().sent[before..].to_vec();
    assert!(
        sent.iter().all(|m| pgn_of(m) == REQUEST),
        "a code clear puts only Requests on the bus"
    );
    let asked = |pgn: i32, dest: u8| {
        sent.iter()
            .filter(|m| m[5] == dest && m.len() == 9 && pgn_of(&m[6..9]) == pgn)
            .count()
    };
    assert_eq!(asked(0xFED3, 0), 1, "DM11 to the engine once");
    assert_eq!(asked(0xFED3, 48), 1, "DM11 to the controller once");
    assert_eq!(asked(0xFED3, 255), 1, "DM11 to everyone once");
    assert_eq!(asked(0xFECC, 0), 1, "DM3 to the engine once");
    let s = call(&mut bus, json!({"cmd": "snapshot"}), &apis);
    assert_eq!(
        s["active"],
        json!([]),
        "engine list empty after the fresh DM1"
    );
    assert_eq!(s["resetSeq"], json!(1));
    assert_eq!(s["lastResetReport"], json!(report));
}

#[test]
fn adapter_rejecting_every_frame_fails_fast() {
    let (mut bus, fake, apis) = connected();
    fake.state().fail_sends = 1000;
    let report = call(&mut bus, json!({"cmd": "clear", "kind": "previous"}), &apis);
    assert!(
        report.as_str().unwrap().contains(
            "RESULT: FAILED — the adapter rejected every request (fake adapter: bus off (140)). Nothing was sent."
        ),
        "{report}"
    );
}

#[test]
fn clear_while_disconnected_sends_nothing() {
    let fake = FakeBus::default();
    let mut bus = Bus::new(Box::new(fake.clone()));
    let report = call(&mut bus, json!({"cmd": "clear", "kind": "all"}), &[]);
    assert_eq!(
        report,
        json!("Reset all codes FAILED: adapter not connected — nothing was sent.\r\n")
    );
    assert!(fake.state().sent.is_empty());
}

#[test]
fn tsc1_hold_needs_the_safety_toggle() {
    let (mut bus, fake, apis) = connected();
    let tsc_frames = |f: &FakeBus| -> Vec<u8> {
        f.state()
            .sent
            .iter()
            .filter(|m| pgn_of(m) == TSC1)
            .map(|m| m[6])
            .collect()
    };
    // Safety off: a hold request becomes a release.
    assert_eq!(
        call(&mut bus, json!({"cmd": "tsc1", "rpm": 1200}), &apis),
        json!(0)
    );
    run_for(&mut bus, 200);
    assert_eq!(tsc_frames(&fake), vec![0x00], "only a release went out");
    let log = call(&mut bus, json!({"cmd": "log"}), &apis);
    assert!(
        log.as_array()
            .unwrap()
            .contains(&json!("TSC1 refused — Safety toggle is off")),
        "{log}"
    );

    // Safety on: the hold goes out and the heartbeat repeats it.
    assert_eq!(
        call(&mut bus, json!({"cmd": "safety", "allowHold": true}), &apis),
        json!(true)
    );
    assert_eq!(
        call(&mut bus, json!({"cmd": "tsc1", "rpm": 1200}), &apis),
        json!(1200)
    );
    run_for(&mut bus, 300);
    let holds = tsc_frames(&fake).iter().filter(|&&b| b == 0x01).count();
    assert!(holds >= 4, "heartbeat every 40 ms while held, saw {holds}");
    assert_eq!(
        call(&mut bus, json!({"cmd": "snapshot"}), &apis)["tscRpm"],
        json!(1200)
    );

    // Safety off again: released at once, and no further holds.
    call(
        &mut bus,
        json!({"cmd": "safety", "allowHold": false}),
        &apis,
    );
    let after_off = tsc_frames(&fake);
    assert_eq!(after_off.last(), Some(&0x00), "Safety off sends a release");
    run_for(&mut bus, 200);
    assert_eq!(tsc_frames(&fake), after_off, "no holds after Safety off");
    assert_eq!(
        call(&mut bus, json!({"cmd": "snapshot"}), &apis)["tscRpm"],
        json!(0)
    );
}

#[test]
fn guard_stops_forbidden_frames_before_the_driver() {
    let (mut bus, fake, _) = connected();
    let before = fake.state().sent.len();
    let policy = SendPolicy {
        allow_tsc_hold: true,
    };
    let dm14 = tx(0xD900, 0, &[0x01, 0x13, 0, 0, 0, 0, 0xFF, 0xFF], 6);
    assert!(
        !bus.rp_mut().send_j1939(&dm14, policy),
        "DM14 write refused"
    );
    assert_eq!(
        bus.rp().last_error,
        "refused: PGN 0xD900 (DM14 memory access (calibration / parameter write))"
    );
    let prop_b = tx(0xFF5A, 0, &[0; 8], 6);
    assert!(
        !bus.rp_mut().send_j1939(&prop_b, policy),
        "proprietary B refused"
    );
    assert_eq!(bus.rp().refused.len(), 2);
    // Neither reached RP1210_SendMessage. Allow for the auto-refresh racing in nothing: no cycle ran.
    assert_eq!(
        fake.state().sent.len(),
        before,
        "nothing reached the driver"
    );
}

#[test]
fn the_protocol_has_no_raw_send() {
    let mut bus = Bus::new(Box::new(FakeBus::default()));
    for cmd in ["send", "sendRaw", "write", "dm14", "defReset"] {
        assert_eq!(
            bus.handle(&json!({"cmd": cmd, "pgn": 0xD900, "data": [1, 2, 3]}), &[]),
            Err(format!("unknown command \"{cmd}\""))
        );
    }
    assert_eq!(
        bus.handle(&json!({"cmd": "clear", "kind": "def"}), &[]),
        Err("clear needs kind \"all\" or \"previous\"".to_string())
    );
}
