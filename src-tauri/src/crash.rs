//! Crash screen support (`src/Features/Crash/CrashForm.cs`): the crash log both the Rust panic
//! hook and the React crash screen append to, and Open log folder. Local only; nothing is sent.

use std::path::PathBuf;

use tauri::AppHandle;
use tb_core::crash;
use tb_core::time::utc_now_o;

/// Where the crash log lives. The WinForms build writes beside its exe; this app may be
/// installed read-only, so it uses the temp folder.
pub fn log_dir() -> PathBuf {
    std::env::temp_dir()
}

pub fn install_panic_hook() {
    let default = std::panic::take_hook();
    std::panic::set_hook(Box::new(move |info| {
        let written = crash::append_log(&log_dir(), &utc_now_o(), "panic", &info.to_string());
        match written {
            Ok(p) => eprintln!("Tech Bench crashed: {info} (written to {})", p.display()),
            Err(_) => eprintln!("Tech Bench crashed: {info}"),
        }
        default(info);
    }));
}

/// What the crash screen got from a UI error: where it was logged, and the Copy details text.
#[derive(serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct CrashReport {
    log_path: String,
    clipboard: String,
}

/// The crash screen's record of a UI error (`Program.Crash`: write the log, build the text).
#[tauri::command]
pub fn crash_report(place: String, details: String) -> CrashReport {
    let log_path = crash::append_log(&log_dir(), &utc_now_o(), &place, &details)
        .map(|p| p.to_string_lossy().into_owned())
        .unwrap_or_default();
    CrashReport {
        clipboard: crash::clipboard_text(&place, &details, &log_path),
        log_path,
    }
}

/// Open log folder.
#[tauri::command]
pub fn crash_open_folder(app: AppHandle) -> Result<(), String> {
    #[cfg(desktop)]
    {
        use tauri_plugin_opener::OpenerExt;
        let dir = log_dir();
        if !dir.is_dir() {
            return Err("The crash log folder is not available.".into());
        }
        app.opener()
            .open_path(dir.to_string_lossy().into_owned(), None::<&str>)
            .map_err(|e| format!("Could not open the log folder: {e}"))
    }
    #[cfg(mobile)]
    {
        let _ = app;
        Err("The crash log folder is not available.".into())
    }
}
