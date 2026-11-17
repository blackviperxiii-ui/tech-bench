//! End to end: one tech's saved INLINE 7 session becomes another tech's unit history, through the
//! same steps the apps run (ShellForm after Save, RunSync and SyncForm afterwards):
//! session_files -> publish_history -> shop sync push -> shop sync pull -> import_history ->
//! History::load(...).summary(job).
#![cfg(not(target_arch = "wasm32"))]

use std::fs;
use std::path::{Path, PathBuf};
use tb_core::history::History;
use tb_core::j1939::monitor::BusMonitor;
use tb_core::j1939::report::{session_files, LocalNow};
use tb_core::j1939::{Dtc, Names};
use tb_core::kb::shop::SyncSettings;
use tb_core::kb::sync;

const NOW: &str = "2026-10-26T00:00:00.0000000Z";
const JOB: &str = "HP450 12345";

fn dir(base: &Path, name: &str) -> PathBuf {
    let d = base.join(name);
    fs::create_dir_all(&d).unwrap();
    d
}

#[test]
fn one_techs_session_is_the_next_techs_unit_history() {
    let base = std::env::temp_dir().join(format!("tb-hist-e2e-{}", std::process::id()));
    let _ = fs::remove_dir_all(&base);
    let usb = dir(&base, "usb");
    let (kb_a, cfg_a, sessions_a) = (
        dir(&base, "a-kb"),
        dir(&base, "a-cfg"),
        dir(&base, "a-sessions"),
    );
    let (kb_b, cfg_b, sessions_b) = (
        dir(&base, "b-kb"),
        dir(&base, "b-cfg"),
        dir(&base, "b-sessions"),
    );
    let settings = |tech: &str| SyncSettings {
        tech_id: tech.into(),
        sync_folder: usb.to_string_lossy().into_owned(),
    };

    // Tech A saves a session with SPN 1761 FMI 9 active (what Save on INLINE 7 writes).
    let mut s = BusMonitor::new().snapshot(-1);
    s.connected = true;
    s.active = vec![Dtc {
        spn: 1761,
        fmi: 9,
        occ: 3,
        name: "DEF tank level".into(),
        fmi_text: "abnormal update rate".into(),
    }];
    let now = LocalNow {
        local: "2026-05-01 07:16:00".into(),
        stamp: "20260501_071600".into(),
        iso: "2026-05-01T07:16:00.0000000-05:00".into(),
    };
    let f = session_files(
        &s,
        &[],
        &Names::new(),
        &[],
        String::new(),
        String::new(),
        &[],
        JOB,
        &now,
    );
    let dtc = sessions_a.join(format!("{}_dtcs.csv", f.base));
    fs::write(&dtc, &f.dtc_csv).unwrap();

    // A publishes it to the shop and syncs; B syncs and imports.
    sync::publish_history(&kb_a, "alice", &dtc).unwrap();
    sync::run(
        &kb_a.to_string_lossy(),
        &settings("alice"),
        &cfg_a,
        NOW,
        |_| {},
    )
    .unwrap();
    assert_eq!(
        History::load(&sessions_b).summary(JOB).lines().next(),
        Some("No saved sessions yet."),
        "B has nothing before the sync"
    );
    sync::run(
        &kb_b.to_string_lossy(),
        &settings("bob"),
        &cfg_b,
        NOW,
        |_| {},
    )
    .unwrap();
    assert_eq!(sync::import_history(&kb_b, &sessions_b).unwrap(), 1);

    let summary = History::load(&sessions_b).summary(JOB);
    assert!(
        summary.starts_with("This unit has been here before — 1 distinct fault(s) on file:\n\n  SPN 1761   FMI 9   seen on 1 visit   last 2026-05-01  DEF tank level\n"),
        "{summary}"
    );
    // A second import brings nothing new.
    assert_eq!(sync::import_history(&kb_b, &sessions_b).unwrap(), 0);
    fs::remove_dir_all(&base).unwrap();
}
