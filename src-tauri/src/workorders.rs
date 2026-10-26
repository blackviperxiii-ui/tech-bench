//! Work-order commands over tb-core's `WorkOrderStore`. Packets live in the same folder the
//! WinForms app uses on Windows (`%LOCALAPPDATA%\TechBench\work-orders`), so both apps see them.

use std::path::{Path, PathBuf};
use std::sync::Mutex;

use serde::{Deserialize, Serialize};
use tauri::{AppHandle, Manager, State};
use tauri_plugin_dialog::DialogExt;
use tb_core::settings::ShopSettings;
use tb_core::workorders::job_strip;
use tb_core::workorders::{resolve_media_path, WorkOrder, WorkOrderClock, WorkOrderStore};

use crate::Kb;

pub struct WoState(pub Mutex<Option<WorkOrderStore>>);

#[derive(Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ClockDto {
    kind: String,
    when_utc: String,
    detail: String,
}

/// A work order as the UI sees it. `label` and `key` are computed here so the UI matches the
/// WinForms list exactly.
#[derive(Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct WoDto {
    number: String,
    segment: String,
    customer: String,
    customer_no: String,
    model: String,
    serial: String,
    stock: String,
    description: String,
    assigned_tech: String,
    notes: String,
    report_text: String,
    source: String,
    clock_state: String,
    updated_utc: String,
    folder: String,
    media: Vec<String>,
    clock: Vec<ClockDto>,
    #[serde(default)]
    key: String,
    #[serde(default)]
    label: String,
    #[serde(default)]
    clock_lines: Vec<String>,
}

impl From<&WorkOrder> for WoDto {
    fn from(w: &WorkOrder) -> Self {
        WoDto {
            number: w.number.clone(),
            segment: w.segment.clone(),
            customer: w.customer.clone(),
            customer_no: w.customer_no.clone(),
            model: w.model.clone(),
            serial: w.serial.clone(),
            stock: w.stock.clone(),
            description: w.description.clone(),
            assigned_tech: w.assigned_tech.clone(),
            notes: w.notes.clone(),
            report_text: w.report_text.clone(),
            source: w.source.clone(),
            clock_state: w.clock_state.clone(),
            updated_utc: w.updated_utc.clone(),
            folder: w.folder.clone(),
            media: w.media.clone(),
            clock: w
                .clock
                .iter()
                .map(|c| ClockDto {
                    kind: c.kind.clone(),
                    when_utc: c.when_utc.clone(),
                    detail: c.detail.clone(),
                })
                .collect(),
            key: w.key(),
            label: w.list_label(),
            clock_lines: w.clock.iter().map(|c| c.to_string()).collect(),
        }
    }
}

impl From<WoDto> for WorkOrder {
    fn from(d: WoDto) -> Self {
        WorkOrder {
            number: d.number,
            segment: d.segment,
            customer: d.customer,
            customer_no: d.customer_no,
            model: d.model,
            serial: d.serial,
            stock: d.stock,
            description: d.description,
            assigned_tech: d.assigned_tech,
            notes: d.notes,
            report_text: d.report_text,
            source: d.source,
            clock_state: d.clock_state,
            updated_utc: d.updated_utc,
            folder: d.folder,
            media: d.media,
            clock: d
                .clock
                .into_iter()
                .map(|c| WorkOrderClock {
                    kind: c.kind,
                    when_utc: c.when_utc,
                    detail: c.detail,
                })
                .collect(),
        }
    }
}

/// `%LOCALAPPDATA%\TechBench` on Windows (the WinForms `ShopSettings.Folder()`); the platform's
/// local data folder elsewhere. `TECHBENCH_HOME` overrides it (tests and CI).
pub fn settings_folder(app: &AppHandle) -> PathBuf {
    if let Some(p) = std::env::var_os("TECHBENCH_HOME") {
        return PathBuf::from(p);
    }
    app.path()
        .local_data_dir()
        .unwrap_or_else(|_| std::env::temp_dir())
        .join("TechBench")
}

pub fn user_name() -> String {
    std::env::var("USERNAME")
        .or_else(|_| std::env::var("USER"))
        .unwrap_or_default()
}

pub(crate) fn exe_dir() -> PathBuf {
    std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(Path::to_path_buf))
        .unwrap_or_default()
}

/// The KB root when its shop tree is writable, else "" (no shop shard copy). The WinForms
/// installer is per-user so its KB is writable; a per-machine MSI or a Linux package is not,
/// and a save must not fail just because the shop copy cannot be written.
pub(crate) fn writable_kb(kb_root: &str) -> String {
    if kb_root.is_empty() {
        return String::new();
    }
    let shop = Path::new(kb_root).join("data").join("shop");
    let probe = shop.join(format!(".write-probe-{}", std::process::id()));
    let ok = std::fs::create_dir_all(&shop).is_ok() && std::fs::write(&probe, b"").is_ok();
    let _ = std::fs::remove_file(&probe);
    if ok {
        kb_root.to_string()
    } else {
        String::new()
    }
}

pub(crate) fn with_store<T>(
    app: &AppHandle,
    state: &State<'_, WoState>,
    kb: &State<'_, Kb>,
    f: impl FnOnce(&mut WorkOrderStore, &ShopSettings, &str) -> Result<T, String>,
) -> Result<T, String> {
    let folder = settings_folder(app);
    let shop = ShopSettings::load(&folder, &user_name());
    let kb_root = writable_kb(&kb.0.lock().unwrap_or_else(|e| e.into_inner()).root);
    let mut guard = state.0.lock().unwrap_or_else(|e| e.into_inner());
    let store = guard.get_or_insert_with(|| WorkOrderStore::new(folder, exe_dir()));
    f(store, &shop, &kb_root)
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct WoList {
    orders: Vec<WoDto>,
    conflicts: Vec<String>,
    tech_id: String,
    folder: String,
    /// Job-strip pick list: the no-work-order row, then each order's label.
    pick_rows: Vec<String>,
    sync_chip: String,
}

#[tauri::command]
pub fn wo_list(
    app: AppHandle,
    state: State<'_, WoState>,
    kb: State<'_, Kb>,
) -> Result<WoList, String> {
    with_store(&app, &state, &kb, |store, shop, kb_root| {
        let orders = store.list_all(shop, kb_root);
        Ok(WoList {
            orders: orders.iter().map(WoDto::from).collect(),
            conflicts: store.conflicts.iter().map(|c| c.to_string()).collect(),
            pick_rows: job_strip::pick_items(&orders, "").0,
            sync_chip: job_strip::sync_chip(store.conflicts.len(), &shop.tech_id),
            tech_id: shop.tech_id.clone(),
            folder: store.local_root.to_string_lossy().into_owned(),
        })
    })
}

#[tauri::command]
pub fn wo_save(
    wo: WoDto,
    app: AppHandle,
    state: State<'_, WoState>,
    kb: State<'_, Kb>,
) -> Result<WoDto, String> {
    with_store(&app, &state, &kb, |store, shop, kb_root| {
        let mut w = WorkOrder::from(wo);
        store
            .save(&mut w, shop, kb_root)
            .map_err(|e| e.to_string())?;
        Ok(WoDto::from(&w))
    })
}

/// Shop log on / log off: a shop record only, written into the packet's clock.
#[tauri::command]
pub fn wo_clock(
    wo: WoDto,
    kind: String,
    app: AppHandle,
    state: State<'_, WoState>,
    kb: State<'_, Kb>,
) -> Result<WoDto, String> {
    let on = match kind.as_str() {
        "logon" => true,
        "logoff" => false,
        _ => return Err(format!("unknown clock kind {kind}")),
    };
    with_store(&app, &state, &kb, |store, shop, kb_root| {
        let mut w = WorkOrder::from(wo);
        w.clock_state = if on { "logged-on" } else { "logged-off" }.into();
        w.add_clock(&format!("shop-{kind}"), "Shop record only.");
        store
            .save(&mut w, shop, kb_root)
            .map_err(|e| e.to_string())?;
        Ok(WoDto::from(&w))
    })
}

#[derive(Serialize)]
pub struct Shared {
    wo: WoDto,
    dest: String,
}

#[tauri::command]
pub fn wo_share(
    wo: WoDto,
    app: AppHandle,
    state: State<'_, WoState>,
    kb: State<'_, Kb>,
) -> Result<Shared, String> {
    with_store(&app, &state, &kb, |store, shop, kb_root| {
        let mut w = WorkOrder::from(wo);
        let dest = store
            .share(&mut w, shop, kb_root)
            .map_err(|e| e.to_string())?;
        Ok(Shared {
            wo: WoDto::from(&w),
            dest: dest.to_string_lossy().into_owned(),
        })
    })
}

/// Add photo: pick pictures, copy each into the packet's media folder, save.
#[tauri::command]
pub async fn wo_add_photos(
    wo: WoDto,
    app: AppHandle,
    state: State<'_, WoState>,
    kb: State<'_, Kb>,
) -> Result<Option<Attached>, String> {
    let Some(files) = app
        .dialog()
        .file()
        .set_title("Attach photo or screenshot")
        .add_filter("Pictures", &["png", "jpg", "jpeg", "bmp", "gif"])
        .add_filter("All files", &["*"])
        .blocking_pick_files()
    else {
        return Ok(None);
    };
    let paths: Vec<PathBuf> = files
        .into_iter()
        .filter_map(|f| f.into_path().ok())
        .collect();
    with_store(&app, &state, &kb, |store, shop, kb_root| {
        let mut w = WorkOrder::from(wo);
        let mut attached = 0;
        for p in &paths {
            if store
                .attach_file(&mut w, p, shop, kb_root)
                .map_err(|e| e.to_string())?
                .is_some()
            {
                attached += 1;
            }
        }
        store
            .save(&mut w, shop, kb_root)
            .map_err(|e| e.to_string())?;
        Ok(Some(Attached {
            wo: WoDto::from(&w),
            count: attached,
        }))
    })
}

#[derive(Serialize)]
pub struct Attached {
    wo: WoDto,
    count: usize,
}

/// Open one of the packet's media files, only when the name stays inside its media folder.
#[tauri::command]
pub fn wo_open_media(folder: String, name: String, app: AppHandle) -> Result<(), String> {
    let path = resolve_media_path(&folder, &name)
        .ok_or("Media name is not a file inside this work order's media folder.")?;
    crate::job::safe_open(&app, &path)
}
