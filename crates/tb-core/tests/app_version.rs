//! The new app's version lives in three files that must agree: `src-tauri/tauri.conf.json`
//! (what the installers and the About/update screens show), `src-tauri/Cargo.toml` (the binary)
//! and the root `package.json` (the UI build). The release notes in docs/new-app-release.md
//! bump them together.
#![cfg(not(target_arch = "wasm32"))]

use std::path::Path;
use tb_core::json;

fn repo(rel: &str) -> String {
    let p = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../..")
        .join(rel);
    std::fs::read_to_string(&p).unwrap_or_else(|e| panic!("{}: {e}", p.display()))
}

fn json_version(rel: &str) -> String {
    json::parse(&repo(rel))
        .unwrap()
        .get("version")
        .map(json::Value::text)
        .unwrap_or_default()
}

#[test]
fn tauri_conf_cargo_and_package_json_agree() {
    let conf = json_version("src-tauri/tauri.conf.json");
    let cargo = repo("src-tauri/Cargo.toml")
        .lines()
        .find_map(|l| l.strip_prefix("version = \""))
        .map(|v| v.trim_end_matches('"').to_string())
        .unwrap_or_default();
    let npm = json_version("package.json");
    assert!(!conf.is_empty(), "tauri.conf.json has a version");
    assert_eq!(
        cargo, conf,
        "src-tauri/Cargo.toml version matches tauri.conf.json"
    );
    assert_eq!(npm, conf, "package.json version matches tauri.conf.json");
}
