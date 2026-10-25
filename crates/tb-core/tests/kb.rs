//! Ported from SelfTest.cs KbTests / SpnHitsTests / BundledKbTests and tests/SelfTest.Open.cs
//! (KB document path confinement). Same fixtures, same literal expected values.

use std::fs;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicUsize, Ordering};
use tb_core::kb::paths::{is_absolute_path, is_path_under_root, resolve_document_path};
use tb_core::kb::{Hit, KbIndex, MAX_RESULTS};

static SEQ: AtomicUsize = AtomicUsize::new(0);

/// A fresh empty directory under the system temp folder, removed on drop.
struct TempDir(PathBuf);

impl TempDir {
    fn new(tag: &str) -> Self {
        let n = SEQ.fetch_add(1, Ordering::SeqCst);
        let p = std::env::temp_dir().join(format!("tb-core-{tag}-{}-{n}", std::process::id()));
        let _ = fs::remove_dir_all(&p);
        fs::create_dir_all(&p).unwrap();
        TempDir(p)
    }
    fn s(&self) -> String {
        self.0.to_string_lossy().into_owned()
    }
}

impl Drop for TempDir {
    fn drop(&mut self) {
        let _ = fs::remove_dir_all(&self.0);
    }
}

fn write(p: &Path, text: &str) {
    fs::create_dir_all(p.parent().unwrap()).unwrap();
    fs::write(p, text).unwrap();
}

fn titles(hits: &[Hit]) -> Vec<&str> {
    hits.iter().map(|h| h.title.as_str()).collect()
}

fn count_kind(kb: &KbIndex, kind: &str) -> usize {
    kb.all.iter().filter(|h| h.kind == kind).count()
}

const CODES: &str = r#"{"codes":[
  {"brand_id":"DOOSAN","code":"F68","title":"Discharge temperature high",
   "description":"Airend discharge over limit","likely_causes":["Low oil"],
   "checks":["Check cooler"],"severity":"high","controller_id":"ifix","tags":["heat"]},
  {"brand_id":"DOOSAN","code":"F12","title":"Sensor fault, see also F68 for heat",
   "description":"Mentions F68 in the text","likely_causes":[],"checks":[],
   "severity":"low","controller_id":"ifix","tags":[]},
  {"brand_id":"IR","code":"F680","title":"Unrelated longer code",
   "description":"","likely_causes":[],"checks":[],"severity":"low",
   "controller_id":"xe","tags":[]}]}"#;

#[test]
fn kb_load_resilience_and_ranking() {
    let root = TempDir::new("kbtest");
    let data = root.0.join("data");
    write(&data.join("kb.json"), CODES);
    // Deliberately corrupt: this file used to take the whole index down with it.
    write(
        &data.join("rental-equipment-info.json"),
        "{ this is not json ",
    );
    write(
        &data.join("usb-manuals.json"),
        r#"{"documents":[{"document_name":"HP450 service manual","manufacturer":"Doosan",
          "local_path":"E:\\manuals\\hp450.pdf","filename":"hp450.pdf","category":"service"}]}"#,
    );

    let mut kb = KbIndex::new();
    kb.load(&root.s());
    assert_eq!(
        kb.all.len(),
        4,
        "codes + manual still loaded despite one bad file"
    );
    assert!(
        kb.errors.len() == 1 && kb.errors[0].starts_with("rental-equipment-info.json"),
        "{:?}",
        kb.errors
    );

    let (hits, total) = kb.search("F68", "ALL", MAX_RESULTS);
    assert_eq!(total, 3, "F68 matches three entries");
    assert!(
        hits[0].title.contains("F68  —  Discharge"),
        "first was {}",
        hits[0].title
    );
    assert!(
        hits.len() > 2 && hits[1].title.contains("F680"),
        "order: {:?}",
        titles(&hits)
    );

    let (hits, total) = kb.search("doosan discharge", "ALL", MAX_RESULTS);
    assert!(total == 1 && hits[0].kind == "CODE", "total={total}");

    let (_, total) = kb.search("hp450", "MANUAL", MAX_RESULTS);
    assert_eq!(total, 1, "kind filter works");

    let (hits, total) = kb.search("F68", "CODE", 1);
    assert_eq!(total, 3, "total counts all matches even when capped");
    assert_eq!(hits.len(), 1, "cap respected");

    let (_, total) = kb.search("", "ALL", MAX_RESULTS);
    assert_eq!(total, 4, "empty query lists everything");

    let (_, total) = kb.search("nothingmatchesthis", "ALL", MAX_RESULTS);
    assert_eq!(total, 0);

    // Worst case: the primary codes file is the corrupt one.
    write(&data.join("kb.json"), "{ broken");
    let mut partial = KbIndex::new();
    partial.load(&root.s());
    assert_eq!(partial.all.len(), 1, "manuals survive a corrupt kb.json");
    assert_eq!(partial.errors.len(), 2, "both bad files reported");
    assert!(partial.status.contains("kb.json"), "{}", partial.status);

    let mut empty = KbIndex::new();
    empty.load(&root.0.join("does-not-exist").to_string_lossy());
    assert_eq!(empty.all.len(), 0);
    assert!(empty.status.contains("nothing loaded"), "{}", empty.status);
}

#[test]
fn code_hit_text_matches_csharp_layout() {
    let root = TempDir::new("kblayout");
    write(&root.0.join("data").join("kb.json"), CODES);
    let mut kb = KbIndex::new();
    kb.load(&root.s());
    let h = &kb.all[0];
    assert_eq!(h.title, "DOOSAN  F68  —  Discharge temperature high");
    assert_eq!(h.subtitle, "high  ·  ifix  ·  ");
    assert_eq!(
        h.body,
        "Airend discharge over limit\r\n\r\nLikely causes:\r\n  • Low oil\r\n\r\nChecks:\r\n  • Check cooler\r\n"
    );
    assert_eq!(h.key, "f68");
    assert_eq!(
        kb.status,
        "Codes 3  ·  Passwords 0  ·  Manuals 0  ·  Filters 0  ·  Equipment 0"
    );
}

#[test]
fn spn_hits() {
    let root = TempDir::new("kbspn");
    let notes = root.0.join("data").join("notes");
    write(
        &root.0.join("data").join("kb.json"),
        r#"{"codes":[
          {"brand_id":"CUMMINS","code":"SPN 5246","title":"SCR inducement",
           "description":"","likely_causes":[],"checks":[],"severity":"high",
           "controller_id":"cm2350","tags":[]}]}"#,
    );
    // Title and key never name the SPN; only the body does, so it scores 0.
    write(
        &notes.join("def.txt"),
        "Aftertreatment checklist\nIf SPN 5246 is active, check DEF quality first.",
    );
    write(
        &notes.join("other.txt"),
        "Belt tension\nNothing about that fault.",
    );
    // A second note with the same first line is its own entry, not a duplicate.
    write(
        &notes.join("def2.txt"),
        "Aftertreatment checklist\nSPN 5246 came back after the doser swap.",
    );

    let mut kb = KbIndex::new();
    kb.load(&root.s());
    let hits = kb.spn_hits(5246, 0, 3);
    assert_eq!(hits.len(), 3, "code and both body-only notes");
    assert!(
        hits[0].kind == "CODE"
            && hits[1].title == "Aftertreatment checklist"
            && hits[2].title == "Aftertreatment checklist"
            && hits[1].path != hits[2].path,
        "order: {:?}",
        titles(&hits)
    );
    assert_eq!(kb.spn_hits(5246, 0, 1).len(), 1);
    assert_eq!(kb.spn_hits(5246, 0, 0).len(), 0);
    let text = kb.spn_text(5246, 0).expect("SPN text");
    assert!(text.contains("check DEF quality first"), "{text}");
    assert_eq!(kb.spn_hits(9999, 0, 3).len(), 0);
    assert_eq!(kb.spn_text(9999, 0), None);
}

#[test]
fn names_json_overrides_load_into_the_index() {
    let root = TempDir::new("kbnames");
    write(
        &root.0.join("data").join("j1939-names.json"),
        r#"{"spn":{"4001":"Shop-added SPN","0x6E1":"DEF level (shop)","bad":"x","110":"  "},"sa":{"77":"Shop ECU"}}"#,
    );
    let mut kb = KbIndex::new();
    kb.load(&root.s());
    assert_eq!(kb.names.spn(4001), "Shop-added SPN");
    assert_eq!(kb.names.spn(1761), "DEF level (shop)");
    assert_eq!(kb.names.spn(110), "Coolant temp", "blank override ignored");
    assert_eq!(kb.names.sa_label(77), "SA 77 Shop ECU");
    assert_eq!(
        kb.status,
        format!("Codes 0  ·  Passwords 0  ·  Manuals 0  ·  Filters 0  ·  Equipment 0  ·  J1939 names 3  ·  nothing loaded from {}", root.s())
    );
}

#[test]
fn user_codes_load_after_shipped_codes() {
    let root = TempDir::new("kbuser");
    write(&root.0.join("data").join("kb.json"), r#"{"codes":[]}"#);
    write(
        &root.0.join("data").join("user-codes.json"),
        r#"{"codes":[{"brand_id":"X","code":"F99","title":"Shop code"},{"brand_id":"x","code":"f99","title":"dupe"}]}"#,
    );
    let mut kb = KbIndex::new();
    kb.load(&root.s());
    assert_eq!(
        kb.all.len(),
        1,
        "same brand+code collapses, case-insensitive"
    );
    let (hits, total) = kb.search("F99", "CODE", 10);
    assert!(total >= 1 && hits[0].title.contains("F99"));
}

#[test]
fn notes_and_files() {
    let root = TempDir::new("kbfiles");
    let data = root.0.join("data");
    write(
        &data.join("notes").join("a.md"),
        "\n\n  First real line  \nmore",
    );
    write(&data.join("notes").join("skip.pdf"), "x");
    write(&data.join("files").join("wiring.pdf"), "x");
    write(&data.join("files").join("old.bak"), "x");
    write(&data.join("files").join("desktop.ini"), "x");
    write(&data.join("files").join("x.tmp-123"), "x");
    let mut kb = KbIndex::new();
    kb.load(&root.s());
    assert_eq!(count_kind(&kb, "NOTE"), 1);
    assert_eq!(
        kb.all.iter().find(|h| h.kind == "NOTE").unwrap().title,
        "First real line"
    );
    assert_eq!(count_kind(&kb, "FILE"), 1);
    assert_eq!(
        kb.all.iter().find(|h| h.kind == "FILE").unwrap().title,
        "wiring.pdf"
    );
    assert!(
        kb.status.contains("  ·  Notes 1  ·  Files 1"),
        "{}",
        kb.status
    );
}

fn repo_kb() -> String {
    Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("..")
        .join("..")
        .join("kb")
        .to_string_lossy()
        .into_owned()
}

#[test]
fn shipped_field_database() {
    let mut kb = KbIndex::new();
    kb.load(&repo_kb());
    assert_eq!(count_kind(&kb, "CODE"), 155);
    assert_eq!(count_kind(&kb, "PASSWORD"), 1424);
    assert_eq!(count_kind(&kb, "MANUAL"), 2147);
    assert_eq!(count_kind(&kb, "FILTER"), 63);
    assert_eq!(count_kind(&kb, "EQUIP"), 37);
    assert_eq!(kb.errors.len(), 0, "{:?}", kb.errors);

    let (hits, total) = kb.search("1AVPT", "CODE", 5);
    assert!(
        total >= 1 && hits[0].title.to_lowercase().contains("sensor failure"),
        "{:?}",
        titles(&hits)
    );
    let (hits, total) = kb.search("F68", "FILTER", 5);
    assert!(
        total >= 1 && hits[0].title.to_lowercase().contains("f68"),
        "{:?}",
        titles(&hits)
    );
    let (hits, total) = kb.search("XHP1170", "EQUIP", 5);
    assert!(
        total >= 1 && hits[0].title.to_lowercase().contains("xhp1170"),
        "{:?}",
        titles(&hits)
    );
}

#[test]
fn kb_document_paths_stay_under_root() {
    let kb = TempDir::new("kbdoc");
    let outside_dir = TempDir::new("kbout");
    let inside = kb.0.join("manuals").join("unit.pdf");
    write(&inside, "pdf");
    assert_eq!(
        resolve_document_path(&kb.s(), "manuals/unit.pdf", "unit.pdf"),
        Some(inside.clone())
    );
    let outside = outside_dir.0.join("secret.pdf");
    write(&outside, "x");
    let outside_s = outside.to_string_lossy().into_owned();
    assert_eq!(
        resolve_document_path(&kb.s(), &outside_s, "secret.pdf"),
        None,
        "absolute path outside KB refused"
    );
    assert!(
        is_absolute_path(r"C:\Users\other\secret.pdf"),
        "drive path is absolute"
    );
    assert_eq!(
        resolve_document_path(&kb.s(), r"C:\Users\other\secret.pdf", "nope.pdf"),
        None
    );
    write(&kb.0.join("secret.pdf"), "x");
    assert_eq!(
        resolve_document_path(&kb.s(), &outside_s, "secret.pdf"),
        Some(kb.0.join("secret.pdf"))
    );
    let escape = kb.0.join("..").join("escape.pdf");
    assert!(
        !is_path_under_root(&kb.s(), &escape.to_string_lossy()),
        "escape rejected"
    );
    assert!(
        !is_path_under_root(&kb.s(), "../escape.pdf"),
        "relative escape rejected"
    );
    assert!(
        is_path_under_root(&kb.s(), "manuals/../unit.pdf"),
        "fold stays inside"
    );
}
