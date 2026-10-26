//! The app's client against the real bridge binary in `--fake` mode.

use serde_json::{json, Value};
use std::path::Path;
use std::time::{Duration, Instant};
use tb_rp1210_bridge::client::{BridgeClient, CALL_TIMEOUT, CLEAR_TIMEOUT, STOPPED};

fn bridge() -> BridgeClient {
    BridgeClient::spawn(Path::new(env!("CARGO_BIN_EXE_tb-rp1210-bridge")), true)
        .expect("bridge starts")
}

fn snapshot_until(c: &mut BridgeClient, what: &str, ok: impl Fn(&Value) -> bool) -> Value {
    let end = Instant::now() + Duration::from_secs(8);
    loop {
        let s = c.call("snapshot", Value::Null, CALL_TIMEOUT).unwrap();
        if ok(&s) {
            return s;
        }
        assert!(Instant::now() < end, "never saw {what}: {s}");
        std::thread::sleep(Duration::from_millis(50));
    }
}

#[test]
fn reads_codes_and_clears_through_the_client() {
    let mut c = bridge();
    assert_eq!(
        c.call("hello", Value::Null, CALL_TIMEOUT).unwrap()["pointerBits"],
        json!(usize::BITS)
    );
    let adapters = c.call("adapters", Value::Null, CALL_TIMEOUT).unwrap();
    assert_eq!(adapters[0]["id"], json!("FAKE"));
    assert_eq!(adapters[0]["dll"], json!("(built in)"));
    c.call("connect", json!({"adapter": "FAKE"}), CALL_TIMEOUT)
        .unwrap();

    let s = snapshot_until(&mut c, "engine codes", |s| {
        s["rpm"] == json!(800.0) && s["active"].as_array().is_some_and(|a| !a.is_empty())
    });
    assert_eq!(s["connected"], json!(true));
    assert_eq!(s["hud"]["rpm"], json!("RPM: 800"));
    assert_eq!(s["hud"]["lamps"], json!("Red Stop: off   Amber: ON"));
    assert_eq!(
        s["hud"]["mon"],
        json!("Coolant: 90 C    Oil: —    Battery: —    Fuel rate: —    TSC1 released")
    );
    let status = s["hud"]["status"].as_str().unwrap();
    assert!(status.starts_with(
            "Connected  Fake adapter (scripted ECMs, no hardware)  (FAKE)  device 1  J1939:Baud=250  "
        ), "{status}");
    assert_eq!(s["active"][0]["spn"], json!(1761));
    assert_eq!(s["active"][0]["fmi"], json!(9));
    let line = s["active"][0]["text"].as_str().unwrap();
    assert!(line.starts_with("SPN 1761  FMI 9  occ "), "{line}");
    let s = snapshot_until(&mut c, "previous codes", |s| {
        s["prev"].as_array().is_some_and(|a| !a.is_empty())
    });
    assert_eq!(s["prev"][0]["spn"], json!(100));

    // Safety off: a hold is refused and nothing is held.
    assert_eq!(
        c.call("tsc1", json!({"rpm": 800}), CALL_TIMEOUT).unwrap(),
        json!(0)
    );
    assert_eq!(
        c.call("safety", json!({"allowHold": true}), CALL_TIMEOUT)
            .unwrap(),
        json!(true)
    );
    assert_eq!(
        c.call("tsc1", json!({"rpm": 800}), CALL_TIMEOUT).unwrap(),
        json!(800)
    );
    c.call("safety", json!({"allowHold": false}), CALL_TIMEOUT)
        .unwrap();
    let s = c.call("snapshot", Value::Null, CALL_TIMEOUT).unwrap();
    assert_eq!(s["tscRpm"], json!(0), "Safety off releases the hold");

    let report = c
        .call("clear", json!({"kind": "all"}), CLEAR_TIMEOUT)
        .unwrap();
    let report = report.as_str().unwrap();
    assert!(
        report.starts_with("CODE CLEAR — reset all codes (J1939 DM11 clear active + DM3 clear previous)\r\nRESULT: cleared — no active codes came back."),
        "{report}"
    );
    let s = c.call("snapshot", Value::Null, CALL_TIMEOUT).unwrap();
    assert_eq!(s["resetSeq"], json!(1));
    assert_eq!(s["lastResetReport"], json!(report));

    let log = c.call("log", Value::Null, CALL_TIMEOUT).unwrap();
    let log: Vec<&str> = log
        .as_array()
        .unwrap()
        .iter()
        .filter_map(Value::as_str)
        .collect();
    assert!(
        log.contains(&"TSC1 refused — Safety toggle is off"),
        "{log:?}"
    );
    assert!(
        log.iter().any(|l| l.starts_with("Code clear — ")),
        "{log:?}"
    );

    let err = c
        .call("clear", json!({"kind": "dm14"}), CALL_TIMEOUT)
        .unwrap_err();
    assert_eq!(err, "clear needs kind \"all\" or \"previous\"");
    c.quit();
}

#[test]
fn a_stopped_bridge_says_so() {
    let mut c = bridge();
    assert!(c.alive());
    c.call("quit", Value::Null, CALL_TIMEOUT).unwrap();
    let end = Instant::now() + Duration::from_secs(5);
    while c.alive() {
        assert!(Instant::now() < end, "bridge did not exit on quit");
        std::thread::sleep(Duration::from_millis(20));
    }
    assert_eq!(
        c.call("snapshot", Value::Null, CALL_TIMEOUT).unwrap_err(),
        STOPPED
    );
}

#[test]
fn a_missing_bridge_is_a_plain_error() {
    let err = BridgeClient::spawn(Path::new("no-such-dir/tb-rp1210-bridge"), true)
        .err()
        .unwrap();
    assert!(
        err.starts_with("Could not start the adapter bridge no-such-dir"),
        "{err}"
    );
}

#[test]
fn session_files_and_report_from_the_live_bus() {
    let mut c = bridge();
    c.call("connect", json!({"adapter": "FAKE"}), CALL_TIMEOUT)
        .unwrap();
    snapshot_until(&mut c, "both code lists", |s| {
        s["rpm"] == json!(800.0)
            && s["active"].as_array().is_some_and(|a| !a.is_empty())
            && s["prev"].as_array().is_some_and(|a| !a.is_empty())
    });
    let f = c
        .call(
            "session",
            json!({"job": "WO 7-01", "local": "2026-05-01 14:30:00", "stamp": "20260501_143000",
                   "iso": "2026-05-01T14:30:00.0000000-05:00"}),
            CALL_TIMEOUT,
        )
        .unwrap();
    assert_eq!(f["base"], json!("WO_7_01_session_20260501_143000"));
    let body = f["body"].as_str().unwrap();
    assert!(
        body.starts_with("Job  WO 7-01\r\nTech Bench session  2026-05-01 14:30:00\r\n"),
        "{body}"
    );
    assert!(
        body.contains("\r\nRPM 800  Red Stop off  Amber ON"),
        "{body}"
    );
    let dtc = f["dtcCsv"].as_str().unwrap();
    assert!(
        dtc.contains("2026-05-01T14:30:00.0000000-05:00,\"WO 7-01\",active,1761,9,"),
        "{dtc}"
    );
    assert!(dtc.contains(",previous,100,1,"), "{dtc}");
    assert!(f["timelineCsv"]
        .as_str()
        .unwrap()
        .starts_with("first_seen,last_seen,job,spn,fmi,"));

    let report = c
        .call(
            "report",
            json!({"job": "WO 7-01", "when": "2026-05-01 14:30", "history": "This unit has been here before"}),
            CALL_TIMEOUT,
        )
        .unwrap();
    let report = report.as_str().unwrap();
    assert!(
        report.starts_with("TECH BENCH — DIAGNOSTIC REPORT\r\n"),
        "{report}"
    );
    assert!(report.contains("Job:      WO 7-01\r\nDate:     2026-05-01 14:30\r\n"));
    assert!(report.contains("  RPM:      800\r\n"), "{report}");
    assert!(report.contains("ACTIVE FAULTS (DM1)\r\n  SPN 1761  FMI 9"));
    assert!(report.contains("UNIT HISTORY\r\n  This unit has been here before\r\n"));
    c.quit();
}
