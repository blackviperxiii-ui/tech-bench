//! Ported from tests/SelfTest.WorkOrders.cs (WorkOrder helpers and media path confinement).
//! Uses the filesystem, so it is native only (wasm32-unknown-unknown has no fs).
#![cfg(not(target_arch = "wasm32"))]

use std::fs;
use tb_core::workorders::{resolve_media_path, WorkOrder, WorkOrderClock};

fn wo(number: &str, segment: &str) -> WorkOrder {
    WorkOrder {
        number: number.into(),
        segment: segment.into(),
        ..WorkOrder::default()
    }
}

#[test]
fn work_order_key_tag_label_clock_line() {
    let mut w = wo(" 1234 ", " 2 ");
    assert_eq!(w.key(), "1234-2");
    w.segment = "  ".into();
    assert_eq!(w.key(), "1234");
    w.number = "   ".into();
    w.segment = "2".into();
    assert_eq!(w.key(), "");

    let mut tag = WorkOrder {
        model: "HP450".into(),
        serial: "S77".into(),
        ..wo("1234", "1")
    };
    assert_eq!(tag.job_tag(), "WO 1234-1  HP450 S77");
    tag.serial = String::new();
    assert_eq!(tag.job_tag(), "WO 1234-1  HP450");
    tag.number = String::new();
    assert_eq!(tag.job_tag(), "HP450");
    tag.model = String::new();
    assert_eq!(tag.job_tag(), "");
    assert_eq!(wo("88", "").job_tag(), "WO 88");

    assert_eq!(WorkOrder::default().list_label(), "(no number)");
    let mut lab = WorkOrder {
        customer: " Acme ".into(),
        model: "HP450".into(),
        serial: "S77".into(),
        ..wo("1234", "1")
    };
    assert_eq!(lab.list_label(), "1234-1  Acme  HP450 S77");
    lab.clock_state = "logged-on".into();
    assert_eq!(lab.list_label(), "1234-1  Acme  HP450 S77  [logged on]");
    lab.clock_state = "logged-off".into();
    assert_eq!(lab.list_label(), "1234-1  Acme  HP450 S77  [logged off]");
    lab.clock_state = "idle".into();
    lab.source = "SHARED".into();
    assert_eq!(lab.list_label(), "1234-1  Acme  HP450 S77  (shared)");
    assert_eq!(
        WorkOrder {
            customer: "  ".into(),
            ..wo("5", "")
        }
        .list_label(),
        "5"
    );

    let ev = WorkOrderClock {
        kind: "logon".into(),
        when_utc: "2026-05-01T14:30:00.0000000Z".into(),
        detail: "at bench".into(),
    };
    assert_eq!(
        ev.to_string(),
        "2026-05-01T14:30:00.0000000Z  logon  [shop]  at bench"
    );
}

#[test]
fn work_order_defaults_and_clock() {
    let mut w = WorkOrder::default();
    assert_eq!(w.source, "file");
    assert_eq!(w.clock_state, "idle");
    let ev = w.add_clock("logon", "at bench");
    assert_eq!(w.clock.len(), 1);
    assert_eq!(w.clock[0], ev);
    assert_eq!(ev.when_utc.len(), 28, "{}", ev.when_utc);
    assert_eq!(w.updated_utc.len(), 28);
}

#[test]
fn media_path_confinement() {
    let root = std::env::temp_dir().join(format!("tb-core-media-{}", std::process::id()));
    let media = root.join("media");
    fs::create_dir_all(&media).unwrap();
    let pic = media.join("shot.jpg");
    fs::write(&pic, "x").unwrap();
    let root_s = root.to_string_lossy().into_owned();
    let sep = std::path::MAIN_SEPARATOR;

    assert_eq!(resolve_media_path(&root_s, "shot.jpg"), Some(pic.clone()));
    assert_eq!(
        resolve_media_path(&root_s, &format!("..{sep}secret.txt")),
        None
    );
    assert_eq!(
        resolve_media_path(&root_s, &format!("sub{sep}shot.jpg")),
        None
    );
    assert_eq!(resolve_media_path(&root_s, "  "), None);
    assert_eq!(resolve_media_path("", "shot.jpg"), None);
    // Both separators are refused on every OS, and so is a bare "..".
    assert_eq!(resolve_media_path(&root_s, r"sub\shot.jpg"), None);
    assert_eq!(resolve_media_path(&root_s, ".."), None);
    let _ = fs::remove_dir_all(&root);
}
