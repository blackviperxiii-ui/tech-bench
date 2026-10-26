//! The bridge's 32-bit DLL loader against the mock RP1210 driver DLL: load it, connect, read the
//! scripted engine, run a full code clear through RP1210_SendMessage, and check the guard keeps a
//! calibration write from ever reaching the DLL. Windows i686 only; CI builds the DLL first and
//! points RP1210_MOCK_DLL at it.

#![cfg(all(windows, target_pointer_width = "32"))]

use libloading::Library;
use serde_json::json;
use std::time::{Duration, Instant};
use tb_core::j1939::clear::{tx, REQUEST};
use tb_core::j1939::guard::SendPolicy;
use tb_core::rp1210::Rp1210Api;
use tb_rp1210_bridge::bus::Bus;
use tb_rp1210_bridge::dll::DllDriver;

fn mock_dll() -> String {
    if let Ok(p) = std::env::var("RP1210_MOCK_DLL") {
        return p;
    }
    // target/<triple>/debug/deps/load_dll-*.exe -> target/<triple>/debug/rp1210_mock.dll
    let exe = std::env::current_exe().unwrap();
    for dir in exe.ancestors().skip(1).take(2) {
        let p = dir.join("rp1210_mock.dll");
        if p.is_file() {
            return p.to_string_lossy().into_owned();
        }
    }
    panic!("rp1210_mock.dll not found: build -p rp1210-mock first or set RP1210_MOCK_DLL");
}

fn sent(lib: &Library) -> Vec<Vec<u8>> {
    type Count = unsafe extern "system" fn() -> i32;
    type Get = unsafe extern "system" fn(i32, *mut u8, i16) -> i16;
    // SAFETY: the mock DLL's own test exports with these signatures.
    unsafe {
        let count: libloading::Symbol<Count> = lib
            .get(b"MOCK_SentCount")
            .or_else(|_| lib.get(b"MOCK_SentCount@0"))
            .unwrap();
        let get: libloading::Symbol<Get> = lib
            .get(b"MOCK_SentMessage")
            .or_else(|_| lib.get(b"MOCK_SentMessage@12"))
            .unwrap();
        (0..count())
            .map(|i| {
                let mut buf = vec![0u8; 64];
                let n = get(i, buf.as_mut_ptr(), buf.len() as i16);
                assert!(n > 0, "message {i} readable");
                buf.truncate(n as usize);
                buf
            })
            .collect()
    }
}

#[test]
fn bridge_drives_the_mock_rp1210_dll() {
    let path = mock_dll();
    let driver = DllDriver::load(&path).expect("mock DLL loads with every RP1210 entry point");
    // A second handle to the same loaded DLL, for the MOCK_* read-back exports.
    // SAFETY: the DLL is already loaded by the driver; this only adds a reference.
    let lib = unsafe { Library::new(&path) }.unwrap();

    let api = Rp1210Api {
        id: "MOCK".into(),
        name: "Mock RP1210 DLL".into(),
        dll_path: path.clone(),
        devices: Vec::new(),
        j1939_protocols: vec!["J1939:Baud=250".into()],
        supports_iso15765: false,
    };
    let mut bus = Bus::new(Box::new(driver));
    let apis = vec![api];
    bus.handle(&json!({"cmd": "connect", "adapter": "MOCK"}), &apis)
        .unwrap();
    let end = Instant::now() + Duration::from_secs(5);
    loop {
        bus.cycle();
        let s = bus.handle(&json!({"cmd": "snapshot"}), &apis).unwrap();
        if s["rpm"] == json!(800.0) && s["active"][0]["spn"] == json!(1761) {
            break;
        }
        assert!(Instant::now() < end, "no engine data through the DLL: {s}");
        std::thread::sleep(Duration::from_millis(5));
    }
    let log = bus.handle(&json!({"cmd": "log"}), &apis).unwrap();
    assert_eq!(
        log[1],
        json!("Tool address SA 249 (0xF9) claimed."),
        "SendCommand bound and Protect_J1939_Address answered"
    );

    let report = bus
        .handle(&json!({"cmd": "clear", "kind": "all"}), &apis)
        .unwrap();
    assert!(
        report
            .as_str()
            .unwrap()
            .contains("RESULT: cleared — no active codes came back."),
        "{report}"
    );

    let before = sent(&lib);
    assert!(!before.is_empty(), "frames reached RP1210_SendMessage");
    assert!(
        before.iter().all(|m| {
            (i32::from(m[0]) | (i32::from(m[1]) << 8) | (i32::from(m[2]) << 16)) == REQUEST
        }),
        "only Requests reached the DLL"
    );
    let dm14 = tx(0xD900, 0, &[0x01, 0x13, 0, 0, 0, 0, 0xFF, 0xFF], 6);
    assert!(!bus.rp_mut().send_j1939(
        &dm14,
        SendPolicy {
            allow_tsc_hold: true
        }
    ));
    assert_eq!(
        sent(&lib).len(),
        before.len(),
        "the DM14 write never reached the DLL"
    );

    bus.handle(&json!({"cmd": "disconnect"}), &apis).unwrap();
    bus.cycle();
}
