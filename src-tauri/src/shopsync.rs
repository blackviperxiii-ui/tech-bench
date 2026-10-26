//! Shop sync screen (port of `src/Features/Sync/SyncForm.cs` and `ShellForm.RunSync`): this PC's
//! tech name, the optional sync folder, a run, and the conflicts the tech settles by hand. Also
//! the shop folder watcher (`ShopWatcher`), which polls instead of using a platform file watcher.

use std::path::PathBuf;
use std::sync::Mutex;
use std::time::Duration;

use serde::Serialize;
use tauri::{AppHandle, Emitter, Manager, State};
use tauri_plugin_dialog::{DialogExt, MessageDialogButtons, MessageDialogKind};
use tb_core::history::History;
use tb_core::kb::shop::SyncSettings;
use tb_core::kb::sync::{self, ShopSyncResult, NO_KB_STATUS};
use tb_core::kb::watch::{watch_dirs, Snapshot};
use tb_core::time::utc_now_o;
use tb_core::workorders::job_strip;

use crate::workorders::{exe_dir, settings_folder, user_name, with_store, writable_kb, WoState};
use crate::Kb;

/// The last sync result, so the chip and the screen agree until the next run.
pub struct LastSync(pub Mutex<Option<ShopSyncResult>>);

/// What the shop folders looked like after the last sync (or the last change the watcher
/// reported), so a sync's own copies do not set off another sync.
pub struct WatchBase(pub Mutex<Option<Snapshot>>);

/// How often the watcher looks. A change is reported once two looks in a row agree, so a burst
/// of writes settles first (the WinForms watcher waited 900 ms after the last event).
const WATCH_POLL: Duration = Duration::from_secs(2);

/// Event the UI listens for to run a quiet sync.
pub const SHOP_CHANGED: &str = "shop-changed";

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SyncView {
    tech_id: String,
    sync_folder: String,
    text: String,
    conflicts: Vec<String>,
    /// Job-strip chip once a sync has run; `None` before the first run.
    chip: Option<String>,
}

fn settings(app: &AppHandle) -> SyncSettings {
    sync::load_settings(&settings_folder(app), &user_name(), &exe_dir())
}

fn kb_root(kb: &State<'_, Kb>) -> String {
    kb.0.lock().unwrap_or_else(|e| e.into_inner()).root.clone()
}

/// `SessionIo.Folder()`: `TechBench-sessions` on the Desktop, else Documents, else local app
/// data. Under `TECHBENCH_HOME` (tests and CI) it lives there instead.
pub fn session_folder(app: &AppHandle) -> PathBuf {
    const NAME: &str = "TechBench-sessions";
    if let Some(home) = std::env::var_os("TECHBENCH_HOME") {
        return PathBuf::from(home).join(NAME);
    }
    let p = app.path();
    [p.desktop_dir(), p.document_dir(), p.local_data_dir()]
        .into_iter()
        .flatten()
        .map(|d| d.join(NAME))
        .find(|d| std::fs::create_dir_all(d).is_ok())
        .unwrap_or_else(|| std::env::temp_dir().join(NAME))
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct HistoryView {
    text: String,
    folder: String,
}

/// Unit history for a job tag (the INLINE 7 History panel).
#[tauri::command]
pub fn history_summary(job: String, app: AppHandle) -> HistoryView {
    let folder = session_folder(&app);
    HistoryView {
        text: History::load(&folder).summary(&job),
        folder: folder.to_string_lossy().into_owned(),
    }
}

fn watched(app: &AppHandle) -> Vec<PathBuf> {
    let root = kb_root(&app.state::<Kb>());
    watch_dirs(&root, Some(&settings(app)))
}

fn set_base(app: &AppHandle, snap: Snapshot) {
    *app.state::<WatchBase>()
        .0
        .lock()
        .unwrap_or_else(|e| e.into_inner()) = Some(snap);
}

/// Poll the shop folders in the background and emit [`SHOP_CHANGED`] when they settle after a
/// change. Not started in smoke mode.
pub fn start_watcher(app: AppHandle) {
    std::thread::spawn(move || {
        let mut pending: Option<Snapshot> = None;
        loop {
            std::thread::sleep(WATCH_POLL);
            let now = Snapshot::take(&watched(&app));
            let base = app
                .state::<WatchBase>()
                .0
                .lock()
                .unwrap_or_else(|e| e.into_inner())
                .clone();
            match base {
                None => set_base(&app, now),
                Some(b) if b == now => pending = None,
                Some(_) if pending.as_ref() == Some(&now) => {
                    set_base(&app, now);
                    pending = None;
                    let _ = app.emit(SHOP_CHANGED, ());
                }
                Some(_) => pending = Some(now),
            }
        }
    });
}

fn view(app: &AppHandle, kb: &State<'_, Kb>, last: &State<'_, LastSync>, extra: &str) -> SyncView {
    let s = settings(app);
    let last = last.0.lock().unwrap_or_else(|e| e.into_inner());
    let text = match (&*last, extra.is_empty()) {
        (Some(r), true) => sync::screen_text(r),
        (r, _) => sync::idle_text(&kb_root(kb), &s, r.as_ref(), extra),
    };
    SyncView {
        conflicts: last
            .iter()
            // The brand + code key separator is U+0001; show it as a space.
            .flat_map(|r| {
                r.conflicts
                    .iter()
                    .map(|c| c.to_string().replace('\u{1}', " "))
            })
            .collect(),
        chip: last
            .as_ref()
            .map(|r| job_strip::sync_chip(r.conflicts.len(), &s.tech_id)),
        tech_id: s.tech_id,
        sync_folder: s.sync_folder,
        text,
    }
}

#[tauri::command]
pub fn sync_view(app: AppHandle, kb: State<'_, Kb>, last: State<'_, LastSync>) -> SyncView {
    view(&app, &kb, &last, "")
}

/// Save settings. A blank `sync_folder` means "the KB folder is the shared tree".
#[tauri::command]
pub fn sync_save(
    tech_id: String,
    sync_folder: String,
    app: AppHandle,
    kb: State<'_, Kb>,
    last: State<'_, LastSync>,
) -> Result<SyncView, String> {
    let folder = sync_folder.trim().to_string();
    sync::save_settings(
        &settings_folder(&app),
        &SyncSettings {
            tech_id,
            sync_folder: folder.clone(),
        },
    )
    .map_err(|e| format!("Could not save sync settings. {e}"))?;
    let extra = if folder.is_empty() {
        "Using the knowledge-base folder."
    } else {
        "Settings saved."
    };
    Ok(view(&app, &kb, &last, extra))
}

fn run_once(
    app: &AppHandle,
    kb: &State<'_, Kb>,
    wo: &State<'_, WoState>,
) -> Result<ShopSyncResult, String> {
    let root = kb_root(kb);
    if root.trim().is_empty() {
        return Ok(ShopSyncResult {
            status: NO_KB_STATUS.into(),
            ..Default::default()
        });
    }
    if writable_kb(&root).is_empty() {
        return Ok(ShopSyncResult {
            status: format!(
                "Sync: the knowledge base folder is read-only here, so there is nothing to sync: {root}"
            ),
            ..Default::default()
        });
    }
    let wo_conflicts = with_store(app, wo, kb, |store, shop, kb_root| {
        store.list_all(shop, kb_root);
        Ok(store.conflicts.clone())
    })?;
    let r = sync::run(
        &root,
        &settings(app),
        &settings_folder(app),
        &utc_now_o(),
        |r| r.conflicts.extend(wo_conflicts),
    )
    .map_err(|e| format!("Sync failed: {e}"))?;
    // Other techs' saved sessions become this PC's unit history (ShellForm.RunSync).
    let _ = sync::import_history(std::path::Path::new(&root), &session_folder(app));
    kb.0.lock().unwrap_or_else(|e| e.into_inner()).load(&root);
    set_base(app, Snapshot::take(&watched(app)));
    Ok(r)
}

/// Run a sync with the saved settings, then reload the KB so pulled notes and codes show.
#[tauri::command]
pub fn sync_run(
    app: AppHandle,
    kb: State<'_, Kb>,
    wo: State<'_, WoState>,
    last: State<'_, LastSync>,
) -> Result<SyncView, String> {
    let r = run_once(&app, &kb, &wo)?;
    *last.0.lock().unwrap_or_else(|e| e.into_inner()) = Some(r);
    Ok(view(&app, &kb, &last, ""))
}

/// Settle conflict `index` of the last run after a Yes/No check, then sync again. `None` when
/// the tech says No.
#[tauri::command]
pub async fn sync_resolve(
    index: usize,
    choice: String,
    app: AppHandle,
    kb: State<'_, Kb>,
    wo: State<'_, WoState>,
    last: State<'_, LastSync>,
) -> Result<Option<SyncView>, String> {
    let conflict = last
        .0
        .lock()
        .unwrap_or_else(|e| e.into_inner())
        .as_ref()
        .and_then(|r| r.conflicts.get(index).cloned())
        .ok_or("Select a conflict first.")?;
    let verb = match choice.as_str() {
        "local" => "Keep mine",
        "remote" => "Keep theirs",
        "both" => "Keep both",
        _ => return Err(format!("Unknown choice: {choice}")),
    };
    let yes = app
        .dialog()
        .message(format!(
            "{verb} for this conflict?\n\n{}\n\nThis overwrites the other side for that file.",
            conflict.relative_path
        ))
        .title("Shop sync")
        .kind(MessageDialogKind::Info)
        .buttons(MessageDialogButtons::OkCancelCustom(
            "Yes".into(),
            "No".into(),
        ))
        .blocking_show();
    if !yes {
        return Ok(None);
    }
    let root = kb_root(&kb);
    let s = settings(&app);
    let folder = settings_folder(&app);
    with_store(&app, &wo, &kb, |store, _, _| {
        sync::resolve(&root, &conflict, &choice, &s, &folder, store).map_err(|e| e.to_string())
    })?;
    sync_run(app, kb, wo, last).map(Some)
}

#[tauri::command]
pub async fn sync_pick_folder(app: AppHandle) -> Option<String> {
    app.dialog()
        .file()
        .set_title("Folder both techs can see (USB, network, or OneDrive).")
        .blocking_pick_folder()
        .and_then(|p| p.into_path().ok())
        .map(|p| p.to_string_lossy().into_owned())
}
