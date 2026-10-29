//! Add code / Add note / Add file: tests/SelfTest.ShopAdd.cs and the UserCodes KeyOf / PathFor
//! literals from tests/SelfTest.WorkOrders.cs, plus the shard files and the index reading them.
#![cfg(not(target_arch = "wasm32"))]

use std::fs;
use std::path::{Path, PathBuf};
use tb_core::kb::shop::{self, SyncSettings};
use tb_core::kb::user_codes::{self, UserCode};
use tb_core::kb::KbIndex;

fn uc(brand: &str, code: &str, title: &str) -> UserCode {
    UserCode {
        brand: brand.into(),
        code: code.into(),
        title: title.into(),
        ..UserCode::default()
    }
}

fn temp(tag: &str) -> PathBuf {
    let p = std::env::temp_dir().join(format!("tb-shopadd-{tag}-{}", std::process::id()));
    let _ = fs::remove_dir_all(&p);
    fs::create_dir_all(&p).unwrap();
    p
}

#[test]
fn key_path_seed_and_queries() {
    assert_eq!(
        user_codes::key_of(&uc(" Doosan ", " F68-A ", "")),
        "doosan\u{1}f68-a"
    );
    assert_eq!(user_codes::key_of(&uc("", "X1", "")), "\u{1}x1");
    assert_eq!(
        user_codes::key_of(&uc("CUMMINS", "f1", "")),
        user_codes::key_of(&uc("cummins", "F1", ""))
    );
    assert_ne!(
        user_codes::key_of(&uc("a", "1", "")),
        user_codes::key_of(&uc("b", "1", ""))
    );
    assert_eq!(
        user_codes::path_for(Path::new("kb")),
        Path::new("kb").join("data").join("user-codes.json")
    );

    let saved = vec![
        UserCode {
            description: "saved".into(),
            ..uc("doosan", "F68", "Low oil pressure")
        },
        uc("cummins", "1761", "DEF level low"),
    ];
    let hit = user_codes::code_seed(&saved, "CODE", "DOOSAN  F68  —  low oil pressure");
    assert_eq!(
        (hit.description.as_str(), hit.code.as_str()),
        ("saved", "F68")
    );
    let miss = user_codes::code_seed(&saved, "CODE", "Doosan  F99  —  Unknown");
    assert_eq!(
        (miss.title.as_str(), miss.code.as_str()),
        ("Doosan  F99  —  Unknown", "")
    );
    assert_eq!(
        user_codes::code_seed(&saved, "MANUAL", "Doosan  F68  —  Low oil pressure").title,
        ""
    );
    assert_eq!(
        user_codes::code_seed(&[], "CODE", "X  1  —  Y").title,
        "X  1  —  Y"
    );

    assert_eq!(
        user_codes::code_query(&uc("", "F68", "Low oil")),
        "F68 Low oil"
    );
    assert_eq!(user_codes::code_query(&uc("", "", "Low oil")), "Low oil");
    assert_eq!(
        user_codes::note_query("Belt chart", "line one"),
        "Belt chart"
    );
    assert_eq!(
        user_codes::note_query("", "\r\n  \r\n  Check 4-pin  \r\nmore"),
        "Check 4-pin"
    );
    assert_eq!(user_codes::note_query("", ""), "");

    assert_eq!(
        user_codes::split_lines("• Inlet sensor\r\n\r\n - wiring \n* relay\n   "),
        vec!["Inlet sensor", "wiring", "relay"]
    );
    assert_eq!(shop::sanitize_tech_id(" user B. "), "user-B");
    assert_eq!(shop::sanitize_tech_id("__x__"), "x");
    assert_eq!(shop::sanitize_tech_id("***"), "tech");
    assert_eq!(shop::sanitize_tech_id(&"y".repeat(45)), "y".repeat(40));
}

#[test]
fn user_codes_file_round_trip() {
    let dir = temp("codes");
    let path = dir.join("user-codes.json");
    let mut list = Vec::new();
    user_codes::upsert(&mut list, uc("", "", "")); // unusable: ignored
    user_codes::upsert(
        &mut list,
        UserCode {
            causes: vec!["Sensor \"A\"".into()],
            reset: "Key off\ton".into(),
            ..uc("doosan", "F68", "Low oil")
        },
    );
    user_codes::upsert(&mut list, uc("DOOSAN", "f68", "Low oil pressure"));
    assert_eq!(list.len(), 1);
    user_codes::save_to(&path, &list).unwrap();
    assert_eq!(
        fs::read_to_string(&path).unwrap(),
        "{\r\n  \"codes\": [\r\n    {\r\n      \"brand_id\": \"DOOSAN\",\r\n      \"code\": \"f68\",\r\n      \"title\": \"Low oil pressure\",\r\n      \"description\": \"\",\r\n      \"severity\": \"\",\r\n      \"controller_id\": \"\",\r\n      \"likely_causes\": [],\r\n      \"checks\": [],\r\n      \"reset_notes\": \"\",\r\n      \"safety\": \"\",\r\n      \"source\": \"tech-bench\"\r\n    }\r\n  ]\r\n}\r\n"
    );
    let two = vec![
        UserCode {
            causes: vec!["Sensor \"A\"".into(), "Wiring".into()],
            checks: vec!["C:\\kb".into()],
            reset: "Key off\ton".into(),
            ..uc("a", "1", "T")
        },
        uc("b", "2", ""),
    ];
    user_codes::save_to(&path, &two).unwrap();
    assert_eq!(user_codes::load_from(&path).unwrap(), two);
    assert!(fs::read_to_string(&path)
        .unwrap()
        .contains("      \"likely_causes\": [\r\n        \"Sensor \\\"A\\\"\",\r\n        \"Wiring\"\r\n      ],\r\n"));

    fs::write(&path, r#"{"other": []}"#).unwrap();
    assert!(user_codes::load_from(&path).is_err());
    fs::write(&path, r#"{"codes": {}}"#).unwrap();
    assert!(user_codes::load_from(&path).is_err());
    assert!(user_codes::load_from(&dir.join("missing.json"))
        .unwrap()
        .is_empty());
    let _ = fs::remove_dir_all(&dir);
}

#[test]
fn shards_are_written_and_indexed() {
    let kb = temp("kb");
    fs::create_dir_all(kb.join("data")).unwrap();
    fs::write(kb.join("data").join("kb.json"), r#"{"codes": []}"#).unwrap();

    let p = shop::save_user_code(&kb, "user B.", uc("doosan", "F68", "Low oil")).unwrap();
    assert_eq!(
        p,
        kb.join("data")
            .join("shop")
            .join("user-B")
            .join("user-codes.json")
    );
    shop::save_user_code(&kb, "user-B", uc("doosan", "F69", "High temp")).unwrap();

    let note = shop::save_note(
        &kb,
        "user-B",
        " Belt chart ",
        "Use the 4-pin.",
        "20261026_101500",
    )
    .unwrap();
    assert_eq!(note.file_name().unwrap(), "20261026_101500-Belt-chart.txt");
    assert_eq!(
        fs::read_to_string(&note).unwrap(),
        "Belt chart\r\n\r\nUse the 4-pin."
    );
    let untitled = shop::save_note(
        &kb,
        "user-B",
        "",
        "\n  Fan relay 3 \nmore",
        "20261026_101501",
    )
    .unwrap();
    assert_eq!(
        untitled.file_name().unwrap(),
        "20261026_101501-Fan-relay-3.txt"
    );
    let blank = shop::save_note(&kb, "user-B", "", "***", "20261026_101502").unwrap();
    assert_eq!(blank.file_name().unwrap(), "20261026_101502-note.txt");

    let src = kb.join("chart.pdf");
    fs::write(&src, b"one").unwrap();
    let f1 = shop::add_file(&kb, "user-B", &src, "20261026_101503").unwrap();
    assert_eq!(
        f1,
        kb.join("data")
            .join("shop")
            .join("user-B")
            .join("files")
            .join("chart.pdf")
    );
    assert_eq!(
        shop::add_file(&kb, "user-B", &src, "20261026_101504").unwrap(),
        f1
    );
    fs::write(&src, b"two").unwrap();
    let f2 = shop::add_file(&kb, "user-B", &src, "20261026_101505").unwrap();
    assert_eq!(f2.file_name().unwrap(), "chart-20261026_101505.pdf");
    assert!(shop::add_file(&kb, "user-B", &kb.join("gone.pdf"), "x").is_err());

    // Another tech's code with the same key, and a resolved copy that wins.
    shop::save_user_code(&kb, "pat", uc("doosan", "F68", "Low oil (pat)")).unwrap();
    assert_eq!(shop::tech_ids(&kb), vec!["pat", "user-B"]);
    let all = shop::load_all_codes(&kb).unwrap();
    let authors: Vec<_> = all
        .iter()
        .map(|c| format!("{}:{}", c.author, c.code))
        .collect();
    assert_eq!(authors, vec!["pat:F68", "user-B:F68", "user-B:F69"]);

    let mut index = KbIndex::new();
    index.load(&kb.to_string_lossy());
    assert_eq!(
        index.status,
        "Codes 2  ·  Passwords 0  ·  Manuals 0  ·  Filters 0  ·  Equipment 0  ·  Notes 3  ·  Files 2"
    );
    let (hits, _) = index.search("F68", "CODE", 10);
    let titles: Vec<_> = hits
        .iter()
        .map(|h| (h.title.clone(), h.subtitle.clone()))
        .collect();
    assert_eq!(
        titles,
        vec![(
            "doosan  F68  —  Low oil (pat)".to_string(),
            "  ·    ·    ·  pat".to_string()
        )]
    );
    let (notes, _) = index.search("belt", "NOTE", 10);
    assert_eq!(notes[0].subtitle, "Tech note  ·  user-B");

    // A resolution picks a copy; keep-both shows both techs' copies again.
    user_codes::save_to(
        &kb.join("data")
            .join("shop")
            .join("_resolved")
            .join("user-codes.json"),
        &[uc("doosan", "F68", "Low oil (resolved)")],
    )
    .unwrap();
    index.load(&kb.to_string_lossy());
    let (hits, _) = index.search("F68", "CODE", 10);
    assert_eq!(hits.len(), 1);
    assert_eq!(hits[0].title, "doosan  F68  —  Low oil (resolved)");
    fs::write(
        kb.join("data")
            .join("shop")
            .join("_resolved")
            .join("keep-both.txt"),
        "# keys\nDOOSAN\u{1}F68\n",
    )
    .unwrap();
    index.load(&kb.to_string_lossy());
    assert_eq!(index.search("F68", "CODE", 10).0.len(), 3);
    let _ = fs::remove_dir_all(&kb);
}

#[test]
fn sync_settings_tech_id() {
    let dir = temp("sync");
    assert_eq!(SyncSettings::load(&dir, "Pat Lee").tech_id, "Pat-Lee");
    fs::write(
        dir.join("sync.json"),
        r#"{"TechId":" user.B ","SyncFolder":"S:\\usb"}"#,
    )
    .unwrap();
    assert_eq!(
        SyncSettings::load(&dir, "x"),
        SyncSettings {
            tech_id: "user-B".into(),
            sync_folder: "S:\\usb".into()
        }
    );
    let _ = fs::remove_dir_all(&dir);
}
