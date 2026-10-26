//! Ported from tests/SelfTest.Open.cs (LaunchBlockTests): the shell-open blocklist and message.

// Under wasm-pack the same cases run through wasm-bindgen-test.
#[cfg(target_arch = "wasm32")]
use wasm_bindgen_test::wasm_bindgen_test as test;

use tb_core::launch::{blocked_launch_message, is_blocked_launch_path, missing_file_message};

const BLOCKED: &[&str] = &[
    ".exe",
    ".com",
    ".bat",
    ".cmd",
    ".scr",
    ".pif",
    ".msi",
    ".msp",
    ".ps1",
    ".psm1",
    ".vbs",
    ".vbe",
    ".js",
    ".jse",
    ".wsf",
    ".wsh",
    ".hta",
    ".cpl",
    ".reg",
    ".lnk",
    ".jar",
    ".appref-ms",
    ".application",
    ".gadget",
    ".msc",
    ".psc1",
    ".scf",
    ".url",
    ".vb",
    ".ws",
    ".inf",
    ".dll",
    ".sys",
    ".chm",
    ".docm",
    ".xlsm",
    ".pptm",
    ".dotm",
    ".xltm",
    ".ppsm",
];

#[test]
fn every_program_and_script_extension_is_blocked() {
    assert_eq!(BLOCKED.len(), 40);
    for ext in BLOCKED {
        assert!(is_blocked_launch_path(&format!("manual{ext}")), "{ext}");
        assert!(
            is_blocked_launch_path(&format!("MANUAL{}", ext.to_uppercase())),
            "upper {ext}"
        );
    }
}

#[test]
fn tricky_names() {
    let blocked = [
        "x.pdf.exe",
        "X.PDF.EXE",
        "x.exe.",
        "x.exe ",
        "x.exe. ",
        "x.pdf:evil.exe",
        "x.pdf:evil.EXE",
        r"C:\kb\x.pdf:evil.exe",
        "C:setup.exe",
        "x.exe:Zone.Identifier",
        r"C:\kb\files\setup.EXE",
        "",
        "   ",
        " . ",
    ];
    for p in blocked {
        assert!(is_blocked_launch_path(p), "should block {p:?}");
    }
    let allowed = [
        "x.exe.pdf",
        "C:manual.pdf",
        r"C:\kb\x.pdf",
        "kb.json",
        "x.ps1xml",
        "x.pdf",
        "x.jpg",
        "x.png",
        "x.txt",
        "x.docx",
        "x.xlsx",
        "photo.jpg.",
        "photo.jpg ",
        "/home/user/kb/files/manual.pdf",
        "README",
    ];
    for p in allowed {
        assert!(!is_blocked_launch_path(p), "should allow {p:?}");
    }
}

#[test]
fn messages() {
    assert_eq!(
        blocked_launch_message(r"C:\kb\files\setup.exe"),
        "Tech Bench won't open program or script files from the knowledge base: setup.exe. Open it from Explorer if you trust it."
    );
    assert_eq!(
        blocked_launch_message("/home/user/kb/run.sh.bat. "),
        "Tech Bench won't open program or script files from the knowledge base: run.sh.bat. Open it from Explorer if you trust it."
    );
    assert_eq!(
        blocked_launch_message(""),
        "Tech Bench won't open program or script files from the knowledge base: (no file). Open it from Explorer if you trust it."
    );
    assert_eq!(
        missing_file_message(" S:\\shop\\a.pdf "),
        "Cannot find this file:\nS:\\shop\\a.pdf\n\nCheck that the USB drive or network share is connected."
    );
}
