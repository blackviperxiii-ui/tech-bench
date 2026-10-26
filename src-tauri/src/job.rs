//! Job strip, shop settings and the OS opener. The strip's text (tag, title) comes from
//! tb-core's `job_strip`; the saved job lives in the same settings.json the WinForms app uses.

use std::path::Path;

use serde::Serialize;
use tauri::{AppHandle, Window};
use tauri_plugin_dialog::DialogExt;
use tauri_plugin_opener::OpenerExt;
use tb_core::launch::{blocked_launch_message, is_blocked_launch_path, missing_file_message};
use tb_core::settings::{shop, SettingsStore, ShopSettings};
use tb_core::workorders::job_strip;
use tb_core::workorders::WorkOrder;

use crate::workorders::{settings_folder, user_name, WoDto};

fn store(app: &AppHandle) -> SettingsStore {
    SettingsStore::new(settings_folder(app))
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Job {
    customer: String,
    model: String,
    serial: String,
    last_wo: String,
}

#[tauri::command]
pub fn job_get(app: AppHandle) -> Job {
    let s = store(&app).load_app();
    Job {
        customer: s.customer,
        model: s.model,
        serial: s.serial,
        last_wo: s.last_wo,
    }
}

/// Typed job fields. Only a change is written (the strip calls this as the tech types).
#[tauri::command]
pub fn job_set_header(
    customer: String,
    model: String,
    serial: String,
    app: AppHandle,
) -> Result<(), String> {
    let st = store(&app);
    st.save_job_customer(&customer).map_err(|e| e.to_string())?;
    let mut disk = st.load_app();
    let (model, serial) = (model.trim().to_string(), serial.trim().to_string());
    if disk.model != model || disk.serial != serial {
        disk.model = model;
        disk.serial = serial;
        st.save_app_to(&disk, &st.settings_path())
            .map_err(|e| e.to_string())?;
    }
    Ok(())
}

#[tauri::command]
pub fn job_set_wo(key: String, app: AppHandle) -> Result<(), String> {
    let k = key.trim();
    store(&app)
        .save_job_work_order((!k.is_empty()).then_some(k))
        .map(|_| ())
        .map_err(|e| e.to_string())
}

/// Clear job: wipes the saved customer and last work order (not the typed model and serial).
#[tauri::command]
pub fn job_clear(app: AppHandle) -> Result<(), String> {
    store(&app).clear_job_strip().map_err(|e| e.to_string())
}

/// Job tag for the open work order (or the typed model and serial), also put in the window title.
#[tauri::command]
pub fn job_title(
    wo: Option<WoDto>,
    model: String,
    serial: String,
    app: AppHandle,
    window: Window,
) -> String {
    let wo = wo.map(WorkOrder::from);
    let tag = job_strip::job_tag(wo.as_ref(), &model, &serial);
    let version = app.package_info().version.to_string();
    let _ = window.set_title(&job_strip::window_title(&version, &tag));
    tag
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ShopDto {
    tech_id: String,
    share_folder: String,
    assigned_file: String,
    path: String,
}

fn shop_dto(s: ShopSettings, path: &Path) -> ShopDto {
    ShopDto {
        tech_id: s.tech_id,
        share_folder: s.share_folder,
        assigned_file: s.assigned_file,
        path: path.to_string_lossy().into_owned(),
    }
}

#[tauri::command]
pub fn shop_settings_get(app: AppHandle) -> ShopDto {
    let folder = settings_folder(&app);
    shop_dto(
        ShopSettings::load(&folder, &user_name()),
        &folder.join(shop::FILE_NAME),
    )
}

/// Shop settings form Save: sanitized tech id, trimmed paths.
#[tauri::command]
pub fn shop_settings_save(
    tech_id: String,
    share_folder: String,
    assigned_file: String,
    app: AppHandle,
) -> Result<ShopDto, String> {
    let path = settings_folder(&app).join(shop::FILE_NAME);
    let s = ShopSettings::collect(&tech_id, &share_folder, &assigned_file);
    s.save_to(&path).map_err(|e| e.to_string())?;
    Ok(shop_dto(s, &path))
}

/// Folder picker for the share folder. Async so the native dialog never blocks the main thread.
#[tauri::command]
pub async fn pick_folder(app: AppHandle) -> Option<String> {
    #[cfg(desktop)]
    {
        app.dialog()
            .file()
            .set_title("Folder other techs can read (USB, network, or OneDrive)")
            .blocking_pick_folder()
            .and_then(|p| p.into_path().ok())
            .map(|p| p.to_string_lossy().into_owned())
    }
    // Phones have no folder picker; shop folders are a desktop feature.
    #[cfg(mobile)]
    {
        let _ = app;
        None
    }
}

#[tauri::command]
pub async fn pick_assigned_file(app: AppHandle) -> Option<String> {
    app.dialog()
        .file()
        .set_title("Assigned work-order list")
        .add_filter("Work orders", &["json", "csv"])
        .blocking_pick_file()
        .and_then(|p| p.into_path().ok())
        .map(|p| p.to_string_lossy().into_owned())
}

/// Open a document with the OS default app, behind the same program/script blocklist as
/// `SafeOpen.TryOpen`. Errors are the tech-facing message.
pub fn safe_open(app: &AppHandle, path: &Path) -> Result<(), String> {
    let shown = path.to_string_lossy();
    if is_blocked_launch_path(&shown) {
        return Err(blocked_launch_message(&shown));
    }
    if !path.is_file() {
        return Err(missing_file_message(&shown));
    }
    // SafeOpen.NoAssociationMessage: the opener fails when no app takes this file type.
    let name = path
        .file_name()
        .map_or(shown.to_string(), |n| n.to_string_lossy().into_owned());
    app.opener()
        .open_path(shown.into_owned(), None::<&str>)
        .map_err(|_| {
            format!("No program is associated with this file type: {name}. Open it from Explorer and choose an app.")
        })
}

/// Open a KB file from a search hit.
#[tauri::command]
pub fn kb_open(path: String, app: AppHandle) -> Result<(), String> {
    safe_open(&app, Path::new(path.trim()))
}
