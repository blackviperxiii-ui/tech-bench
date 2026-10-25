//! Ported from tests/SelfTest.Adapter.cs NameTests and tests/SelfTest.Session.cs SaLabel cases.

use std::collections::HashMap;
use tb_core::j1939::decode::parse_dm;
use tb_core::j1939::Names;

fn map(pairs: &[(i32, &str)]) -> Option<HashMap<i32, String>> {
    Some(pairs.iter().map(|(k, v)| (*k, v.to_string())).collect())
}

#[test]
fn name_overrides() {
    let mut names = Names::new();
    assert_eq!(names.spn(1761), "DEF tank level");
    assert_eq!(names.sa(48), "Compressor controller");
    assert_eq!(names.pgn(0xFECA), "DM1 active DTCs");
    assert_eq!(names.spn(999999), "");
    assert_eq!(names.sa(77), "SA 77");

    names.load(
        map(&[(1761, "DEF level (shop wording)"), (4001, "Shop-added SPN")]),
        None,
        map(&[(77, "Doosan aux module")]),
    );
    assert_eq!(names.spn(1761), "DEF level (shop wording)");
    assert_eq!(names.spn(4001), "Shop-added SPN");
    assert_eq!(names.sa(77), "Doosan aux module");
    assert_eq!(names.spn(5246), "SCR operator inducement severity");
    // C# "SpnName delegates to Names": a DM1 DTC picks up the override name.
    let mut dtcs = Vec::new();
    parse_dm(&[0x00, 0xFF, 0xA1, 0x0F, 0x02, 0x01], &mut dtcs, &names);
    assert_eq!(dtcs[0].spn, 4001);
    assert_eq!(dtcs[0].name, "Shop-added SPN");
    names.reset();
    assert_eq!(names.spn(1761), "DEF tank level");
    assert_eq!(names.spn(4001), "");
}

#[test]
fn sa_label_and_override_count() {
    let mut names = Names::new();
    assert_eq!(names.sa_label(0), "SA 0 ECM 1");
    assert_eq!(names.sa_label(77), "SA 77");
    assert_eq!(names.override_count(), 0);
    names.load(None, None, map(&[(77, "Shop ECU")]));
    assert_eq!(names.override_count(), 1);
    assert_eq!(names.sa_label(77), "SA 77 Shop ECU");
    names.reset();
    assert_eq!(names.override_count(), 0);
    assert_eq!(names.sa_label(77), "SA 77");
}

#[test]
fn empty_override_falls_back_to_built_in() {
    let mut names = Names::new();
    names.load(map(&[(1761, "")]), None, None);
    assert_eq!(names.spn(1761), "DEF tank level");
}
