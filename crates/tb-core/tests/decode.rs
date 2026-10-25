//! Ported from tests/SelfTest.Decode.cs and SelfTest.cs (AftertreatmentTests, NormalizePgn).
//! Same inputs, same literal expected values as the C# SelfTest.

use tb_core::j1939::decode::*;
use tb_core::j1939::Names;

fn lamp_case(b0: u8, red: bool, amber: bool, protect: bool, mil: bool) {
    let mut list = Vec::new();
    let l = parse_dm(
        &[b0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
        &mut list,
        &Names::new(),
    );
    assert_eq!(
        l,
        Lamps {
            red,
            amber,
            protect,
            mil
        },
        "byte0 {b0:#04x}"
    );
}

#[test]
fn dm1_lamp_decode() {
    lamp_case(0x00, false, false, false, false);
    lamp_case(0x04, false, true, false, false);
    lamp_case(0x10, true, false, false, false);
    lamp_case(0x01, false, false, true, false);
    lamp_case(0x40, false, false, false, true);
    lamp_case(0x14, true, true, false, false);
    lamp_case(0x55, true, true, true, true);
}

#[test]
fn dtc_unpack() {
    let names = Names::new();
    let mut dtcs = Vec::new();
    let l = parse_dm(
        &[0x10, 0xFF, 0x7E, 0x14, 0x00, 0x01, 0xFF, 0xFF],
        &mut dtcs,
        &names,
    );
    assert_eq!(dtcs.len(), 1, "one DTC parsed");
    assert_eq!(dtcs[0].spn, 5246);
    assert_eq!(dtcs[0].fmi, 0);
    assert_eq!(dtcs[0].occ, 1);
    assert_eq!(dtcs[0].name, "SCR operator inducement severity");
    assert!(l.red, "red stop set");

    dtcs.clear();
    let l = parse_dm(
        &[0x04, 0xFF, 0xE1, 0x06, 0x09, 0x02, 0xE1, 0x06, 0x09, 0x03],
        &mut dtcs,
        &names,
    );
    assert_eq!(dtcs.len(), 1, "duplicate SPN/FMI collapsed");
    assert_eq!(dtcs[0].spn, 1761);
    assert_eq!(dtcs[0].fmi, 9);
    assert!(l.amber, "amber set");
}

#[test]
fn dtc_display_matches_csharp_tostring() {
    let mut dtcs = Vec::new();
    parse_dm(
        &[0x10, 0xFF, 0x7E, 0x14, 0x00, 0x01],
        &mut dtcs,
        &Names::new(),
    );
    assert_eq!(
        dtcs[0].to_string(),
        "SPN 5246  FMI 0  occ 1  above normal (most severe)  SCR operator inducement severity"
    );
}

#[test]
fn scalar_decodes() {
    assert_eq!(
        format!("{:.0}", rpm(&[0, 0, 0, 0x00, 0x19, 0, 0, 0])),
        "800"
    );
    assert_eq!(rpm(&[0, 0, 0, 0xFF, 0xFF, 0, 0, 0]), -1.0);
    assert_eq!(rpm(&[0, 0]), -1.0);
    assert_eq!(temp_c(130), "90 C");
    assert_eq!(temp_c(0xFF), "n/a");
    assert_eq!(battery_v(&[0, 0, 0, 0, 0x28, 0x02, 0, 0]), "27.60 V");
    assert_eq!(battery_v(&[0, 0]), "n/a");
    assert_eq!(def_text(&[125, 60]), "50.0%  temp 20 C");
    assert_eq!(def_text(&[0xFF, 0xFF]), "n/a (not talking)");
    assert_eq!(
        hours(&[0x72, 0x60, 0x00, 0x00]).as_deref(),
        Some("1234.5 h")
    );
    assert_eq!(hours(&[0xFF, 0xFF, 0xFF, 0xFF]), None);
    assert_eq!(hours(&[0x01]), None);
}

#[test]
fn aftertreatment() {
    let empty = parse_aftertreatment(None);
    assert_eq!(empty.level, AftState::NO_DATA);
    assert_eq!(empty.temp, AftState::NO_DATA);
    assert_eq!(empty.severity, AftState::NO_DATA);
    assert_eq!(empty.low_lamp, AftState::NO_DATA);
    assert!(empty.text().contains("SPN 1761"), "{}", empty.text());
    assert!(empty.text().contains("SPN 5246"), "{}", empty.text());

    let short = parse_aftertreatment(Some(&[125, 60]));
    assert_eq!(short.level, "50.0%");
    assert_eq!(short.temp, "20 C");
    assert_eq!(short.severity, AftState::NO_DATA);
    assert_eq!(short.low_lamp, AftState::NO_DATA);

    let low: u8 = 1 << 5;
    let severe: u8 = 5 << 5;
    let full = parse_aftertreatment(Some(&[125, 60, 0xFF, 0xFF, low, severe]));
    assert_eq!(full.level, "50.0%");
    assert_eq!(full.temp, "20 C");
    assert_eq!(full.low_lamp, "on solid — DEF low");
    assert_eq!(full.severity, "level 5 — final inducement");

    let na = parse_aftertreatment(Some(&[0xFF, 0xFF]));
    assert_eq!(na.level, "not available");
    assert_eq!(na.temp, "not available");
    assert_eq!(na.severity, AftState::NO_DATA);

    assert_eq!(full.severity_raw, 5);
    assert_eq!(full.low_lamp_raw, 1);
    assert_eq!(AftState::attention_band(5, 0), 2);
    assert_eq!(AftState::attention_band(4, -1), 2);
    assert_eq!(AftState::attention_band(0, 4), 2);
    assert_eq!(AftState::attention_band(1, 0), 1);
    assert_eq!(AftState::attention_band(3, -1), 1);
    assert_eq!(AftState::attention_band(0, 1), 1);
    assert_eq!(AftState::attention_band(0, 0), 0);
    assert_eq!(AftState::attention_band(-1, -1), 0);
    assert_eq!(AftState::attention_band(7, 7), 0);

    assert_eq!(severity_5246(0), "not active");
    assert_eq!(severity_5246(7), "not available");
    assert_eq!(low_level_5245(4), "fast blink — DEF lower");
}

#[test]
fn ascii_component_id() {
    assert_eq!(ascii(b"1ABC23456789*", false), "1ABC23456789");
    assert_eq!(
        ascii(b"CUMMINS*QSB6.7*12345678*A1*", true),
        "CUMMINS  ·  QSB6.7  ·  12345678  ·  A1"
    );
    assert_eq!(ascii(&[], false), "");
}

#[test]
fn normalize_pgn_pdu1() {
    assert_eq!(normalize_pgn(0xE8F9), 0xE800);
    assert_eq!(normalize_pgn(0xECFF), 0xEC00);
    assert_eq!(normalize_pgn(0xFECA), 0xFECA);
    assert_eq!(normalize_pgn(0x1E8F9), 0x1E800);
}

#[test]
fn pressure_and_fuel_rate() {
    // 100 * 4 kPa = 400 kPa; 400 * 0.145 = 58.0 psi
    assert_eq!(pressure_kpa(100, 4), "400 kPa (58.0 psi)");
    assert_eq!(pressure_kpa(0xFB, 4), "n/a");
    // 0.05 L/h per bit: raw 500 -> 25.00 L/h
    assert_eq!(fuel_rate(&[0xF4, 0x01]), "25.00 L/h");
    assert_eq!(fuel_rate(&[0x00, 0xFB]), "n/a");
    assert_eq!(fuel_rate(&[0x01]), "n/a");
}
