//! Add code / Add note / Add file: writes into this tech's shop shard under the KB
//! (`data/shop/<tech>/`), then reloads the index so the new entry is searchable at once.

use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};
use tauri::{AppHandle, State};
use tauri_plugin_dialog::DialogExt;
use tb_core::kb::shop::{self, SyncSettings};
use tb_core::kb::user_codes::{self, UserCode, NO_KB_MESSAGE};

use crate::workorders::{settings_folder, user_name, writable_kb};
use crate::Kb;

#[derive(Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct CodeDto {
    brand: String,
    code: String,
    title: String,
    severity: String,
    controller: String,
    description: String,
    /// One entry per line, as the form shows them.
    causes: String,
    checks: String,
    reset: String,
    safety: String,
}

impl From<UserCode> for CodeDto {
    fn from(c: UserCode) -> Self {
        CodeDto {
            brand: c.brand,
            code: c.code,
            title: c.title,
            severity: c.severity,
            controller: c.controller,
            description: c.description,
            causes: c.causes.join("\n"),
            checks: c.checks.join("\n"),
            reset: c.reset,
            safety: c.safety,
        }
    }
}

/// `CodeEditForm.Collect`: trimmed fields, causes and checks split into lines.
impl From<CodeDto> for UserCode {
    fn from(d: CodeDto) -> Self {
        UserCode {
            brand: d.brand.trim().into(),
            code: d.code.trim().into(),
            title: d.title.trim().into(),
            severity: d.severity.trim().into(),
            controller: d.controller.trim().into(),
            description: d.description.trim().into(),
            causes: user_codes::split_lines(&d.causes),
            checks: user_codes::split_lines(&d.checks),
            reset: d.reset.trim().into(),
            safety: d.safety.trim().into(),
            author: String::new(),
        }
    }
}

fn kb_root(kb: &State<'_, Kb>) -> String {
    kb.0.lock().unwrap_or_else(|e| e.into_inner()).root.clone()
}

/// The KB root to write into, or the tech-facing reason there is none.
fn target(kb: &State<'_, Kb>) -> Result<PathBuf, String> {
    let root = kb_root(kb);
    if root.trim().is_empty() || !Path::new(&root).join("data").is_dir() {
        return Err(NO_KB_MESSAGE.to_string());
    }
    if writable_kb(&root).is_empty() {
        return Err(format!(
            "The knowledge base folder is read-only here, so there is nowhere to save: {root}"
        ));
    }
    Ok(PathBuf::from(root))
}

fn tech(app: &AppHandle) -> String {
    SyncSettings::load(&settings_folder(app), &user_name()).tech_id
}

fn reload(kb: &State<'_, Kb>) {
    let mut kb = kb.0.lock().unwrap_or_else(|e| e.into_inner());
    let root = kb.root.clone();
    kb.load(&root);
}

/// Add code seed from the highlighted search hit (`ShopAdd.CodeSeed`).
#[tauri::command]
pub fn kb_code_seed(kind: String, title: String, kb: State<'_, Kb>) -> Result<CodeDto, String> {
    let root = kb_root(&kb);
    let saved = if kind == "CODE" && !root.is_empty() {
        shop::load_all_codes(Path::new(&root)).map_err(|e| e.to_string())?
    } else {
        Vec::new()
    };
    Ok(user_codes::code_seed(&saved, &kind, &title).into())
}

/// Save a code into this tech's `user-codes.json`. Returns what to search for.
#[tauri::command]
pub fn kb_add_code(code: CodeDto, app: AppHandle, kb: State<'_, Kb>) -> Result<String, String> {
    let entry = UserCode::from(code);
    if !entry.is_usable() {
        return Err("Give it at least a code or a title.".into());
    }
    let root = target(&kb)?;
    let query = user_codes::code_query(&entry);
    shop::save_user_code(&root, &tech(&app), entry)
        .map_err(|e| format!("Could not write your shop user-codes.json. {e}"))?;
    reload(&kb);
    Ok(query)
}

/// Save a tech note. `stamp` is the local `yyyyMMdd_HHmmss` from the UI.
#[tauri::command]
pub fn kb_add_note(
    title: String,
    body: String,
    stamp: String,
    app: AppHandle,
    kb: State<'_, Kb>,
) -> Result<String, String> {
    let title = title.trim().to_string();
    if title.is_empty() && body.trim().is_empty() {
        return Err("Give the note a title or some text.".into());
    }
    let root = target(&kb)?;
    shop::save_note(&root, &tech(&app), &title, &body, &stamp)
        .map_err(|e| format!("Could not write the note. {e}"))?;
    reload(&kb);
    Ok(user_codes::note_query(&title, &body))
}

/// Pick a file and copy it into this tech's shop files. `None` when the picker is cancelled.
#[tauri::command]
pub async fn kb_add_file(
    stamp: String,
    app: AppHandle,
    kb: State<'_, Kb>,
) -> Result<Option<String>, String> {
    let root = target(&kb)?;
    let Some(src) = app
        .dialog()
        .file()
        .set_title("Add a shop file (PDF, photo, chart…)")
        .blocking_pick_file()
        .and_then(|p| p.into_path().ok())
    else {
        return Ok(None);
    };
    let dest = shop::add_file(&root, &tech(&app), &src, &stamp)
        .map_err(|e| format!("Could not copy the file into the knowledge base. {e}"))?;
    reload(&kb);
    Ok(dest.file_name().map(|n| n.to_string_lossy().into_owned()))
}
