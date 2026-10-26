//! Ported from SelfTest.cs: "trend log" (110), "fault timeline" (120), "bus monitor routing" (130),
//! "disconnected snapshot" (140), "TSC1 destination" (150), the monitor half of AftertreatmentTests,
//! and tests/SelfTest.Adapter.cs "RP1210 adapter discovery" (90). Same inputs and literal values.

#[cfg(target_arch = "wasm32")]
use wasm_bindgen_test::wasm_bindgen_test as test;

use tb_core::ini::Ini;
use tb_core::j1939::clear::{tsc1_dest, tsc1_tx, TOOL_NAME};
use tb_core::j1939::monitor::{BusMonitor, BusSnapshot};
use tb_core::j1939::timeline::{DtcEvent, DtcTimeline};
use tb_core::j1939::trend::{TrendChannel, TrendLog, TrendSample};
use tb_core::j1939::{Dtc, J1939Frame, Names};
use tb_core::rp1210::Rp1210Api;

const T_NOON: i64 = 1_767_268_800_000; // 2026-01-01 12:00 UTC
const T_EIGHT: i64 = 1_767_254_400_000; // 2026-01-01 08:00 UTC
const T_NINE: i64 = 1_767_258_000_000; // 2026-01-01 09:00 UTC
const T_MIDNIGHT: i64 = 1_767_225_600_000; // 2026-01-01 00:00 UTC

fn sample(t: i64, rpm: f64) -> TrendSample {
    TrendSample {
        time: t,
        rpm,
        ..TrendSample::default()
    }
}

#[test]
fn trend_log() {
    let mut log = TrendLog::new(4);
    for i in 0..4 {
        log.add(sample(T_NOON + i * 1000, 100.0 + i as f64));
    }
    assert_eq!(log.len(), 4, "fills to capacity");
    assert_eq!(log.get(0).unwrap().rpm, 100.0, "oldest first");
    log.add(sample(T_NOON + 4000, 104.0));
    assert_eq!(log.len(), 4, "ring buffer stays at capacity");
    assert_eq!(log.get(0).unwrap().rpm, 101.0, "oldest sample rolled off");
    assert_eq!(log.get(3).unwrap().rpm, 104.0, "newest sample kept");

    let mut rate = TrendLog::new(100);
    assert!(rate.offer(sample(T_NOON, 800.0)), "first offer accepted");
    assert!(
        !rate.offer(sample(T_NOON + 300, 810.0)),
        "second offer within the same second rejected"
    );
    assert!(
        rate.offer(sample(T_NOON + 1000, 820.0)),
        "offer a second later accepted"
    );
    assert_eq!(rate.len(), 2, "only two samples taken");

    let copy = rate.copy();
    assert_eq!(copy.len(), 2, "copy length");
    rate.clear();
    assert_eq!(rate.len(), 0, "clear empties");
    assert_eq!(copy.len(), 2, "copy is detached from the log");

    let mixed = vec![
        sample(T_NOON, 500.0),
        sample(T_NOON + 1000, f64::NAN),
        sample(T_NOON + 2000, 1500.0),
    ];
    assert_eq!(
        TrendLog::range(&mixed, TrendChannel::Rpm),
        Some((500.0, 1500.0)),
        "range ignores missing values"
    );
    assert_eq!(
        TrendLog::range(&mixed, TrendChannel::Coolant),
        None,
        "range reports nothing for a never-published channel"
    );

    let csv = TrendLog::csv(&mixed);
    let lines: Vec<&str> = csv.trim().split("\r\n").collect();
    assert_eq!(lines.len(), 4, "csv header + one row per sample");
    assert!(
        lines[0].starts_with("time,rpm,coolant_c"),
        "csv header is stable"
    );
    assert!(
        lines[2].contains(",,"),
        "missing value is an empty cell, not NaN"
    );
    assert_eq!(
        lines[1], "2026-01-01T12:00:00.0000000Z,500,,,,,0,0,0",
        "csv row matches the C# round-trip time format"
    );
    assert_eq!(
        TrendLog::label(TrendChannel::Oil),
        "Oil kPa",
        "channel label"
    );
}

fn dtcs(spn_fmi: &[i32]) -> Vec<Dtc> {
    let names = Names::new();
    spn_fmi
        .chunks(2)
        .map(|p| Dtc {
            spn: p[0],
            fmi: p[1],
            occ: 1,
            name: names.spn(p[0]),
            fmi_text: String::new(),
        })
        .collect()
}

#[test]
fn fault_timeline() {
    let mut tl = DtcTimeline::default();
    let t = T_EIGHT;

    tl.update(Some(&dtcs(&[1761, 9])), t, 0.0);
    assert_eq!(tl.len(), 1, "first code recorded");
    assert_eq!(tl.in_order()[0].rpm_at_first, 0.0, "rpm at first captured");

    tl.update(Some(&dtcs(&[1761, 9, 5246, 0])), t + 4000, 180.0);
    assert_eq!(tl.len(), 2, "second code added");
    let induce = &tl.in_order()[1];
    assert_eq!(induce.rpm_at_first, 180.0, "inducement rpm at first");
    assert_eq!(induce.first_seen - t, 4000, "inducement first seen 4s in");

    // Dropping out marks cleared but must not delete the finding.
    tl.update(Some(&dtcs(&[1761, 9])), t + 10_000, 800.0);
    assert_eq!(tl.len(), 2, "cleared code retained");
    assert!(!tl.in_order()[1].active, "inducement marked cleared");
    assert!(tl.in_order()[0].active, "tank fault still active");

    // Coming back counts as another cycle rather than a new row.
    tl.update(Some(&dtcs(&[1761, 9, 5246, 0])), t + 20_000, 1200.0);
    assert_eq!(tl.len(), 2, "no duplicate row on return");
    assert_eq!(tl.in_order()[1].cycles, 2, "cycle counted");
    assert_eq!(
        tl.in_order()[1].rpm_at_first,
        1200.0,
        "rpm at return recorded"
    );

    tl.update(None, t + 30_000, 0.0);
    assert!(
        !tl.in_order()[0].active && !tl.in_order()[1].active,
        "null list clears everything without throwing"
    );
    let recent = tl.recent();
    assert!(
        recent[0].last_seen >= recent[1].last_seen,
        "recent is newest-first"
    );

    let csv = tl.csv("HP450 123");
    assert!(
        csv.starts_with("first_seen,last_seen,job,spn,fmi"),
        "csv has a header"
    );
    assert!(csv.contains("\"HP450 123\""), "csv quotes the job");
    assert_eq!(csv.trim().split("\r\n").count(), 3, "csv row count");

    let e = DtcEvent {
        first_seen: t,
        last_seen: t + 75_000,
        ..DtcEvent::default()
    };
    assert_eq!(e.duration(), "1m15s", "duration formatting");
    assert_eq!(
        tl.in_order()[1].to_string(),
        "08:00:20  cleared SPN 5246   FMI 0   <1s   @1200  RPM  SCR operator inducement severity  (x2)",
        "event line"
    );
    tl.clear();
    assert_eq!(tl.len(), 0, "clear empties the timeline");
}

fn f(pgn: i32, sa: i32, data: &[u8]) -> J1939Frame {
    J1939Frame {
        pgn,
        sa,
        da: 255,
        data: data.to_vec(),
    }
}

fn cm(sa: i32, size: usize, pkts: u8, pgn: i32) -> J1939Frame {
    f(
        0xEC00,
        sa,
        &[
            0x20,
            (size & 0xFF) as u8,
            (size >> 8) as u8,
            pkts,
            0xFF,
            (pgn & 0xFF) as u8,
            ((pgn >> 8) & 0xFF) as u8,
            ((pgn >> 16) & 0xFF) as u8,
        ],
    )
}

fn dt(sa: i32, seq: u8, payload: &[u8]) -> J1939Frame {
    let mut d = [0xFFu8; 8];
    d[0] = seq;
    for (i, b) in payload.iter().take(7).enumerate() {
        d[i + 1] = *b;
    }
    f(0xEB00, sa, &d)
}

const EEC1_800: [u8; 8] = [0, 0, 0, 0x00, 0x19, 0, 0, 0];

#[test]
fn bus_monitor_routing() {
    let t = T_NINE;
    let mut m = BusMonitor::new();

    // Engine SA is discovered from whoever broadcasts EEC1, instead of assuming 0.
    m.feed(&f(0xF004, 17, &EEC1_800), t);
    assert_eq!(m.engine_sa(), 17, "engine SA auto-detected from EEC1");
    assert_eq!(m.rpm, 800.0, "rpm decoded for that SA");

    m.feed(&f(0xFEEE, 17, &[130, 0, 0, 0, 0, 0, 0, 0]), t);
    assert_eq!(m.coolant_c, 90.0, "coolant from the engine");
    m.feed(&f(0xFEEE, 99, &[200, 0, 0, 0, 0, 0, 0, 0]), t);
    assert_eq!(m.coolant_c, 90.0, "coolant from another module ignored");

    m.feed(&f(0xFEEF, 17, &[0, 0, 0, 100, 0, 0, 0, 0]), t);
    assert_eq!(m.oil_kpa, 400.0, "oil pressure decoded");
    assert_eq!(m.oil_text(), "400 kPa (58.0 psi)", "oil text has psi");

    m.feed(&f(0xFE56, 17, &[125, 60, 0, 0, 0, 0, 0, 0]), t);
    assert!(!m.def_silent, "DEF talking");
    assert_eq!(m.def_pct, 50.0, "DEF percent");
    m.feed(&f(0xFE56, 17, &[0xFF, 0xFF, 0, 0, 0, 0, 0, 0]), t);
    assert!(m.def_silent, "DEF silence detected");
    assert_eq!(m.def_text(), "n/a (not talking)", "DEF text when silent");
    assert!(
        m.aft_text().contains("DEF level SPN 1761: not available"),
        "silent tank leaves level as not available: {}",
        m.aft_text()
    );

    // DM1 from two different modules must both be kept.
    m.feed(
        &f(
            0xFECA,
            17,
            &[0x10, 0xFF, 0x7E, 0x14, 0x00, 0x01, 0xFF, 0xFF],
        ),
        t,
    );
    m.feed(
        &f(
            0xFECA,
            48,
            &[0x04, 0xFF, 0xE1, 0x06, 0x09, 0x02, 0xFF, 0xFF],
        ),
        t,
    );
    assert_eq!(m.engine().active.len(), 1, "engine DM1 kept");
    assert!(m.engine().red, "engine red stop");
    assert_eq!(
        m.module(48).active.len(),
        1,
        "controller DM1 kept separately"
    );
    assert_eq!(m.module(48).active[0].spn, 1761, "controller SPN");
    assert!(m.module(48).amber, "controller amber");
    assert_eq!(
        m.engine().active[0].spn,
        5246,
        "engine not credited with the controller fault"
    );
    assert_eq!(m.modules_with_faults(), vec![17, 48], "both modules listed");
    assert!(m.has_spn(5246), "HasSpn sees the engine inducement");
    assert!(!m.has_spn(1761), "HasSpn does not leak across modules");

    // Pinning the engine SA overrides auto-detection.
    let mut pinned = BusMonitor::new();
    pinned.set_engine_sa_setting(0);
    pinned.feed(&f(0xF004, 17, &EEC1_800), t);
    assert_eq!(pinned.engine_sa(), 0, "pinned engine SA respected");
    assert!(
        pinned.rpm.is_nan(),
        "rpm from the wrong SA ignored when pinned"
    );

    // Identity PGNs the app requests must actually land.
    let mut id = BusMonitor::new();
    id.feed(&f(0xF004, 0, &EEC1_800), t);
    id.feed(&f(0xFEEC, 0, b"1ABC23456789*"), t);
    id.feed(&f(0xFEDA, 0, b"SW1234*"), t);
    id.feed(&f(0xFEEB, 0, b"CUMMINS*QSB6.7*555*"), t);
    id.feed(&f(0xFEE5, 0, &[0x72, 0x60, 0x00, 0x00, 0, 0, 0, 0]), t);
    assert_eq!(id.vin, "1ABC23456789", "VIN landed");
    assert_eq!(id.sw, "SW1234", "software landed");
    assert_eq!(
        id.comp_id, "CUMMINS  ·  QSB6.7  ·  555",
        "component id landed"
    );
    assert_eq!(id.hours_text, "1234.5 h", "hours landed");
    id.feed(&f(0xFEE5, 0, &[0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0]), t);
    assert_eq!(id.hours_text, "", "hours NA clears last reading");

    // J1939 NA/error sentinels must blank the HUD, not keep the last good sample.
    m.feed(&f(0xFEF7, 17, &[0, 0, 0, 0, 0x28, 0x02, 0, 0]), t);
    assert!((m.battery_v - 27.6).abs() < 1e-9, "battery decoded");
    m.feed(&f(0xFEF2, 17, &[0x46, 0x00, 0, 0, 0, 0, 0, 0]), t);
    assert!((m.fuel_lph - 3.5).abs() < 1e-9, "fuel decoded");
    m.feed(&f(0xFEEE, 17, &[0xFF, 0, 0, 0, 0, 0, 0, 0]), t);
    assert!(m.coolant_c.is_nan(), "coolant NA clears last reading");
    m.feed(&f(0xFEEF, 17, &[0, 0, 0, 0xFF, 0, 0, 0, 0]), t);
    assert!(m.oil_kpa.is_nan(), "oil NA clears last reading");
    m.feed(&f(0xFEF7, 17, &[0, 0, 0, 0, 0xFF, 0xFF, 0, 0]), t);
    assert!(m.battery_v.is_nan(), "battery NA clears last reading");
    m.feed(&f(0xFEF2, 17, &[0xFF, 0xFF, 0, 0, 0, 0, 0, 0]), t);
    assert!(m.fuel_lph.is_nan(), "fuel NA clears last reading");

    // Trend sampling is rate limited and follows the engine.
    let mut tr = BusMonitor::new();
    tr.feed(&f(0xF004, 0, &EEC1_800), t);
    assert!(tr.sample_trend(t), "first trend sample taken");
    assert!(
        !tr.sample_trend(t + 200),
        "second within the same second skipped"
    );
    assert!(tr.sample_trend(t + 1000), "sample a second later taken");
    assert_eq!(tr.trend().len(), 2, "two samples stored");
    assert_eq!(tr.trend().get(0).unwrap().rpm, 800.0, "sampled rpm");

    // Bus stats and clears.
    let rows = m.pgn_rows();
    assert!(!rows.is_empty(), "pgn rows tracked");
    assert!(
        rows[0].pgn <= rows[rows.len() - 1].pgn,
        "pgn rows sorted ascending"
    );
    m.clear_bus_stats();
    assert_eq!(m.pgn_rows().len(), 0, "bus stats cleared");
    m.clear_live();
    assert!(m.rpm.is_nan(), "live values cleared");
    assert_eq!(m.modules_with_faults().len(), 0, "modules cleared");

    // Long DM1 over BAM must route to the right module.
    let mut bam = BusMonitor::new();
    bam.feed(&f(0xF004, 0, &EEC1_800), t);
    bam.feed(&cm(0, 12, 2, 0xFECA), t);
    // The second DTC straddles the packet boundary: byte 6 is in packet 1, bytes 7-9 in packet 2.
    bam.feed(&dt(0, 1, &[0x10, 0xFF, 0x7E, 0x14, 0x00, 0x01, 0xE1]), t);
    let routed = bam.feed(&dt(0, 2, &[0x06, 0x09, 0x02, 0xFF, 0xFF]), t);
    assert!(routed, "multi-packet DM1 routed after reassembly");
    assert_eq!(bam.engine().active.len(), 2, "BAM DM1 produced two codes");
    assert_eq!(bam.engine().active[0].spn, 5246, "first code from packet 1");
    assert_eq!(
        bam.engine().active[1].spn,
        1761,
        "second code spanning the packet boundary"
    );
    assert_eq!(bam.engine().active[1].fmi, 9, "boundary-spanning FMI");

    // MarkCleared empties the list but must not blank lamps (HUD "healthy silent").
    let mut lamps = BusMonitor::new();
    lamps.feed(&f(0xF004, 0, &EEC1_800), t);
    lamps.feed(
        &f(0xFECA, 0, &[0x10, 0xFF, 0x7E, 0x14, 0x00, 0x01, 0xFF, 0xFF]),
        t,
    );
    assert!(lamps.engine().red, "pre-clear red stop");
    assert_eq!(lamps.engine().active.len(), 1, "pre-clear active count");
    lamps.mark_cleared(0, true, false, t);
    assert_eq!(
        lamps.engine().active.len(),
        0,
        "MarkCleared empties active list"
    );
    assert!(
        lamps.engine().red,
        "MarkCleared keeps red lamp until fresh DM1"
    );
}

#[test]
fn monitor_aftertreatment() {
    let low = 1u8 << 5;
    let severe = 5u8 << 5;
    let mut mon = BusMonitor::new();
    assert!(
        mon.aft_text().contains("DEF level SPN 1761: no data"),
        "monitor starts at no data"
    );
    mon.feed(
        &f(0xFE56, 0, &[125, 60, 0xFF, 0xFF, low, severe, 0, 0]),
        T_MIDNIGHT,
    );
    assert!(
        mon.aft_text().contains("DEF level SPN 1761: 50.0%"),
        "monitor shows 50 percent"
    );
    assert!(
        mon.aft_text().contains("level 5 — final inducement"),
        "monitor shows final inducement"
    );
    mon.clear_live();
    assert!(
        mon.aft_text().contains("SCR inducement SPN 5246: no data"),
        "clear live returns no data"
    );
}

#[test]
fn disconnected_snapshot() {
    let mut s = BusSnapshot {
        rpm: 800.0,
        coolant_c: 90.0,
        oil_kpa: 400.0,
        battery_v: 27.6,
        fuel_lph: 3.5,
        def_text: "50.0%  temp 20 C".into(),
        oil_text: "400 kPa".into(),
        vin: "1ABC".into(),
        sw: "SW".into(),
        comp_id: "CUMMINS".into(),
        hours: "10 h".into(),
        red: true,
        amber: true,
        protect: true,
        mil: true,
        has_spn_5246: true,
        has_tank_fmi9: true,
        has_1569: true,
        ..BusSnapshot::default()
    };
    s.active.push(dtcs(&[5246, 0]).remove(0));
    s.prev.push(dtcs(&[100, 1]).remove(0));
    s.fault_modules.push(0);
    s.blank_disconnected_readouts();
    assert!(s.rpm.is_nan(), "disconnected snapshot blanks rpm");
    assert!(s.coolant_c.is_nan(), "disconnected snapshot blanks coolant");
    assert_eq!(s.def_text, "—", "disconnected snapshot blanks DEF");
    assert!(
        s.aft_text.contains("no data"),
        "disconnected snapshot restores no-data aftertreatment"
    );
    assert_eq!(s.vin, "", "disconnected snapshot blanks VIN");
    assert!(!s.red, "disconnected snapshot clears lamps");
    assert_eq!(
        s.active.len(),
        0,
        "disconnected snapshot clears active list"
    );
    assert_eq!(
        s.fault_modules.len(),
        0,
        "disconnected snapshot clears modules"
    );
}

#[test]
fn tsc1_destination() {
    assert_eq!(tsc1_dest(17), 17, "TSC1 dest follows engine SA 17");
    assert_eq!(tsc1_dest(0), 0, "TSC1 dest SA 0 stays 0");
    assert_eq!(tsc1_dest(-1), 0, "TSC1 dest auto-unknown falls back to 0");
    assert_eq!(tsc1_dest(255), 0, "TSC1 dest broadcast SA rejected");

    let hold = tsc1_tx(1200, 17);
    assert_eq!(
        hold.rp1210,
        vec![0x00, 0x00, 0x00, 3, 0xF9, 17, 0x01, 0x80, 0x25, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
        "TSC1 1200 RPM wire bytes (raw 9600 = 0x2580)"
    );
    assert_eq!(
        tsc1_tx(0, 17).data[0],
        0x00,
        "TSC1 release is override disabled"
    );
    assert_eq!(
        TOOL_NAME[5], 129,
        "tool NAME function is off-board service tool (129)"
    );
    assert!(
        TOOL_NAME[7] & 0x80 != 0,
        "tool NAME is arbitrary-address capable"
    );
}

const VENDOR_INI: &str = "
[VendorInformation]
Name=Cummins Inc. INLINE 7
Devices=1,2
Protocols=1,2
; a comment line
[DeviceInformation1]
DeviceID=1
DeviceName=INLINE7-USB
DeviceDescription=\"INLINE 7 over USB\"
[DeviceInformation2]
DeviceID=2
DeviceName=INLINE7-BT
DeviceDescription=INLINE 7 Bluetooth
[ProtocolInformation1]
ProtocolString=J1939
ProtocolDescription=SAE J1939
ProtocolSpeed=250,500
[ProtocolInformation2]
ProtocolString=ISO15765
ProtocolDescription=ISO 15765
";

#[test]
fn rp1210_adapter_discovery() {
    let api = Rp1210Api::from_ini(
        "CMNSI7",
        &Ini::parse(Some(VENDOR_INI)),
        r"C:\Windows\SysWOW64\CMNSI7.DLL",
    );
    assert_eq!(api.name, "Cummins Inc. INLINE 7", "vendor name");
    assert_eq!(api.devices.len(), 2, "device count");
    assert_eq!(
        api.device_ids(),
        vec![1, 2],
        "device ids from INI, not hardcoded"
    );
    assert_eq!(
        api.devices[0].to_string(),
        "1 — INLINE 7 over USB",
        "device label"
    );
    assert_eq!(
        api.j1939_protocols[0], "J1939:Baud=250",
        "250 baud offered first"
    );
    assert!(
        api.j1939_protocols.iter().any(|p| p == "J1939:Baud=500"),
        "500 baud also offered"
    );
    assert_eq!(
        api.j1939_protocols,
        vec!["J1939:Baud=250", "J1939:Baud=500", "J1939"],
        "full protocol list"
    );
    assert!(api.supports_iso15765, "ISO15765 detected");
    assert!(api.looks_like_inline7(), "recognised as INLINE 7");

    // A vendor INI with no protocol section still has to produce something usable.
    let bare = Rp1210Api::from_ini(
        "OTHER",
        &Ini::parse(Some("[VendorInformation]\nName=Some Adapter\n")),
        "",
    );
    assert_eq!(
        bare.j1939_protocols[0], "J1939:Baud=250",
        "bare vendor falls back to J1939:Baud=250"
    );
    assert_eq!(
        bare.device_ids(),
        vec![1, 2, 111],
        "bare vendor falls back to known device ids"
    );
    assert!(
        !bare.looks_like_inline7(),
        "bare vendor not mistaken for INLINE 7"
    );
    assert!(
        !bare.supports_iso15765,
        "no ISO15765 claimed when not advertised"
    );

    let apis = vec![bare.clone(), api.clone()];
    assert_eq!(
        Rp1210Api::preferred(&apis),
        Some(&api),
        "INLINE 7 preferred over another adapter"
    );
    assert!(
        api.describe().contains("CMNSI7.DLL"),
        "Describe mentions the DLL"
    );
    assert_eq!(
        Rp1210Api::preferred(&[]),
        None,
        "Preferred on empty list is null"
    );
}

#[cfg(not(target_arch = "wasm32"))]
mod fs_tests {
    use super::*;
    use std::fs;

    #[test]
    fn installed_reads_the_master_ini() {
        let dir = std::env::temp_dir().join(format!("tb-rp1210-{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(dir.join("System32")).unwrap();
        assert!(
            Rp1210Api::installed(&dir).is_empty(),
            "Installed() with no RP121032.INI is empty, not an error"
        );
        fs::write(
            dir.join("RP121032.INI"),
            "[RP1210Support]\r\nAPIImplementations=OTHER,CMNSI7,MISSING\r\n",
        )
        .unwrap();
        fs::write(dir.join("CMNSI7.INI"), VENDOR_INI).unwrap();
        fs::write(
            dir.join("OTHER.INI"),
            "[VendorInformation]\r\nName=Some Adapter\r\n",
        )
        .unwrap();
        fs::write(dir.join("System32").join("CMNSI7.DLL"), b"MZ").unwrap();
        let apis = Rp1210Api::installed(&dir);
        assert_eq!(
            apis.iter().map(|a| a.id.as_str()).collect::<Vec<_>>(),
            vec!["OTHER", "CMNSI7"],
            "INI order kept, a vendor with no INI skipped"
        );
        assert!(
            apis[1].dll_path.ends_with("CMNSI7.DLL"),
            "DLL found in System32"
        );
        let sorted = Rp1210Api::adapters(&dir);
        assert_eq!(
            sorted[0].id, "CMNSI7",
            "adapters() lists the INLINE 7 first"
        );
        assert_eq!(
            Rp1210Api::preferred(&sorted).map(|a| a.id.as_str()),
            Some("CMNSI7"),
            "preferred is the INLINE 7 with a DLL"
        );
        fs::remove_dir_all(&dir).unwrap();
    }
}
