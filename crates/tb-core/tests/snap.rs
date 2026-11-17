//! Snapshot A / B and Diff (`SessionIo.Diff` in src/Core/J1939/SessionData.cs; the C# SelfTest
//! has no Diff case, so these pin the C# output read from its source).

#[cfg(target_arch = "wasm32")]
use wasm_bindgen_test::wasm_bindgen_test as test;

use tb_core::j1939::monitor::BusSnapshot;
use tb_core::j1939::snap::{diff, Snap};
use tb_core::j1939::Dtc;

fn snap(time: &str, label: &str, rpm: f64, red: bool, active: &[&str], prev: &[&str]) -> Snap {
    Snap {
        time: time.into(),
        label: label.into(),
        rpm,
        red,
        amber: false,
        def: "62 %".into(),
        coolant: "80 C".into(),
        oil: "—".into(),
        batt: "—".into(),
        fuel: "—".into(),
        active: active.iter().map(|s| s.to_string()).collect(),
        prev: prev.iter().map(|s| s.to_string()).collect(),
    }
}

#[test]
fn diff_needs_both_snapshots() {
    let a = snap("10:00:00", "A before", 800.0, true, &[], &[]);
    assert_eq!(diff(None, None), "Need Snapshot A and Snapshot B first.");
    assert_eq!(
        diff(Some(&a), None),
        "Need Snapshot A and Snapshot B first."
    );
    assert_eq!(
        diff(None, Some(&a)),
        "Need Snapshot A and Snapshot B first."
    );
}

#[test]
fn diff_shows_a_repair() {
    let a = snap(
        "10:00:00",
        "A before",
        812.4,
        true,
        &["SPN 1761 FMI 9", "SPN 100 FMI 1"],
        &[],
    );
    let b = snap(
        "10:20:05",
        "B after",
        -1.0,
        false,
        &["SPN 100 FMI 1"],
        &["SPN 1761 FMI 9"],
    );
    assert_eq!(
        diff(Some(&a), Some(&b)),
        [
            "BEFORE  10:00:00  (A before)",
            "AFTER   10:20:05  (B after)",
            "",
            "RPM     812  →  —",
            "Red     ON  →  off",
            "Amber   off  →  off",
            "DEF     62 %  →  62 %",
            "",
            "Active codes gone (in A, not B):",
            "  SPN 1761 FMI 9",
            "Active codes new (in B, not A):",
            "  (none)",
            "Unchanged active:",
            "  SPN 100 FMI 1",
            "",
            "Previously-active gone (in A, not B):",
            "  (none)",
            "Previously-active new (in B, not A):",
            "  SPN 1761 FMI 9",
            "",
        ]
        .join("\r\n")
    );
}

#[test]
fn take_copies_readings_and_codes() {
    let s = BusSnapshot {
        rpm: f64::NAN,
        coolant_c: 81.6,
        battery_v: 13.456,
        fuel_lph: f64::NAN,
        red: true,
        def_text: "55 %".into(),
        oil_text: "310 kPa".into(),
        active: vec![Dtc {
            spn: 1761,
            fmi: 9,
            occ: 1,
            name: "DEF tank level".into(),
            fmi_text: "abnormal update rate".into(),
        }],
        ..Default::default()
    };
    let t = Snap::take(&s, "09:15:00", "A before");
    assert_eq!(t.rpm, -1.0);
    assert_eq!(
        (
            t.coolant.as_str(),
            t.batt.as_str(),
            t.fuel.as_str(),
            t.oil.as_str(),
            t.def.as_str()
        ),
        ("82 C", "13.46 V", "—", "310 kPa", "55 %")
    );
    assert_eq!(
        t.active,
        ["SPN 1761  FMI 9  occ 1  abnormal update rate  DEF tank level"]
    );
    assert!(t.prev.is_empty());
}

#[test]
fn bus_lists_read_like_the_winforms_bus_tab() {
    use tb_core::j1939::bam::J1939Frame;
    use tb_core::j1939::monitor::BusMonitor;
    let mut mon = BusMonitor::new();
    let f = |pgn, sa, data: &[u8]| J1939Frame {
        pgn,
        sa,
        da: 255,
        data: data.to_vec(),
    };
    mon.feed(&f(0xF004, 0, &[0; 8]), 1_000);
    mon.feed(&f(0xF004, 0, &[0; 8]), 2_000);
    mon.feed(&f(0xEE00, 48, &[1, 2, 3, 4, 5, 6, 7, 8]), 2_000);
    assert_eq!(
        mon.pgn_lines(3_500),
        vec![
            "EE00  SA48  Address claim          x1     1.5s ago".to_string(),
            "F004  SA0   EEC1 RPM               x2     1.5s ago".to_string(),
        ]
    );
    assert_eq!(
        mon.sa_lines(3_500),
        vec![
            "SA 0   ECM 1                    LIVE  x2  1.5s ".to_string(),
            "SA 48  Compressor controller    LIVE  x1  1.5s CLAIM".to_string(),
        ]
    );
    assert_eq!(
        mon.sa_lines(15_000)[0],
        "SA 0   ECM 1                    gone  x2  13.0s "
    );
    assert_eq!(
        mon.bus_csv(),
        "pgn,pgn_hex,name,sa,frames,last_len\r\n\
         60928,EE00,\"Address claim\",48,1,8\r\n\
         61444,F004,\"EEC1 RPM\",0,2,8\r\n\
         \r\n\
         sa,module,frames,claimed,name_hex\r\n\
         0,\"SA 0 ECM 1\",2,no,\r\n\
         48,\"SA 48 Compressor controller\",1,yes,01-02-03-04-05-06-07-08\r\n"
    );
}
