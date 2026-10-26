//! ShopSettings sanitize and file shape (from tests/SelfTest.ShopSettings.cs and the C# writer).
#![cfg(not(target_arch = "wasm32"))]

use std::fs;
use tb_core::settings::shop::{sanitize, ShopSettings};

#[test]
fn sanitize_tech_id() {
    // SelfTest.ShopSettings.cs literals.
    assert_eq!(sanitize(" user B. "), "user-B");
    assert_eq!(sanitize("***"), "tech");
    assert_eq!(sanitize("  Jo Smith.2 "), "Jo-Smith-2");
    assert_eq!(sanitize("a/b\\c"), "abc");
    assert_eq!(sanitize("..."), "tech");
    assert_eq!(sanitize(""), "tech");
    assert_eq!(sanitize(&"x".repeat(50)), "x".repeat(40));
}

#[test]
fn load_shop_settings_file() {
    let dir = std::env::temp_dir().join(format!("tb-shop-{}", std::process::id()));
    let _ = fs::remove_dir_all(&dir);
    fs::create_dir_all(&dir).unwrap();
    assert_eq!(ShopSettings::load(&dir, "Pat Lee").tech_id, "Pat-Lee");
    fs::write(
        dir.join("shop-settings.json"),
        r#"{"TechId":"user","ShareFolder":"S:\\shop","AssignedFile":""}"#,
    )
    .unwrap();
    let s = ShopSettings::load(&dir, "ignored");
    assert_eq!(
        (
            s.tech_id.as_str(),
            s.share_folder.as_str(),
            s.assigned_file.as_str()
        ),
        ("user", "S:\\shop", "")
    );
    fs::write(dir.join("shop-settings.json"), r#"{"TechId":"cut"#).unwrap();
    assert_eq!(ShopSettings::load(&dir, "fallback").tech_id, "fallback");
    let _ = fs::remove_dir_all(&dir);
}

#[test]
fn save_round_trip_and_collect() {
    let dir = std::env::temp_dir().join(format!("tb-shop-save-{}", std::process::id()));
    let _ = fs::remove_dir_all(&dir);
    let s = ShopSettings::collect(" user B. ", "  S:\\shop  ", " C:\\wo\\id-work-orders.csv ");
    assert_eq!(
        s,
        ShopSettings {
            tech_id: "user-B".into(),
            share_folder: "S:\\shop".into(),
            assigned_file: "C:\\wo\\id-work-orders.csv".into(),
        }
    );
    let path = dir.join("sub").join("shop-settings.json");
    s.save_to(&path).unwrap();
    assert_eq!(
        fs::read_to_string(&path).unwrap(),
        r#"{"TechId":"user-B","ShareFolder":"S:\\shop","AssignedFile":"C:\\wo\\id-work-orders.csv"}"#
    );
    assert_eq!(ShopSettings::load_from(&path), Some(s.clone()));
    // A second save keeps the first as .bak.
    ShopSettings::collect("next", "", "")
        .save_to(&path)
        .unwrap();
    assert!(dir.join("sub").join("shop-settings.json.bak").exists());
    assert_eq!(ShopSettings::load(&dir.join("sub"), "x").tech_id, "next");
    let _ = fs::remove_dir_all(&dir);
}
