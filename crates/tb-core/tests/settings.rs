//! Ported from tests/SelfTest.Persist.cs and the AdapterPrefs cases in tests/SelfTest.Adapter.cs.
//! The C# source-level checks on ShellForm / Inline7Control / WorkOrderControl are WinForms
//! wiring and are not ported; the Tauri shell gets its own startup tests.

use std::fs;
use std::path::PathBuf;
use std::sync::atomic::{AtomicUsize, Ordering};
use tb_core::settings::atomic::bak_path;
use tb_core::settings::{match_work_order_key, AdapterPrefsData, AppSettings, SettingsStore};
use tb_core::workorders::WorkOrder;

static SEQ: AtomicUsize = AtomicUsize::new(0);

struct TempDir(PathBuf);

impl TempDir {
    fn new(tag: &str) -> Self {
        let n = SEQ.fetch_add(1, Ordering::SeqCst);
        let p = std::env::temp_dir().join(format!("tb-core-{tag}-{}-{n}", std::process::id()));
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

#[test]
fn persist_restart_and_bad_store() {
    let dir = TempDir::new("persist-restart");
    let store = SettingsStore::new(&dir.0);
    let path = store.settings_path();

    let none = store.load_app();
    assert_eq!(none.customer, "");
    assert_eq!(none.last_wo, "");
    assert!(!path.exists(), "read does not create the store");

    assert_eq!(
        store.save_job_customer("  Acme Quarry  ").unwrap(),
        "Acme Quarry"
    );
    assert_eq!(
        store.save_job_work_order(Some(" 44551-01 ")).unwrap(),
        "44551-01"
    );
    let restarted = SettingsStore::new(&dir.0).load_app();
    assert_eq!(restarted.customer, "Acme Quarry");
    assert_eq!(restarted.last_wo, "44551-01");
    assert_eq!(store.save_job_work_order(None).unwrap(), "");
    assert_eq!(store.load_app().last_wo, "");
    assert_eq!(
        store.load_app().customer,
        "Acme Quarry",
        "customer kept when only wo cleared"
    );

    let bad = [
        "",
        "   ",
        "{ not json",
        "{\"Customer\":\"Trunc",
        "null",
        "[]",
        "{\"Width\":\"abc\"}",
        "{\"Customer\":null,\"LastWo\":null}",
    ];
    for (i, b) in bad.iter().enumerate() {
        fs::write(&path, b).unwrap();
        let s = store.load_app();
        assert!(
            s.customer.is_empty() && s.last_wo.is_empty(),
            "bad store #{i} loads defaults: {b}"
        );
    }

    fs::write(&path, "{ corrupt").unwrap();
    assert_eq!(
        store.save_job_customer("Recovered Co").unwrap(),
        "Recovered Co"
    );
    assert_eq!(store.load_app().customer, "Recovered Co");
    fs::write(&path, "{ corrupt").unwrap();
    assert_eq!(store.save_job_work_order(Some("12-34")).unwrap(), "12-34");
    assert_eq!(store.load_app().last_wo, "12-34");

    assert_eq!(match_work_order_key(&[], "gone-99"), "");
    let orders = [WorkOrder {
        number: "12".into(),
        segment: "34".into(),
        ..WorkOrder::default()
    }];
    assert_eq!(match_work_order_key(&orders, " 12-34 "), "12-34");
    assert_eq!(match_work_order_key(&orders, "gone-99"), "");
}

#[test]
fn persist_startup_no_wipe() {
    let dir = TempDir::new("persist-startup");
    let mut store = SettingsStore::new(&dir.0);
    let settings_path = store.settings_path();
    let prefs_path = store.prefs_path();

    let job = AppSettings {
        model: "HP450".into(),
        serial: "S-99".into(),
        customer: "Acme Quarry".into(),
        last_wo: "44551-01".into(),
        width: 1180,
        height: 720,
        ..AppSettings::default()
    };
    store.save_app_to(&job, &settings_path).unwrap();
    store
        .save_prefs(&AdapterPrefsData {
            adapter_id: "CMNSI7".into(),
            shell_page: 2,
            inner_page: 4,
        })
        .unwrap();
    let settings_before = fs::read_to_string(&settings_path).unwrap();
    let prefs_before = fs::read_to_string(&prefs_path).unwrap();

    // Simulated launch: while the gate is closed, fills must not touch disk.
    store.begin_load();
    assert!(store.is_loading());
    assert!(!store.allow_write());
    let mut wipe = AppSettings {
        width: 800,
        height: 600,
        ..AppSettings::default()
    };
    store.save_app(&mut wipe).unwrap();
    store.save_app_to(&wipe, &settings_path).unwrap();
    store
        .merge_adapter_prefs(Some(""), Some(0), Some(0))
        .unwrap();
    store.save_prefs(&AdapterPrefsData::default()).unwrap();
    assert_eq!(fs::read_to_string(&settings_path).unwrap(), settings_before);
    assert_eq!(fs::read_to_string(&prefs_path).unwrap(), prefs_before);

    let still = store.load_app();
    assert_eq!(still.customer, "Acme Quarry");
    assert_eq!(still.last_wo, "44551-01");
    assert_eq!(still.model, "HP450");
    let prefs_still = store.load_prefs();
    assert_eq!(prefs_still.adapter_id, "CMNSI7");
    assert_eq!(prefs_still.shell_page, 2);
    assert_eq!(prefs_still.inner_page, 4);

    store.end_load();
    assert!(!store.is_loading());
    assert!(store.allow_write());

    // Nested load: one end_load must not reopen while the outer load is active.
    store.begin_load();
    store.begin_load();
    store.end_load();
    assert!(store.is_loading(), "nested load still closed");
    store
        .merge_adapter_prefs(Some("WIPED"), Some(1), Some(1))
        .unwrap();
    assert_eq!(store.load_prefs().adapter_id, "CMNSI7");
    store.end_load();

    store
        .merge_adapter_prefs(Some("OTHER"), Some(3), Some(1))
        .unwrap();
    let after = store.load_prefs();
    assert_eq!(
        (
            after.adapter_id.as_str(),
            after.shell_page,
            after.inner_page
        ),
        ("OTHER", 3, 1)
    );

    let mut real = store.load_app();
    real.customer = "Beta Co".into();
    store.save_app_to(&real, &settings_path).unwrap();
    assert_eq!(store.load_app().customer, "Beta Co");
    assert!(
        bak_path(&settings_path).exists(),
        "settings write keeps .bak"
    );

    assert_eq!(store.save_job_customer("Gamma LLC").unwrap(), "Gamma LLC");
    assert_eq!(store.load_app().customer, "Gamma LLC");
    fs::remove_file(bak_path(&settings_path)).unwrap();
    assert_eq!(store.save_job_customer(" Gamma LLC ").unwrap(), "Gamma LLC");
    assert!(
        !bak_path(&settings_path).exists(),
        "same customer does not rewrite settings.json"
    );
    store.save_job_customer("Gamma Two").unwrap();
    assert_eq!(store.load_app().customer, "Gamma Two");
    assert!(
        bak_path(&settings_path).exists(),
        "changed customer write keeps .bak"
    );
    store.save_job_customer("Gamma LLC").unwrap();
    assert_eq!(store.save_job_work_order(Some("99-01")).unwrap(), "99-01");
    assert_eq!(store.load_app().last_wo, "99-01");
    store.clear_job_strip().unwrap();
    assert_eq!(store.load_app().customer, "");
    assert_eq!(store.load_app().last_wo, "");
    store
        .merge_adapter_prefs(Some("CMNSI7"), Some(2), Some(4))
        .unwrap();
    let via = store.load_prefs();
    assert_eq!((via.adapter_id.as_str(), via.shell_page), ("CMNSI7", 2));
    assert!(
        bak_path(&prefs_path).exists(),
        "adapter-prefs write keeps .bak"
    );

    store.begin_load();
    assert_eq!(
        store.save_job_customer("DuringLoad").unwrap(),
        "",
        "job write refused during load"
    );
    assert_eq!(store.load_app().customer, "");
    store.end_load();
    assert!(!store.is_loading());

    // No temp files left behind.
    let leftovers: Vec<_> = fs::read_dir(&dir.0)
        .unwrap()
        .filter_map(Result::ok)
        .filter(|e| e.file_name().to_string_lossy().contains(".tmp-"))
        .collect();
    assert!(leftovers.is_empty(), "{leftovers:?}");
}

#[test]
fn adapter_prefs() {
    let dir = TempDir::new("adapter-prefs");
    let store = SettingsStore::new(&dir.0);
    assert_eq!(tb_core::settings::store::clamp_shell(-3), 0);
    assert_eq!(tb_core::settings::store::clamp_inner(99), 6);
    assert_eq!(store.load_prefs().adapter_id, "");

    store
        .save_prefs(&AdapterPrefsData {
            adapter_id: "CMNSI7".into(),
            shell_page: 2,
            inner_page: 4,
        })
        .unwrap();
    assert!(store.prefs_path().ends_with("adapter-prefs.json"));
    let round = store.load_prefs();
    assert_eq!(
        (
            round.adapter_id.as_str(),
            round.shell_page,
            round.inner_page
        ),
        ("CMNSI7", 2, 4)
    );

    store
        .merge_adapter_prefs(Some("OTHER"), Some(3), Some(1))
        .unwrap();
    let merged = store.load_prefs();
    assert_eq!(
        (
            merged.adapter_id.as_str(),
            merged.shell_page,
            merged.inner_page
        ),
        ("OTHER", 3, 1)
    );

    store
        .merge_adapter_prefs(Some(""), Some(1), Some(2))
        .unwrap();
    let no_adapter = store.load_prefs();
    assert_eq!(
        (
            no_adapter.adapter_id.as_str(),
            no_adapter.shell_page,
            no_adapter.inner_page
        ),
        ("OTHER", 1, 2)
    );

    assert!(AdapterPrefsData::saved_adapter_matches("CMNSI7", "CMNSI7"));
    assert!(AdapterPrefsData::saved_adapter_matches("cmnsi7", "CMNSI7"));
    assert!(!AdapterPrefsData::saved_adapter_matches("", "CMNSI7"));

    fs::write(store.prefs_path(), "{not json").unwrap();
    assert_eq!(store.load_prefs().adapter_id, "");

    store
        .save_prefs(&AdapterPrefsData {
            adapter_id: "CMNSI7".into(),
            shell_page: 2,
            inner_page: 4,
        })
        .unwrap();
    let frozen = fs::read_to_string(store.prefs_path()).unwrap();
    assert_eq!(
        frozen, r#"{"AdapterId":"CMNSI7","ShellPage":2,"InnerPage":4}"#,
        "no version stamp"
    );
    assert_eq!(
        AdapterPrefsData::load_from(&store.prefs_path()).inner_page,
        4
    );
}

#[test]
fn reads_the_csharp_settings_file_shape() {
    let dir = TempDir::new("csharp-shape");
    let path = dir.0.join("settings.json");
    // What JavaScriptSerializer writes for AppSettings.
    fs::write(
        &path,
        r#"{"Model":"HP450","Serial":"S-99","Customer":"Acme \u0026 Sons","LastWo":"44551-01","X":10,"Y":-5,"Width":1180,"Height":720,"Maximized":true,"ManifestUrl":""}"#,
    )
    .unwrap();
    let s = AppSettings::load_from(&path);
    assert_eq!(s.customer, "Acme & Sons");
    assert_eq!(
        (s.x, s.y, s.width, s.height, s.maximized),
        (10, -5, 1180, 720, true)
    );
    assert_eq!(
        s.to_json(),
        r#"{"Model":"HP450","Serial":"S-99","Customer":"Acme \u0026 Sons","LastWo":"44551-01","X":10,"Y":-5,"Width":1180,"Height":720,"Maximized":true,"ManifestUrl":""}"#
    );
}
