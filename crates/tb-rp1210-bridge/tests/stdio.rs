//! The bridge binary end to end: `--fake`, JSON lines in and out.

use serde_json::{json, Value};
use std::io::{BufRead, BufReader, Write};
use std::process::{Command, Stdio};
use std::time::{Duration, Instant};

#[test]
fn json_lines_over_stdio() {
    let mut child = Command::new(env!("CARGO_BIN_EXE_tb-rp1210-bridge"))
        .arg("--fake")
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .spawn()
        .expect("bridge starts");
    let mut stdin = child.stdin.take().unwrap();
    let mut out = BufReader::new(child.stdout.take().unwrap());
    let mut id = 0;
    let mut ask = |req: Value| -> Value {
        id += 1;
        let mut r = req;
        r["id"] = json!(id);
        writeln!(stdin, "{r}").unwrap();
        stdin.flush().unwrap();
        let mut line = String::new();
        out.read_line(&mut line).unwrap();
        let v: Value = serde_json::from_str(&line).unwrap();
        assert_eq!(v["id"], json!(id), "responses come back in order");
        v
    };

    let hello = ask(json!({"cmd": "hello"}));
    assert_eq!(hello["ok"], json!(true));
    assert_eq!(hello["data"]["pointerBits"], json!(usize::BITS));
    let adapters = ask(json!({"cmd": "adapters"}));
    assert_eq!(adapters["data"][0]["id"], json!("FAKE"));
    assert_eq!(
        ask(json!({"cmd": "connect", "adapter": "fake"}))["data"],
        json!(true)
    );

    let end = Instant::now() + Duration::from_secs(5);
    loop {
        let s = ask(json!({"cmd": "snapshot"}));
        if s["data"]["rpm"] == json!(800.0) {
            break;
        }
        assert!(Instant::now() < end, "no RPM over stdio: {s}");
        std::thread::sleep(Duration::from_millis(20));
    }
    let bad = ask(json!({"cmd": "send", "pgn": 55552}));
    assert_eq!(bad["ok"], json!(false));
    assert_eq!(bad["error"], json!("unknown command \"send\""));
    let missing = ask(json!({"cmd": "connect", "adapter": "CMNSI7"}));
    assert_eq!(
        missing["error"],
        json!("No RP1210 adapter CMNSI7 on this PC.")
    );

    assert_eq!(ask(json!({"cmd": "quit"}))["ok"], json!(true));
    let status = child.wait().unwrap();
    assert!(status.success(), "bridge exits cleanly on quit");
}
