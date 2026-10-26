//! Ported from tests/SelfTest.JobStrip.cs and the job-strip lines of SelfTest.Shortcuts.cs.

// Under wasm-pack the same cases run through wasm-bindgen-test.
#[cfg(target_arch = "wasm32")]
use wasm_bindgen_test::wasm_bindgen_test as test;

use tb_core::workorders::job_strip::*;
use tb_core::workorders::WorkOrder;

fn wo(number: &str) -> WorkOrder {
    WorkOrder {
        number: number.into(),
        ..WorkOrder::default()
    }
}

#[test]
fn title_tag_and_chip() {
    assert_eq!(window_title("1.2.34", ""), "Tech Bench 1.2.34");
    assert_eq!(
        window_title("1.2.34", "WO 555-01"),
        "Tech Bench 1.2.34  ·  WO 555-01"
    );

    let open = WorkOrder {
        segment: "01".into(),
        model: "C185".into(),
        serial: "S123".into(),
        customer: "Acme".into(),
        ..wo("555")
    };
    assert_eq!(
        job_tag(Some(&open), "typed", "typed"),
        "WO 555-01  C185 S123"
    );
    assert_eq!(job_tag(Some(&wo("  ")), "XAS 185", "77"), "XAS 185 77");
    assert_eq!(job_tag(None, "XAS 185", ""), "XAS 185");
    assert_eq!(job_tag(None, "", ""), "");

    assert_eq!(sync_chip(0, "user"), "Sync · user · click");
    assert_eq!(sync_chip(3, "user"), "3 sync conflict(s) — click");
    assert_eq!(SYNC_IDLE, "Sync: idle");

    assert_eq!(search_query(" XAS 185 ", "77 "), "XAS 185  77");
    assert_eq!(search_query("", "77"), "77");
    assert_eq!(startup_customer("Acme", "Old Co"), "Acme");
    assert_eq!(startup_customer("", "Old Co"), "Old Co");
    assert_eq!(startup_customer("", ""), "");
}

#[test]
fn pick_list() {
    let all = vec![
        WorkOrder {
            segment: "01".into(),
            model: "C185".into(),
            serial: "S123".into(),
            customer: "Acme".into(),
            ..wo("555")
        },
        WorkOrder {
            clock_state: "logged-on".into(),
            ..wo("600")
        },
    ];
    let (rows, sel) = pick_items(&all, "555-01");
    assert_eq!(
        rows.join("|"),
        "(no work order)|555-01  Acme  C185 S123|600  [logged on]"
    );
    assert_eq!(sel, 1);
    assert_eq!(pick_items(&all, "600").1, 2);
    assert_eq!(pick_items(&[wo("ab")], "AB").1, 1);
    assert_eq!(pick_items(&all, "999").1, 0);
    assert_eq!(pick_items(&all, "").1, 0);
    assert_eq!(pick_items(&[], "555-01").0.len(), 1);

    assert_eq!(picked(&all, 1).map(|w| w.number.as_str()), Some("555"));
    assert_eq!(picked(&all, 2).map(|w| w.number.as_str()), Some("600"));
    assert!(picked(&all, 0).is_none());
    assert!(picked(&all, 3).is_none());
    assert!(picked(&[], 1).is_none());
}
