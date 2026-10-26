//! Ported from tests/SelfTest.Job.cs (WorkOrderTests, WorkOrderMergeTests,
//! WorkOrderBakRecoveryTests). Same packets and literal expected values. The File.Replace
//! copy-fallback case has no Rust counterpart (rename always replaces) and is not ported.
//! Uses the filesystem, so it is native only (wasm32-unknown-unknown has no fs).
#![cfg(not(target_arch = "wasm32"))]

use std::fs;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicUsize, Ordering};
use tb_core::settings::atomic::write_atomic_temp;
use tb_core::settings::ShopSettings;
use tb_core::time::round_trip_from_ticks;
use tb_core::workorders::store::{
    packet_dir, parse_assigned_csv, parse_assigned_json, safe_key, shop_shared_dir, shop_tech_dir,
};
use tb_core::workorders::{WorkOrder, WorkOrderStore};

static SEQ: AtomicUsize = AtomicUsize::new(0);

struct TempDir(PathBuf);

impl TempDir {
    fn new(tag: &str) -> Self {
        let n = SEQ.fetch_add(1, Ordering::SeqCst);
        let p = std::env::temp_dir().join(format!("tb-wo-{tag}-{}-{n}", std::process::id()));
        let _ = fs::remove_dir_all(&p);
        fs::create_dir_all(&p).unwrap();
        TempDir(p)
    }
}

impl Drop for TempDir {
    fn drop(&mut self) {
        let _ = fs::remove_dir_all(&self.0);
    }
}

struct Fixture {
    _dir: TempDir,
    root: PathBuf,
    kb: String,
    share: PathBuf,
    store: WorkOrderStore,
    shop: ShopSettings,
}

fn fixture(tag: &str) -> Fixture {
    let dir = TempDir::new(tag);
    let root = dir.0.clone();
    let kb = root.join("kb");
    fs::create_dir_all(kb.join("data")).unwrap();
    let share = root.join("share");
    fs::create_dir_all(&share).unwrap();
    let exe = root.join("exe");
    fs::create_dir_all(&exe).unwrap();
    let mut store = WorkOrderStore::new(root.join("cfg"), exe);
    store.local_root = root.join("local-wo");
    let shop = ShopSettings {
        tech_id: "user".into(),
        share_folder: share.to_string_lossy().into_owned(),
        assigned_file: String::new(),
    };
    Fixture {
        _dir: dir,
        root,
        kb: kb.to_string_lossy().into_owned(),
        share,
        store,
        shop,
    }
}

fn read(p: &Path) -> String {
    fs::read_to_string(p).unwrap()
}

fn has_temp(dir: &Path) -> bool {
    fn walk(d: &Path) -> bool {
        fs::read_dir(d).into_iter().flatten().flatten().any(|e| {
            let p = e.path();
            if p.is_dir() {
                walk(&p)
            } else {
                e.file_name()
                    .to_string_lossy()
                    .to_lowercase()
                    .contains(".tmp-")
            }
        })
    }
    dir.is_dir() && walk(dir)
}

fn find<'a>(list: &'a [WorkOrder], number: &str) -> Option<&'a WorkOrder> {
    list.iter().find(|w| w.number == number)
}

/// An updated stamp `hours` ahead of now, in round-trip form.
fn ahead(hours: u64) -> String {
    let now = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .unwrap();
    round_trip_from_ticks((now.as_nanos() / 100) as u64 + hours * 3600 * 10_000_000)
}

#[test]
fn work_orders_and_shop_share() {
    let mut f = fixture("share");
    assert_eq!(safe_key("WO 12/34"), "WO-12-34");
    assert_eq!(safe_key("***"), "");

    let mut wo = WorkOrder {
        number: "44551".into(),
        segment: "01".into(),
        customer: "Acme Quarry".into(),
        model: "HP450".into(),
        serial: "12345".into(),
        notes: "No start. Check 4-pin.".into(),
        description: "Won't crank".into(),
        ..WorkOrder::default()
    };
    assert_eq!(wo.key(), "44551-01");
    assert!(wo.job_tag().contains("WO 44551-01"), "{}", wo.job_tag());

    f.store.save(&mut wo, &f.shop, &f.kb).unwrap();
    let packet = f.store.local_root.join("44551-01");
    assert!(packet.join("wo.json").is_file());
    assert!(read(&packet.join("notes.txt")).contains("4-pin"));
    assert!(shop_tech_dir(&f.kb, "user")
        .join("44551-01")
        .join("wo.json")
        .is_file());

    let loaded = WorkOrderStore::load(&packet).expect("reload packet");
    assert_eq!(loaded.customer, "Acme Quarry");
    assert!(loaded.notes.contains("4-pin"), "{}", loaded.notes);

    let json_path = packet.join("wo.json");
    let notes_path = packet.join("notes.txt");
    assert_eq!(
        &fs::read(&notes_path).unwrap()[..3],
        &[0xEF, 0xBB, 0xBF],
        "notes.txt BOM"
    );
    let json_bytes = fs::read(&json_path).unwrap();
    assert_eq!(json_bytes[0], b'{', "wo.json is UTF-8 without BOM");

    let first_json = read(&json_path);
    wo.notes = "Second visit notes.".into();
    f.store.save(&mut wo, &f.shop, &f.kb).unwrap();
    let second_json = read(&json_path);
    assert_eq!(
        read(&packet.join("wo.json.bak")),
        first_json,
        "wo.json.bak is previous content"
    );
    assert!(second_json.contains("Second visit notes.") && second_json != first_json);
    assert!(read(&packet.join("notes.txt.bak")).contains("4-pin"));
    assert!(read(&notes_path).contains("Second visit notes."));
    assert!(!has_temp(&packet), "no leftover tmp after second save");
    let bak_wo = WorkOrder::from_json(&read(&packet.join("wo.json.bak"))).expect("bak parses");
    assert_eq!(bak_wo.number, "44551");
    assert!(bak_wo.notes.contains("4-pin"), "{}", bak_wo.notes);
    let shop_packet = shop_tech_dir(&f.kb, "user").join("44551-01");
    assert!(
        !shop_packet.join("wo.json.bak").exists(),
        "shop copy ignores wo.json.bak"
    );
    assert!(
        !shop_packet.join("notes.txt.bak").exists(),
        "shop copy ignores notes.txt.bak"
    );
    assert!(!has_temp(&shop_packet));
    assert_eq!(read(&shop_packet.join("wo.json")), read(&json_path));

    let stray = write_atomic_temp(&json_path, "{ truncated").unwrap();
    assert!(
        stray
            .to_string_lossy()
            .starts_with(&format!("{}.tmp-", json_path.to_string_lossy())),
        "crash temp sits beside wo.json: {}",
        stray.display()
    );
    assert_eq!(read(&json_path), second_json, "crash leaves wo.json intact");
    let after_crash = WorkOrderStore::load(&packet).unwrap();
    assert_eq!(after_crash.number, "44551");
    assert!(after_crash.notes.contains("Second visit notes."));
    fs::remove_file(&stray).unwrap();
    assert!(!has_temp(&packet));

    wo.report_text = "report v1".into();
    f.store.save(&mut wo, &f.shop, &f.kb).unwrap();
    wo.report_text = "report v2".into();
    f.store.save(&mut wo, &f.shop, &f.kb).unwrap();
    assert_eq!(
        fs::read_to_string(packet.join("report.txt")).unwrap(),
        "\u{feff}report v2"
    );
    assert_eq!(
        fs::read_to_string(packet.join("report.txt.bak")).unwrap(),
        "\u{feff}report v1"
    );
    assert!(!has_temp(&packet));

    let pic = f.root.join("shot.png");
    fs::write(&pic, "png-bytes").unwrap();
    f.store.attach_file(&mut wo, &pic, &f.shop, &f.kb).unwrap();
    assert!(!wo.media.is_empty(), "media listed");

    let media_dir = packet.join("media");
    fs::write(media_dir.join("shot.png.bak"), "old-png").unwrap();
    fs::write(media_dir.join("shot.png.tmp-dead"), "partial-png").unwrap();
    let media_loaded = WorkOrderStore::load(&packet).unwrap();
    assert!(media_loaded
        .media
        .iter()
        .any(|m| m.eq_ignore_ascii_case("shot.png")));
    assert!(!media_loaded
        .media
        .iter()
        .any(|m| m.eq_ignore_ascii_case("shot.png.bak")));
    assert!(!media_loaded.media.iter().any(|m| m.contains(".tmp-")));
    f.store.save(&mut wo, &f.shop, &f.kb).unwrap();
    assert!(!shop_packet.join("media").join("shot.png.bak").exists());
    assert!(!shop_packet.join("media").join("shot.png.tmp-dead").exists());
    let listed = f.store.list_all(&f.shop, &f.kb);
    assert_eq!(listed.iter().filter(|w| w.number == "44551").count(), 1);
    assert!(f.store.conflicts.is_empty(), "{:?}", f.store.conflicts);

    wo.report_text = "TECH BENCH — DIAGNOSTIC REPORT".into();
    let dest = f.store.share(&mut wo, &f.shop, &f.kb).unwrap();
    assert!(dest.is_dir(), "{}", dest.display());
    assert!(shop_shared_dir(&f.kb)
        .join("44551-01")
        .join("wo.json")
        .is_file());
    assert!(f
        .share
        .join("work-orders")
        .join("44551-01")
        .join("wo.json")
        .is_file());
    assert!(f
        .share
        .join("data")
        .join("shop")
        .join("_shared")
        .join("work-orders")
        .join("44551-01")
        .join("wo.json")
        .is_file());
    assert_eq!(wo.clock.last().map(|c| c.kind.as_str()), Some("share"));

    let from_json =
        parse_assigned_json(r#"[{"Number":"99","Customer":"Bob","Model":"XHP750","Serial":"S1"}]"#);
    assert_eq!(from_json.len(), 1);
    assert_eq!(from_json[0].number, "99");
    let wrapped = parse_assigned_json(
        r#"{"value":[{"WorkOrder":"77","Segment":"02","CustomerName":"Cat"}]}"#,
    );
    assert_eq!(wrapped.len(), 1);
    assert_eq!(wrapped[0].number, "77");
    assert_eq!(
        (wrapped[0].segment.as_str(), wrapped[0].customer.as_str()),
        ("02", "Cat")
    );
    let csv = parse_assigned_csv("Number,Customer,Model\n88,Delta,HP1600\n");
    assert_eq!(csv.len(), 1);
    assert_eq!(csv[0].model, "HP1600");

    fs::write(
        f.store.exe_dir.join("id-work-orders.json"),
        r#"[{"Number":"SIDECAR","Customer":"From file"}]"#,
    )
    .unwrap();
    let all = f.store.list_all(&f.shop, &f.kb);
    assert!(find(&all, "44551").is_some(), "list includes saved WO");
    assert_eq!(
        find(&all, "SIDECAR").map(|w| w.customer.as_str()),
        Some("From file")
    );
}

#[test]
fn work_order_merge_conflicts() {
    let mut f = fixture("merge");
    let mut clash = WorkOrder {
        number: "9001".into(),
        segment: "01".into(),
        customer: "Clash Co".into(),
        notes: "Local notes A".into(),
        ..WorkOrder::default()
    };
    f.store.save(&mut clash, &f.shop, &f.kb).unwrap();
    let local_dir = f.store.local_root.join("9001-01");
    let shop_dir = shop_tech_dir(&f.kb, "user").join("9001-01");
    let mut shop_wo = WorkOrderStore::load(&shop_dir).expect("shop shard exists for merge");
    shop_wo.notes = "Remote notes B".into();
    shop_wo.updated_utc = ahead(2);
    fs::write(shop_dir.join("notes.txt"), &shop_wo.notes).unwrap();
    fs::write(shop_dir.join("wo.json"), shop_wo.to_json()).unwrap();

    let listed = f.store.list_all(&f.shop, &f.kb);
    let w = find(&listed, "9001").unwrap();
    assert!(
        w.notes.contains("Remote notes B"),
        "newer remote packet listed: {}",
        w.notes
    );
    assert!(read(&local_dir.join("notes.txt")).contains("Local notes A"));
    let wo_clash = f
        .store
        .conflicts
        .iter()
        .find(|c| c.kind == "work-order" && c.key.contains("9001"))
        .cloned()
        .expect("same WO key disagreement is a conflict");
    assert_eq!(
        wo_clash.to_string(),
        "WO  9001-01  —  Local notes A  vs  Remote notes B"
    );

    f.store.resolve(&wo_clash, "remote", &f.kb).unwrap();
    let after = f.store.list_all(&f.shop, &f.kb);
    assert!(
        !f.store.conflicts.iter().any(|c| c.key.contains("9001")),
        "resolve remote clears WO conflict"
    );
    assert!(read(&local_dir.join("notes.txt")).contains("Remote notes B"));
    assert!(find(&after, "9001")
        .unwrap()
        .notes
        .contains("Remote notes B"));

    let mut same = WorkOrder {
        number: "9002".into(),
        segment: "01".into(),
        notes: "Same notes".into(),
        ..WorkOrder::default()
    };
    f.store.save(&mut same, &f.shop, &f.kb).unwrap();
    let shop2 = shop_tech_dir(&f.kb, "user").join("9002-01");
    let mut shop_same = WorkOrderStore::load(&shop2).unwrap();
    let newer = ahead(3);
    shop_same.updated_utc = newer.clone();
    fs::write(shop2.join("wo.json"), shop_same.to_json()).unwrap();
    let listed_same = f.store.list_all(&f.shop, &f.kb);
    assert!(
        !f.store.conflicts.iter().any(|c| c.key.contains("9002")),
        "same content is not a conflict"
    );
    assert_eq!(find(&listed_same, "9002").unwrap().updated_utc, newer);

    // Keep both: the clash stays on disk on both sides but stops being reported.
    let mut both = WorkOrder {
        number: "9003".into(),
        notes: "mine".into(),
        ..WorkOrder::default()
    };
    f.store.save(&mut both, &f.shop, &f.kb).unwrap();
    let shop3 = shop_tech_dir(&f.kb, "user").join("9003");
    fs::write(shop3.join("notes.txt"), "theirs").unwrap();
    f.store.list_all(&f.shop, &f.kb);
    let c3 = f
        .store
        .conflicts
        .iter()
        .find(|c| c.key == "9003")
        .cloned()
        .expect("9003 conflicts");
    f.store.resolve(&c3, " Both ", &f.kb).unwrap();
    f.store.list_all(&f.shop, &f.kb);
    assert!(!f.store.conflicts.iter().any(|c| c.key == "9003"));
    assert_eq!(
        read(
            &Path::new(&f.kb)
                .join("data")
                .join("shop")
                .join("_resolved")
                .join("keep-both-wo.txt")
        ),
        "9003\r\n"
    );
}

#[test]
fn work_order_bak_recovery() {
    let mut f = fixture("bak");
    let mut wo = WorkOrder {
        number: "77701".into(),
        segment: "01".into(),
        customer: "Prev Co".into(),
        notes: "prev note".into(),
        ..WorkOrder::default()
    };
    f.store.save(&mut wo, &f.shop, &f.kb).unwrap();
    let packet = packet_dir(&f.store.local_root, &wo.key());
    let json_path = packet.join("wo.json");
    let first_json = read(&json_path);
    wo.customer = "Curr Co".into();
    wo.notes = "curr note".into();
    f.store.save(&mut wo, &f.shop, &f.kb).unwrap();

    assert_eq!(WorkOrderStore::load(&packet).unwrap().customer, "Curr Co");
    let bak_text = read(&packet.join("wo.json.bak"));
    assert_eq!(bak_text, first_json);
    let recovered = WorkOrder::from_json(&bak_text).unwrap();
    assert_eq!(
        (recovered.number.as_str(), recovered.customer.as_str()),
        ("77701", "Prev Co")
    );

    let only_bak = f.store.local_root.join("77702");
    fs::create_dir_all(&only_bak).unwrap();
    fs::write(
        only_bak.join("wo.json.bak"),
        r#"{"Number":"77702","Customer":"Bak Only"}"#,
    )
    .unwrap();
    assert!(
        WorkOrderStore::load(&only_bak).is_none(),
        "bak-only folder is not loaded"
    );
    assert!(find(&f.store.list_all(&f.shop, &f.kb), "77702").is_none());
}

#[test]
fn save_rejects_blank_number_and_reads_csharp_packet() {
    let f = fixture("csharp");
    let mut blank = WorkOrder::default();
    let err = f.store.save(&mut blank, &f.shop, &f.kb).unwrap_err();
    assert_eq!(err.to_string(), "Work order number is required.");

    // What the WinForms app writes: JavaScriptSerializer wo.json and a BOM'd notes.txt.
    let dir = f.root.join("from-csharp");
    fs::create_dir_all(dir.join("media")).unwrap();
    fs::write(
        dir.join("wo.json"),
        r#"{"Number":"5","Segment":"","Customer":"A \u0026 B","CustomerNo":"","Model":"HP450","Serial":"","Stock":"","Description":"","AssignedTech":"","Notes":"stale","ReportText":"","Source":"file","ClockState":"logged-on","UpdatedUtc":"2026-05-01T14:30:00.0000000Z","Folder":"","Media":["a.jpg"],"Clock":[{"Kind":"on","WhenUtc":"2026-05-01T14:00:00.0000000Z","Detail":"x"}]}"#,
    )
    .unwrap();
    fs::write(dir.join("notes.txt"), "\u{feff}fresh notes").unwrap();
    fs::write(dir.join("media").join("b.jpg"), "b").unwrap();
    let wo = WorkOrderStore::load(&dir).unwrap();
    assert_eq!(wo.customer, "A & B");
    assert_eq!(
        wo.notes, "fresh notes",
        "notes.txt wins and its BOM is dropped"
    );
    assert_eq!(wo.media, ["a.jpg", "b.jpg"]);
    assert_eq!(wo.clock.len(), 1);
    assert_eq!(wo.list_label(), "5  A & B  HP450  [logged on]");
    assert!(wo
        .to_json()
        .starts_with(r#"{"Number":"5","Segment":"","Customer":"A \u0026 B","#));
    assert!(wo
        .to_json()
        .contains(r#""Folder":"","Media":["a.jpg","b.jpg"],"Clock":[{"Kind":"on","#));
}
