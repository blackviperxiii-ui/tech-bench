//! Self-update through tauri-plugin-updater (desktop only). A build turns it on by naming the
//! update signing public key and a feed under `plugins.updater` in its Tauri config; without
//! that section the plugin is not loaded and the Settings page says updates are not set up.
//! Updates are signature-checked by the plugin. Like the WinForms updater, nothing installs
//! while the adapter bridge is running.
//!
//! `--update-smoke` (CI only): check the configured feed, then download and install what it
//! offers, writing `UPDATE …` lines through the smoke output. On Windows the installer
//! relaunches the app with the same arguments, so the new version reports in and stops.

// Phones update from the app store; only the commands' refusals are used there.
#![cfg_attr(mobile, allow(dead_code, unused_imports))]

use serde::Serialize;
use tauri::{AppHandle, Manager, Runtime, State};

use crate::livebus::Bridge;

/// Whether this build's config has an updater section (the plugin needs one to load).
pub fn configured<R: Runtime>(ctx: &tauri::Context<R>) -> bool {
    ctx.config().plugins.0.contains_key("updater")
}

/// Managed state: whether the updater plugin was loaded.
pub struct UpdaterOn(pub bool);

pub const NOT_SET_UP: &str =
    "Updates are not set up in this build. Install new versions with the shop Setup.";

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UpdateInfo {
    /// The running version.
    current: String,
    /// The offered version, or None when this is the newest.
    available: Option<String>,
    notes: String,
}

#[cfg(desktop)]
fn updater(app: &AppHandle) -> Result<tauri_plugin_updater::Updater, String> {
    use tauri_plugin_updater::UpdaterExt;
    if !app.try_state::<UpdaterOn>().is_some_and(|on| on.0) {
        return Err(NOT_SET_UP.into());
    }
    app.updater().map_err(|e| e.to_string())
}

/// Settings > Check for updates.
#[tauri::command]
pub async fn update_check(app: AppHandle) -> Result<UpdateInfo, String> {
    let current = app.package_info().version.to_string();
    #[cfg(desktop)]
    {
        let found = updater(&app)?
            .check()
            .await
            .map_err(|e| format!("Update check failed: {e}"))?;
        Ok(UpdateInfo {
            current,
            notes: found
                .as_ref()
                .and_then(|u| u.body.clone())
                .unwrap_or_default(),
            available: found.map(|u| u.version),
        })
    }
    #[cfg(mobile)]
    {
        let _ = current;
        Err("Phones update from the app store.".into())
    }
}

/// Download, verify and install the offered update, then restart. On Windows the installer
/// takes over and the app exits.
#[tauri::command]
pub async fn update_install(app: AppHandle, bridge: State<'_, Bridge>) -> Result<(), String> {
    if bridge.0.lock().unwrap_or_else(|e| e.into_inner()).is_some() {
        return Err(
            "Close the live bus session first (Disconnect on the INLINE 7 page), then update."
                .into(),
        );
    }
    #[cfg(desktop)]
    {
        install(&app).await?;
        app.restart();
    }
    #[cfg(mobile)]
    {
        let _ = app;
        Err("Phones update from the app store.".into())
    }
}

#[cfg(desktop)]
async fn install(app: &AppHandle) -> Result<String, String> {
    let update = updater(app)?
        .check()
        .await
        .map_err(|e| format!("Update check failed: {e}"))?
        .ok_or("This is the newest version.")?;
    let version = update.version.clone();
    update
        .download_and_install(|_, _| {}, || {})
        .await
        .map_err(|e| format!("Update {version} failed: {e}"))?;
    Ok(version)
}

/// `--update-smoke`: one check and install against the configured (CI-local) feed.
#[cfg(desktop)]
pub fn run_smoke(app: AppHandle) {
    tauri::async_runtime::spawn(async move {
        let current = app.package_info().version.to_string();
        crate::smoke_line(&format!("UPDATE current {current}"));
        let check = match updater(&app) {
            Ok(u) => u.check().await.map_err(|e| e.to_string()),
            Err(e) => Err(e),
        };
        match check {
            Ok(Some(u)) => crate::smoke_line(&format!("UPDATE found {}", u.version)),
            Ok(None) => {
                crate::smoke_line("UPDATE newest");
                std::process::exit(0);
            }
            Err(e) => {
                crate::smoke_line(&format!("UPDATE FAIL: {e}"));
                std::process::exit(1);
            }
        }
        // On Windows the plugin starts the installer and exits this process.
        match install(&app).await {
            Ok(v) => {
                crate::smoke_line(&format!("UPDATE installed {v}"));
                std::process::exit(0);
            }
            Err(e) => {
                crate::smoke_line(&format!("UPDATE FAIL: {e}"));
                std::process::exit(1);
            }
        }
    });
}
