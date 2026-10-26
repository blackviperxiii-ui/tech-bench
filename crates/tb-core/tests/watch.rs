//! Shop watch folders: tests/SelfTest.Sync.cs (`ShopWatchTests`), plus the polling snapshot.
#![cfg(not(target_arch = "wasm32"))]

use std::fs;
use std::path::{Path, PathBuf};
use tb_core::kb::shop::SyncSettings;
use tb_core::kb::watch::{watch_dirs, Snapshot};

fn temp(tag: &str) -> PathBuf {
    let p = std::env::temp_dir().join(format!("tb-watch-{tag}-{}", std::process::id()));
    let _ = fs::remove_dir_all(&p);
    fs::create_dir_all(&p).unwrap();
    p
}

fn folder(f: &Path) -> SyncSettings {
    SyncSettings {
        tech_id: "tech".into(),
        sync_folder: f.to_string_lossy().into_owned(),
    }
}

#[test]
fn shop_watch_dirs() {
    let root = temp("root");
    let other = temp("usb");
    let kb = root.to_string_lossy().into_owned();
    assert_eq!(
        watch_dirs("", Some(&SyncSettings::default())).len(),
        0,
        "no kb root watches nothing"
    );
    assert_eq!(
        watch_dirs("", None).len(),
        0,
        "null kb root watches nothing"
    );

    let shared = watch_dirs(&kb, Some(&folder(Path::new(""))));
    assert_eq!(shared.len(), 3, "shared tree watches three folders");
    assert_eq!(shared[0], root.join("data").join("shop"));
    assert_eq!(shared[1], root.join("data").join("notes"));
    assert_eq!(shared[2], root.join("data").join("files"));

    assert_eq!(
        watch_dirs(&kb, None).len(),
        3,
        "null settings watches three folders"
    );
    assert_eq!(
        watch_dirs(&kb, Some(&folder(&root))).len(),
        3,
        "sync folder equal to kb root is shared"
    );
    assert_eq!(
        watch_dirs(&kb, Some(&folder(&other.join("gone")))).len(),
        3,
        "missing sync folder is not watched"
    );
    let usb = watch_dirs(&kb, Some(&folder(&other)));
    assert_eq!(usb.len(), 4, "separate sync folder adds a fourth");
    assert_eq!(usb[3], other, "fourth is the sync folder itself");
    assert!(!shared[0].exists(), "watch_dirs creates nothing");
}

#[test]
fn snapshot_sees_adds_edits_and_deletes() {
    let root = temp("snap");
    let dirs = vec![root.join("shop"), root.join("missing")];
    let empty = Snapshot::take(&dirs);
    assert!(empty.is_empty());

    let note = root.join("shop").join("alice").join("n.txt");
    fs::create_dir_all(note.parent().unwrap()).unwrap();
    fs::write(&note, "one").unwrap();
    let one = Snapshot::take(&dirs);
    assert_eq!(one.len(), 2, "the alice folder and the note");
    assert_ne!(one, empty);
    assert_eq!(Snapshot::take(&dirs), one, "nothing changed");

    fs::write(&note, "one two").unwrap();
    let two = Snapshot::take(&dirs);
    assert_ne!(two, one, "a size change is seen");

    fs::remove_file(&note).unwrap();
    let gone = Snapshot::take(&dirs);
    assert_eq!(gone.len(), 1);
    assert_ne!(gone, two);
}
