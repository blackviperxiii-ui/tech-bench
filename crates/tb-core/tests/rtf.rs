//! The cheat sheet reader against the shipped assets/cheatsheet.rtf and small literal cases.

// Under wasm-pack the same cases run through wasm-bindgen-test.
#[cfg(target_arch = "wasm32")]
use wasm_bindgen_test::wasm_bindgen_test as test;

use tb_core::rtf::{paragraphs, Para};

const CHEATSHEET: &str = include_str!("../../../assets/cheatsheet.rtf");

fn p(text: &str, bold: bool, big: bool) -> Para {
    Para {
        text: text.into(),
        bold,
        big,
    }
}

#[test]
fn shipped_cheatsheet() {
    let paras = paragraphs(CHEATSHEET);
    assert_eq!(paras.len(), 45);
    assert_eq!(paras[0], p("Tech Bench quick reference", true, true));
    assert_eq!(paras[1], p("", false, false));
    assert_eq!(paras[2], p("Keyboard shortcuts", true, false));
    assert_eq!(
        paras[3],
        p(
            "F1\tOpen this cheat sheet (press F1 again to focus it if it is already open)",
            false,
            false
        )
    );
    let headings: Vec<&str> = paras
        .iter()
        .filter(|x| x.bold)
        .map(|x| x.text.as_str())
        .collect();
    assert_eq!(
        headings,
        [
            "Tech Bench quick reference",
            "Keyboard shortcuts",
            "Main navigation",
            "Job strip (top)",
            "Shop menu",
            "Help menu",
            "Search filters (kind box)",
            "INLINE 7 (high level)",
            "Knowledge base",
        ]
    );
    let sessions = paras
        .iter()
        .find(|x| x.text.starts_with("Sessions\t"))
        .unwrap();
    assert!(
        sessions
            .text
            .contains("Saved under Desktop\\TechBench-sessions (or Documents"),
        "{}",
        sessions.text
    );
    assert_eq!(
        paras.last().unwrap().text,
        "Shipped data is embedded in TechBench.exe and copied beside the exe on install. Override with TECHBENCH_KB, kb-path.txt, or sync-path.txt next to the exe."
    );
    assert!(
        paras.iter().all(|x| !x.text.contains("Segoe UI")),
        "font table skipped"
    );
}

#[test]
fn escapes_and_destinations() {
    let rtf = r"{\rtf1{\fonttbl{\f0 Arial;}}{\*\generator X;}caf\'e9 \{a\}\\b\~c\par\b bold \b0 plain\par{\b in group}\par tail}";
    assert_eq!(
        paragraphs(rtf),
        [
            p("café {a}\\b\u{a0}c", false, false),
            p("bold plain", false, false),
            p("in group", true, false),
            p("tail", false, false),
        ]
    );
    assert_eq!(
        paragraphs(r"{\rtf1 a\'96b\'93c\'94\par}"),
        [p("a–b“c”", false, false)]
    );
}
