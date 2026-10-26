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
