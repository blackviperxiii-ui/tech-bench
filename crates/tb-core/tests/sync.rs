//! Shop sync: the sidecar cases from tests/SelfTest.Open.cs (`PacketSidecarSyncTests`) plus the
//! push / pull / conflict / resolve flow, code conflicts and the state and settings files.
#![cfg(not(target_arch = "wasm32"))]

use std::fs;
use std::path::{Path, PathBuf};
use tb_core::kb::shop::{self, SyncSettings};
use tb_core::kb::sync::{self, ShopSyncResult};
use tb_core::kb::user_codes::{self, UserCode};
use tb_core::workorders::WorkOrderStore;

const NOW: &str = "2026-10-26T00:00:00.0000000Z";

struct Rig {
    kb: PathBuf,
    usb: PathBuf,
    cfg: PathBuf,
}

impl Rig {
    fn new(tag: &str) -> Rig {
        let base = std::env::temp_dir().join(format!("tb-sync-{tag}-{}", std::process::id()));
        let _ = fs::remove_dir_all(&base);
        let rig = Rig {
            kb: base.join("kb"),
            usb: base.join("usb"),
            cfg: base.join("cfg"),
        };
        for d in [&rig.kb, &rig.usb, &rig.cfg] {
            fs::create_dir_all(d).unwrap();
        }
        rig
    }

    fn settings(&self, tech: &str) -> SyncSettings {
        SyncSettings {
            tech_id: tech.into(),
            sync_folder: self.usb.to_string_lossy().into_owned(),
        }
    }

    fn kb_str(&self) -> String {
        self.kb.to_string_lossy().into_owned()
    }

    fn run(&self, tech: &str) -> ShopSyncResult {
        sync::run(&self.kb_str(), &self.settings(tech), &self.cfg, NOW, |_| {}).unwrap()
    }

    fn store(&self) -> WorkOrderStore {
        WorkOrderStore::new(&self.cfg, &self.cfg)
    }

    fn resolve(&self, r: &ShopSyncResult, choice: &str) {
        sync::resolve(
            &self.kb_str(),
            &r.conflicts[0],
            choice,
            &self.settings("alice"),
            &self.cfg,
            &self.store(),
        )
        .unwrap();
    }
}

fn write(path: &Path, text: &str) {
    fs::create_dir_all(path.parent().unwrap()).unwrap();
    fs::write(path, text).unwrap();
}

fn read(path: &Path) -> String {
    fs::read_to_string(path).unwrap()
}

fn uc(brand: &str, code: &str, title: &str) -> UserCode {
    UserCode {
        brand: brand.into(),
        code: code.into(),
        title: title.into(),
        ..UserCode::default()
    }
}

#[test]
fn describe_lines() {
    let shared = ShopSyncResult {
        shared_tree: true,
        techs: 2,
        ..Default::default()
    };
    let s = SyncSettings {
        tech_id: "Alice Smith".into(),
        sync_folder: String::new(),
    };
    assert_eq!(
        sync::describe(&s, &shared),
        "Sync: sharing KB folder  ·  tech Alice-Smith  ·  2 tech folders  ·  notes and files in data\\shop stay in this tree"
    );
    let busy = ShopSyncResult {
        pulled: 1,
        pushed: 2,
        skipped: 1,
        techs: 1,
        conflicts: vec![Default::default()],
        notes: vec!["first note".into(), "second".into()],
        ..Default::default()
    };
    let usb = SyncSettings {
        tech_id: "bob".into(),
        sync_folder: "/media/usb".into(),
    };
    assert_eq!(
        sync::describe(&usb, &busy),
        "Sync: folder /media/usb  ·  tech bob  ·  1 tech folder  ·  pulled 1  ·  pushed 2  ·  1 conflict — Shop → Sync  ·  skipped 1  ·  first note"
    );
}

#[test]
fn kinds_and_ignore_rules() {
    assert_eq!(sync::kind_of("alice\\notes\\x.txt"), "note");
    assert_eq!(sync::kind_of("notes\\x.txt"), "note");
    assert_eq!(sync::kind_of("alice\\user-codes.json"), "code-file");
    assert_eq!(sync::kind_of("alice\\J1939-Names.json"), "names");
    assert_eq!(sync::kind_of("alice\\files\\chart.pdf"), "file");
    for skip in [
        "alice\\~$report.docx",
        "a.TMP",
        "wo.json.tmp-abc",
        "wo.json.bak",
        "Desktop.ini",
        "_conflicts\\a.txt",
        "alice\\_conflicts\\a.txt",
    ] {
        assert!(sync::is_ignored(skip), "{skip}");
    }
    for keep in [
        "alice\\notes\\a.txt",
        "tmp\\a.txt",
        "alice\\conflicts\\a.txt",
    ] {
        assert!(!sync::is_ignored(keep), "{keep}");
    }
}

#[test]
fn shared_tree_and_state_path() {
    let rig = Rig::new("shared");
    let kb = rig.kb_str();
    let with = |f: &str| SyncSettings {
        tech_id: "a".into(),
        sync_folder: f.into(),
    };
    assert!(sync::is_shared_tree(&kb, &with("")));
    assert!(sync::is_shared_tree(&kb, &with("   ")));
    assert!(sync::is_shared_tree(&kb, &with(&kb)));
    assert!(sync::is_shared_tree(&kb, &with(&format!("{kb}/"))));
    let shop = rig.kb.join("data").join("shop");
    assert!(sync::is_shared_tree(&kb, &with(&shop.to_string_lossy())));
    assert!(!sync::is_shared_tree(
        &kb,
        &with(&rig.usb.to_string_lossy())
    ));
    assert!(!sync::same_path("", &kb));

    assert_eq!(
        sync::state_path(Path::new("cfg"), "C:\\kb", "D:\\usb"),
        Path::new("cfg").join("sync-state-0c41758ff061.json")
    );
}

#[test]
fn shared_tree_copies_nothing() {
    let rig = Rig::new("noop");
    write(&rig.kb.join("data/notes/a.txt"), "hi");
    let s = SyncSettings {
        tech_id: "alice".into(),
        sync_folder: String::new(),
    };
    let r = sync::run(&rig.kb_str(), &s, &rig.cfg, NOW, |_| {}).unwrap();
    assert!(r.shared_tree);
    assert_eq!((r.pulled, r.pushed, r.conflicts.len()), (0, 0, 0));
    assert_eq!(r.techs, 1);
    assert!(rig.kb.join("data/shop/alice").is_dir());
    assert_eq!(fs::read_dir(&rig.cfg).unwrap().count(), 0);
    assert_eq!(
        r.status,
        "Sync: sharing KB folder  ·  tech alice  ·  1 tech folder  ·  notes and files in data\\shop stay in this tree"
    );

    let none = sync::run("", &s, &rig.cfg, NOW, |_| {}).unwrap();
    assert_eq!(none.status, "No knowledge base folder — nowhere to sync.");
}

#[test]
fn state_file_text() {
    let rig = Rig::new("state");
    write(&rig.kb.join("data/notes/a.txt"), "hi");
    let r = rig.run("alice");
    assert_eq!((r.pushed, r.pulled), (1, 0));
    assert_eq!(read(&rig.usb.join("data/notes/a.txt")), "hi");
    let state = sync::state_path(&rig.cfg, &rig.kb_str(), &rig.usb.to_string_lossy());
    let h = "8f434346648f6b96df89dda901c5176b10a6d83961dd3c1ac88b59b2dc327aa4";
    assert_eq!(
        read(&state),
        format!(
            "{{\r\n  \"lastSyncUtc\": \"{NOW}\",\r\n  \"local\": {{\r\n    \"notes/a.txt\": \"{h}\"\r\n  }},\r\n  \"remote\": {{\r\n    \"notes/a.txt\": \"{h}\"\r\n  }}\r\n}}\r\n"
        )
    );
}

#[test]
fn sidecars_are_never_synced() {
    // PacketSidecarSyncTests
    let rig = Rig::new("sidecar");
    let dir = rig.kb.join("data/shop/alice/work-orders/9");
    write(&dir.join("wo.json"), "{\"Number\":\"9\"}");
    write(&dir.join("wo.json.bak"), "{\"Number\":\"old\"}");
    write(&dir.join("wo.json.tmp-abc"), "{ partial");
    write(&dir.join("notes.txt.tmp-zzz"), "partial notes");
    let r = rig.run("alice");
    let remote = rig.usb.join("data/shop/alice/work-orders/9");
    assert!(remote.join("wo.json").is_file(), "sync copies wo.json");
    assert!(
        !remote.join("wo.json.bak").exists(),
        "sync ignores wo.json.bak"
    );
    assert!(
        !remote.join("wo.json.tmp-abc").exists(),
        "sync ignores wo.json.tmp"
    );
    assert!(
        !remote.join("notes.txt.tmp-zzz").exists(),
        "sync ignores notes tmp"
    );
    assert!(
        r.conflicts.is_empty(),
        "sync does not conflict on bak or tmp"
    );

    write(&remote.join("report.txt.bak"), "old report");
    write(&remote.join("report.txt.tmp-qq"), "partial report");
    rig.run("alice");
    assert!(
        !dir.join("report.txt.bak").exists(),
        "sync does not pull bak"
    );
    assert!(
        !dir.join("report.txt.tmp-qq").exists(),
        "sync does not pull tmp"
    );
}

#[test]
fn push_pull_conflict_and_resolve() {
    let rig = Rig::new("flow");
    let local = rig.kb.join("data/shop/alice/notes/n.txt");
    let remote = rig.usb.join("data/shop/alice/notes/n.txt");
    write(&local, "v1");
    write(&rig.usb.join("data/files/chart.pdf"), "pdf");

    let r = rig.run("alice");
    assert_eq!((r.pushed, r.pulled, r.conflicts.len()), (1, 1, 0));
    assert_eq!(read(&remote), "v1");
    assert_eq!(read(&rig.kb.join("data/files/chart.pdf")), "pdf");

    let r = rig.run("alice");
    assert_eq!((r.pushed, r.pulled), (0, 0), "nothing changed");

    write(&local, "v2 mine");
    let r = rig.run("alice");
    assert_eq!((r.pushed, r.pulled), (1, 0));
    assert_eq!(read(&remote), "v2 mine");

    write(&remote, "v3 theirs");
    let r = rig.run("alice");
    assert_eq!((r.pushed, r.pulled), (0, 1));
    assert_eq!(read(&local), "v3 theirs");

    write(&local, "local v1");
    write(&remote, "remote v1");
    let r = rig.run("alice");
    assert_eq!(r.conflicts.len(), 1);
    let c = &r.conflicts[0];
    assert_eq!(c.kind, "note");
    assert_eq!(c.relative_path, "alice\\notes\\n.txt");
    assert_eq!(c.prefix, "shop");
    assert_eq!(c.to_string(), "NOTE  alice\\notes\\n.txt");
    assert_eq!(read(&local), "local v1", "a conflict never overwrites");
    assert_eq!(read(&remote), "remote v1");
    assert!(
        r.status.contains("  ·  1 conflict — Shop → Sync"),
        "{}",
        r.status
    );

    rig.resolve(&r, "local");
    assert_eq!(read(&remote), "local v1");
    let r = rig.run("alice");
    assert_eq!((r.pushed, r.pulled, r.conflicts.len()), (0, 0, 0));

    write(&local, "mine again");
    write(&remote, "theirs again");
    let r = rig.run("alice");
    rig.resolve(&r, "remote");
    assert_eq!(read(&local), "theirs again");
    assert_eq!(rig.run("alice").conflicts.len(), 0);

    write(&local, "keep A");
    write(&remote, "keep B");
    let r = rig.run("alice");
    rig.resolve(&r, "both");
    let extra = rig.kb.join("data/shop/alice/notes/n (from other tech).txt");
    assert_eq!(read(&extra), "keep B");
    assert_eq!(read(&local), "keep A");
    assert_eq!(
        read(&remote),
        "keep A",
        "keep both sends ours to the sync folder"
    );
    let r = rig.run("alice");
    assert_eq!(r.conflicts.len(), 0, "keep both settles the conflict");
    assert_eq!((r.pushed, r.pulled), (1, 0), "the kept copy goes out too");
    assert_eq!(
        read(
            &rig.usb
                .join("data/shop/alice/notes/n (from other tech).txt")
        ),
        "keep B"
    );

    write(&local, "third A");
    write(&remote, "third B");
    let r = rig.run("alice");
    rig.resolve(&r, "both");
    assert_eq!(
        read(
            &rig.kb
                .join("data/shop/alice/notes/n (from other tech 2).txt")
        ),
        "third B",
        "a second kept copy gets the next free name"
    );
}

#[test]
fn big_files_are_skipped() {
    let rig = Rig::new("big");
    let big = rig.kb.join("data/files/big.bin");
    fs::create_dir_all(big.parent().unwrap()).unwrap();
    fs::File::create(&big)
        .unwrap()
        .set_len(sync::MAX_BYTES + 1)
        .unwrap();
    let r = rig.run("alice");
    assert_eq!((r.skipped, r.pushed), (1, 0));
    assert_eq!(r.notes, vec!["Skipped (over 50 MB): big.bin".to_string()]);
    assert!(!rig.usb.join("data/files/big.bin").exists());
    assert!(r
        .status
        .ends_with("  ·  skipped 1  ·  Skipped (over 50 MB): big.bin"));
}

#[test]
fn code_conflicts_between_shards() {
    let rig = Rig::new("codes");
    let kb = rig.kb.as_path();
    let mut a = uc("Cummins", "F1", "Low oil");
    a.causes = vec!["sensor".into()];
    shop::save_user_code(kb, "alice", a.clone()).unwrap();
    shop::save_user_code(kb, "bob", uc("cummins", "f1", "Oil low")).unwrap();
    shop::save_user_code(kb, "bob", uc("Cummins", "F2", "Same")).unwrap();
    shop::save_user_code(kb, "alice", uc("Cummins", "F2", "Same")).unwrap();

    let s = SyncSettings {
        tech_id: "alice".into(),
        sync_folder: String::new(),
    };
    let run = || sync::run(&rig.kb_str(), &s, &rig.cfg, NOW, |_| {}).unwrap();
    let r = run();
    assert_eq!(r.conflicts.len(), 1, "identical F2 is not a conflict");
    let c = &r.conflicts[0];
    assert_eq!(c.kind, "code");
    assert_eq!(c.key, "cummins\u{1}f1");
    assert_eq!(
        c.to_string(),
        "CODE  cummins\u{1}f1  —  alice: Low oil  vs  bob: Oil low"
    );
    assert_eq!(
        Path::new(&c.remote_path),
        shop::shard_codes_path(kb, "bob").as_path()
    );

    let store = rig.store();
    sync::resolve(&rig.kb_str(), c, "remote", &s, &rig.cfg, &store).unwrap();
    let picked = user_codes::load_from(&kb.join("data/shop/_resolved/user-codes.json")).unwrap();
    assert_eq!(picked.len(), 1);
    assert_eq!(picked[0].title, "Oil low");
    assert_eq!(run().conflicts.len(), 0, "resolved key is not reported");
    assert_eq!(run().techs, 2, "_resolved is not a tech");

    shop::save_user_code(kb, "alice", uc("Doosan", "X9", "One")).unwrap();
    shop::save_user_code(kb, "bob", uc("Doosan", "X9", "Two")).unwrap();
    let r = run();
    assert_eq!(r.conflicts.len(), 1);
    sync::resolve(&rig.kb_str(), &r.conflicts[0], "both", &s, &rig.cfg, &store).unwrap();
    sync::resolve(&rig.kb_str(), &r.conflicts[0], "both", &s, &rig.cfg, &store).unwrap();
    assert_eq!(
        read(&kb.join("data/shop/_resolved/keep-both.txt")),
        "doosan\u{1}x9\r\n"
    );
    assert_eq!(run().conflicts.len(), 0);
}

#[test]
fn fingerprint_covers_every_field() {
    let mut c = uc("Cummins", "F1", "T");
    c.causes = vec!["a".into()];
    assert_eq!(
        user_codes::fingerprint(&c),
        "a077c55c25de912e4edfd97afabfd62eeb1ea8d688697a076358e33dd619c29a"
    );
    let mut d = c.clone();
    d.author = "bob".into();
    assert_eq!(user_codes::fingerprint(&d), user_codes::fingerprint(&c));
    d.safety = "Lock out".into();
    assert_ne!(user_codes::fingerprint(&d), user_codes::fingerprint(&c));
}

#[test]
fn legacy_codes_copied_once() {
    let rig = Rig::new("legacy");
    user_codes::save_to(
        &user_codes::path_for(&rig.kb),
        &[uc("Cummins", "F1", "Old")],
    )
    .unwrap();
    let r = rig.run("alice");
    assert_eq!(
        r.notes,
        vec!["Copied data\\user-codes.json into your shop folder.".to_string()]
    );
    let shard = user_codes::load_from(&shop::shard_codes_path(&rig.kb, "alice")).unwrap();
    assert_eq!(shard[0].title, "Old");
    assert!(
        rig.run("alice").notes.is_empty(),
        "second run leaves the shard"
    );
}

#[test]
fn work_order_conflicts_come_from_the_caller() {
    let rig = Rig::new("wo");
    let r = sync::run(&rig.kb_str(), &rig.settings("alice"), &rig.cfg, NOW, |r| {
        r.conflicts.push(tb_core::workorders::ShopConflict {
            kind: "work-order".into(),
            key: "9001-01".into(),
            relative_path: String::new(),
            local_path: String::new(),
            remote_path: String::new(),
            local_summary: "a".into(),
            remote_summary: "b".into(),
            prefix: "wo".into(),
        })
    })
    .unwrap();
    assert_eq!(r.conflicts.len(), 1);
    assert!(r.status.contains("1 conflict — Shop → Sync"));
}

#[test]
fn settings_round_trip() {
    let rig = Rig::new("settings");
    let saved = sync::save_settings(
        &rig.cfg,
        &SyncSettings {
            tech_id: "My Tech.".into(),
            sync_folder: "D:\\share".into(),
        },
    )
    .unwrap();
    assert_eq!(saved.tech_id, "My-Tech");
    assert_eq!(
        read(&rig.cfg.join("sync.json")),
        "{\"TechId\":\"My-Tech\",\"SyncFolder\":\"D:\\\\share\"}"
    );
    let exe = rig.cfg.join("exe");
    let back = sync::load_settings(&rig.cfg, "user", &exe);
    assert_eq!(back, saved);

    sync::save_settings(
        &rig.cfg,
        &SyncSettings {
            tech_id: String::new(),
            sync_folder: String::new(),
        },
    )
    .unwrap();
    write(
        &exe.join("sync-path.txt"),
        "# shop share\r\n\r\n  \\\\server\\shop  \r\n",
    );
    let fallback = sync::load_settings(&rig.cfg, "user", &exe);
    assert_eq!(fallback.sync_folder, "\\\\server\\shop");
    assert_eq!(
        fallback.tech_id, "tech",
        "a blank name saves as tech, like C#"
    );
}

#[test]
fn screen_and_idle_text() {
    let rig = Rig::new("screen");
    fs::create_dir_all(rig.kb.join("data/shop/alice")).unwrap();
    fs::create_dir_all(rig.kb.join("data/shop/_resolved")).unwrap();
    let s = rig.settings("alice");
    let folder = rig.usb.to_string_lossy();
    assert_eq!(
        sync::idle_text(&rig.kb_str(), &s, None, "Settings saved."),
        format!("Sync: folder {folder}  ·  tech alice  ·  1 tech folder\nSettings saved.")
    );
    let r = ShopSyncResult {
        status: "Sync: x".into(),
        notes: vec!["n1".into()],
        conflicts: vec![tb_core::workorders::ShopConflict {
            kind: "code".into(),
            ..Default::default()
        }],
        ..Default::default()
    };
    assert_eq!(
        sync::screen_text(&r),
        "Sync: x\nn1\n\nSelect a conflict, then Keep mine / Keep theirs / Keep both.\nThis code exists in two tech folders with different text."
    );
    let quiet = ShopSyncResult {
        status: "Sync: y".into(),
        ..Default::default()
    };
    assert_eq!(sync::screen_text(&quiet), "Sync: y");
}
