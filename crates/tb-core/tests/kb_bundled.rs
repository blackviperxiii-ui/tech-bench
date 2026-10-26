//! The web build's KB: the shipped JSON handed in as text, no folder and no password DB. Runs
//! on wasm32 too, since nothing here touches the disk.

#[cfg(target_arch = "wasm32")]
use wasm_bindgen_test::wasm_bindgen_test as test;

use tb_core::kb::KbIndex;

const KB: &str = include_str!("../../../kb/data/kb.json");
const MANUALS: &str = include_str!("../../../kb/data/usb-manuals.json");
const FILTERS: &str = include_str!("../../../kb/data/rental-portable-filters-oil.json");
const EQUIP: &str = include_str!("../../../kb/data/rental-equipment-info.json");

fn shipped() -> KbIndex {
    let mut kb = KbIndex::new();
    kb.load_bundled(
        "(web)",
        &[
            ("kb.json", KB),
            ("usb-manuals.json", MANUALS),
            ("rental-portable-filters-oil.json", FILTERS),
            ("rental-equipment-info.json", EQUIP),
        ],
    );
    kb
}

#[test]
fn bundled_kb_matches_the_folder_load_without_passwords() {
    let kb = shipped();
    assert_eq!(
        kb.status,
        "Codes 155  ·  Passwords 0  ·  Manuals 2147  ·  Filters 63  ·  Equipment 37"
    );
    assert_eq!(kb.root, "(web)");
    assert!(kb.errors.is_empty(), "{:?}", kb.errors);
    let (hits, total) = kb.search("1AVPT", "CODE", 5);
    assert_eq!(total, 1);
    assert_eq!(hits[0].title, "ingersoll-rand  01  —  Sensor Failure 1AVPT");
    let (_, total) = kb.search("", "PASSWORD", 5);
    assert_eq!(total, 0, "no password entries in a bundled load");
    let (hits, _) = kb.search("F68", "FILTER", 5);
    assert!(hits[0].title.to_lowercase().contains("f68"));
}

#[test]
fn a_broken_bundled_file_costs_only_that_section() {
    let mut kb = KbIndex::new();
    kb.load_bundled("(web)", &[("kb.json", KB), ("usb-manuals.json", "{ nope")]);
    assert!(
        kb.status
            .starts_with("Codes 155  ·  Passwords 0  ·  Manuals 0  ·  Filters 0  ·  Equipment 0  ·  1 file(s) failed: usb-manuals.json ("),
        "{}",
        kb.status
    );
    assert_eq!(kb.search("1AVPT", "CODE", 5).1, 1);
}
