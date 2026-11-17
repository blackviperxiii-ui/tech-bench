//! Ported from tests/SelfTest.Crash.cs (CrashFormTests): the crash log record and the text Copy
//! details puts on the clipboard.

#[cfg(target_arch = "wasm32")]
use wasm_bindgen_test::wasm_bindgen_test as test;

use tb_core::crash::{clipboard_text, record, LOG_FILE_NAME};

#[test]
fn record_and_clipboard_text() {
    assert_eq!(LOG_FILE_NAME, "techbench-crash.log");
    assert_eq!(
        record("2026-11-17 09:30:00", "SelfTest", "self-test crash marker"),
        "=== 2026-11-17 09:30:00  SelfTest ===\nself-test crash marker\n\n"
    );
    assert_eq!(
        record("2026-11-17 09:30:00", "UI", ""),
        "=== 2026-11-17 09:30:00  UI ===\n(no exception object)\n\n"
    );
    assert_eq!(
        clipboard_text("SelfTest", "self-test crash marker", "/home/user/techbench-crash.log"),
        "Tech Bench crash\nWhere: SelfTest\nLog: /home/user/techbench-crash.log\n\nself-test crash marker\n"
    );
    // No log written (web build): no Log line.
    assert_eq!(
        clipboard_text("UI", "boom", ""),
        "Tech Bench crash\nWhere: UI\n\nboom\n"
    );
}

#[cfg(not(target_arch = "wasm32"))]
#[test]
fn append_log_appends_records() {
    use std::fs;
    use tb_core::crash::append_log;

    let dir = std::env::temp_dir().join(format!("tb-crash-log-{}", std::process::id()));
    let _ = fs::remove_dir_all(&dir);
    let path = append_log(&dir, "2026-11-17 09:30:00", "SelfTest", "first").unwrap();
    assert_eq!(path, dir.join("techbench-crash.log"));
    append_log(&dir, "2026-11-17 09:31:00", "panic", "second").unwrap();
    assert_eq!(
        fs::read_to_string(&path).unwrap(),
        "=== 2026-11-17 09:30:00  SelfTest ===\nfirst\n\n=== 2026-11-17 09:31:00  panic ===\nsecond\n\n"
    );
    let _ = fs::remove_dir_all(&dir);
}
