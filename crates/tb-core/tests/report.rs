//! Ported from tests/SelfTest.Job.cs "job report" and tests/SelfTest.Session.cs "Session base
//! name", plus the exact session files `Inline7Control.SaveSessionCore` writes.

#[cfg(target_arch = "wasm32")]
use wasm_bindgen_test::wasm_bindgen_test as test;

use tb_core::j1939::monitor::{BusMonitor, SaRow};
use tb_core::j1939::report::{base_name, lines, session_files, text, LocalNow, ReportData};
use tb_core::j1939::{Dtc, Names};

fn dtc(spn: i32, fmi: i32) -> Dtc {
    Dtc {
        spn,
        fmi,
        occ: 1,
        name: Names::new().spn(spn),
        fmi_text: String::new(),
    }
}

const STAMP: &str = "20260501_143000";

#[test]
fn job_report() {
    let mut d = ReportData {
        when: "2026-05-01 14:30".into(),
        job_tag: "HP450 12345".into(),
        adapter: "Cummins Inc. INLINE 7  (CMNSI7)".into(),
        vin: "1ABC".into(),
        sw: "SW9".into(),
        comp_id: "CUMMINS".into(),
        hours: "1234.5 h".into(),
        rpm: 800.0,
        red: true,
        amber: true,
        def: "n/a (not talking)".into(),
        coolant: "90 C".into(),
        oil: "400 kPa".into(),
        batt: "27.6 V".into(),
        fuel: "3.5 L/h".into(),
        history_note: "This unit has been here before".into(),
        ..ReportData::default()
    };
    d.active.push(dtc(5246, 0));
    d.prev.push(dtc(1761, 9));
    d.markers.push("09:00:01  CRANK  RPM=180".into());

    let l = lines(&d);
    assert_eq!(l[0], "TECH BENCH — DIAGNOSTIC REPORT");
    assert_eq!(l[2], "Job:      HP450 12345");
    assert_eq!(l[3], "Date:     2026-05-01 14:30");
    assert_eq!(l[4], "Adapter:  Cummins Inc. INLINE 7  (CMNSI7)");
    let t = l.join("\n");
    assert!(t.contains("  Engine hours: 1234.5 h"), "{t}");
    assert!(t.contains("  RPM:      800\n"), "{t}");
    assert!(
        t.contains("  Lamps:    Red Stop ON   Amber ON   Protect off   MIL off"),
        "{t}"
    );
    assert!(
        t.contains("ACTIVE FAULTS (DM1)\n  SPN 5246  FMI 0  occ 1  "),
        "{t}"
    );
    assert!(
        t.contains("PREVIOUSLY ACTIVE (DM2)\n  SPN 1761  FMI 9  occ 1  "),
        "{t}"
    );
    assert!(t.contains("MARKERS\n  09:00:01  CRANK  RPM=180\n"), "{t}");
    assert!(
        t.contains("UNIT HISTORY\n  This unit has been here before\n"),
        "{t}"
    );
    assert!(t.contains("Signature"));
    assert!(t.contains("disabled to produce this report."));
    assert!(!t.contains("Need Snapshot"));

    d.diff = "Need Snapshot A and Snapshot B first.".into();
    assert!(!lines(&d).join("\n").contains("BEFORE / AFTER"));
}

#[test]
fn empty_report_text() {
    let e = text(&ReportData {
        when: "2026-05-01 14:30".into(),
        ..ReportData::default()
    });
    let want = [
        "TECH BENCH — DIAGNOSTIC REPORT",
        "================================================================",
        "Job:      (not entered)",
        "Date:     2026-05-01 14:30",
        "",
        "ENGINE IDENTITY",
        "  VIN / NAME:  (not published)",
        "  Software:    (not published)",
        "  Component:   (not published)",
        "  Engine hours: (not published)",
        "",
        "READINGS AT REPORT TIME",
        "  RPM:      —",
        "  Lamps:    Red Stop off   Amber off   Protect off   MIL off",
        "  DEF:      —",
        "  Coolant:  —     Oil: —",
        "  Battery:  —     Fuel rate: —",
        "",
        "ACTIVE FAULTS (DM1)",
        "  (none)",
        "",
        "PREVIOUSLY ACTIVE (DM2)",
        "  (none)",
        "",
        "----------------------------------------------------------------",
        "Red Stop and Amber are DM1 lamp bits, not separate codes. They go out when the",
        "faults that set them are gone. No DEF/SCR or engine-protection function was",
        "disabled to produce this report.",
        "",
        "Tech: ______________________    Signature: ______________________",
    ]
    .iter()
    .map(|l| format!("{l}\r\n"))
    .collect::<String>();
    assert_eq!(e, want);

    let cleared = text(&ReportData {
        code_clear:
            "CODE CLEAR — reset all codes\r\nRESULT: cleared — no active codes came back.\r\n"
                .into(),
        ..ReportData::default()
    });
    assert!(cleared.contains(
        "LAST CODE CLEAR\r\n  CODE CLEAR — reset all codes\r\n  RESULT: cleared — no active codes came back.\r\n\r\n"
    ));
}

#[test]
fn session_base_names() {
    let b = |tag: &str| base_name(tag, STAMP);
    assert_eq!(b(""), "session_20260501_143000");
    assert_eq!(b("  \t "), "session_20260501_143000");
    assert_eq!(b("!!!"), "session_20260501_143000");
    assert_eq!(b("___"), "session_20260501_143000");
    assert_eq!(b("HP450 12345"), "HP450_12345_session_20260501_143000");
    assert_eq!(b("a-b.c/d"), "a_b_c_d_session_20260501_143000");
    assert_eq!(b("  ab  "), "ab_session_20260501_143000");
    assert_eq!(b("--ab--"), "ab_session_20260501_143000");
    assert_eq!(b("a!!b"), "a__b_session_20260501_143000");
    assert_eq!(
        b(&"A".repeat(45)),
        format!("{}_session_20260501_143000", "A".repeat(40))
    );
    assert_eq!(
        b(&"B".repeat(40)),
        format!("{}_session_20260501_143000", "B".repeat(40))
    );
}

#[test]
fn session_files_match_save_session() {
    let mut s = BusMonitor::new().snapshot(-1);
    s.connected = true;
    s.adapter_name = "Fake adapter  (FAKE)".into();
    s.device_id = 1;
    s.protocol = "J1939:Baud=250".into();
    s.rpm = 799.6;
    s.amber = true;
    s.coolant_c = 90.0;
    s.def_text = "50.0%".into();
    s.oil_text = "—".into();
    s.active = vec![dtc(1761, 9)];
    s.prev = vec![dtc(100, 1)];
    let sas = [
        SaRow {
            sa: 48,
            count: 7,
            last: 0,
            name_hex: String::new(),
            claimed: false,
        },
        SaRow {
            sa: 0,
            count: 12,
            last: 0,
            name_hex: String::new(),
            claimed: true,
        },
    ];
    let now = LocalNow {
        local: "2026-05-01 14:30:00".into(),
        stamp: STAMP.into(),
        iso: "2026-05-01T14:30:00.0000000-05:00".into(),
    };
    let f = session_files(
        &s,
        &sas,
        &Names::new(),
        &[],
        "trend".into(),
        "timeline".into(),
        "WO 7-01",
        &now,
    );
    assert_eq!(f.base, "WO_7_01_session_20260501_143000");
    let body: Vec<&str> = f.body.split("\r\n").collect();
    assert_eq!(body[0], "Job  WO 7-01");
    assert_eq!(body[1], "Tech Bench session  2026-05-01 14:30:00");
    assert_eq!(
        body[2],
        "Adapter Fake adapter  (FAKE)  device 1  J1939:Baud=250  connected=True"
    );
    assert_eq!(body[3], "Engine SA 0  showing SA 0");
    assert_eq!(
        body[4],
        "RPM 800  Red Stop off  Amber ON  Protect off  MIL off"
    );
    assert_eq!(body[5], "DEF 50.0%");
    assert_eq!(body[6], "Coolant 90 C  Oil —  Battery —  Fuel —");
    let tail = &body[body.len() - 4..];
    assert_eq!(
        tail,
        [
            "Modules:",
            &format!("  SA 0 {} x12", Names::new().sa(0)),
            &format!("  SA 48 {} x7", Names::new().sa(48)),
            ""
        ]
    );
    assert_eq!(
        f.readings_csv,
        "time,job,rpm,red,amber,protect,mil,def,coolant,oil,battery,fuel,active_count,previous_count\r\n\
         2026-05-01T14:30:00.0000000-05:00,\"WO 7-01\",800,False,True,False,False,\"50.0%\",\"90 C\",\"—\",\"—\",\"—\",1,1\r\n"
    );
    let name = Names::new().spn(1761);
    assert_eq!(
        f.dtc_csv.lines().nth(1).unwrap(),
        format!("2026-05-01T14:30:00.0000000-05:00,\"WO 7-01\",active,1761,9,1,\"{name}\",\"\"")
    );
    assert!(f
        .dtc_csv
        .lines()
        .nth(2)
        .unwrap()
        .contains(",previous,100,1,1,"));
    assert_eq!(f.trend_csv, "trend");
    assert_eq!(f.timeline_csv, "timeline");
}
