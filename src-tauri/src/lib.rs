//! Tauri 2 shell for Tech Bench. Commands expose tb-core to the React UI in app/.
//! Everything here is read-only: no bus access, no ECU actions (those stay in the WinForms
//! build until the RP1210 sidecar lands with its guards).

use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Mutex;
use std::time::Duration;

use serde::Serialize;
use tauri::{AppHandle, Manager, State};
use tb_core::kb::{KbIndex, MAX_RESULTS};
use tb_core::search::{codebook, featurebook};

/// Same file the WinForms build embeds as `TechBench.assets.cheatsheet.rtf`.
const CHEATSHEET_RTF: &str = include_str!("../../assets/cheatsheet.rtf");

/// How long `--smoke` waits for the UI to report before failing.
const SMOKE_TIMEOUT: Duration = Duration::from_secs(90);

struct Kb(Mutex<KbIndex>);
struct Smoke(AtomicBool);

#[derive(Serialize)]
struct HitDto {
    kind: String,
    title: String,
    subtitle: String,
    body: String,
    path: String,
}

#[derive(Serialize)]
struct SearchResult {
    hits: Vec<HitDto>,
    total: usize,
    status: String,
}

#[derive(Serialize)]
struct KbInfo {
    root: String,
    status: String,
    count: usize,
    errors: Vec<String>,
}

#[tauri::command]
fn kb_info(kb: State<'_, Kb>) -> KbInfo {
    let kb = kb.0.lock().unwrap_or_else(|e| e.into_inner());
    KbInfo {
        root: kb.root.clone(),
        status: kb.status.clone(),
        count: kb.all.len(),
        errors: kb.errors.clone(),
    }
}

#[tauri::command]
fn kb_search(query: String, kind: String, kb: State<'_, Kb>) -> SearchResult {
    let kb = kb.0.lock().unwrap_or_else(|e| e.into_inner());
    let (hits, total) = kb.search(&query, &kind, MAX_RESULTS);
    SearchResult {
        hits: hits
            .into_iter()
            .map(|h| HitDto {
                kind: h.kind,
                title: h.title,
                subtitle: h.subtitle,
                body: h.body,
                path: h.path,
            })
            .collect(),
        total,
        status: kb.status.clone(),
    }
}

#[derive(Serialize)]
struct ParaDto {
    text: String,
    bold: bool,
    big: bool,
}

#[tauri::command]
fn cheatsheet() -> Vec<ParaDto> {
    tb_core::rtf::paragraphs(CHEATSHEET_RTF)
        .into_iter()
        .map(|p| ParaDto {
            text: p.text,
            bold: p.bold,
            big: p.big,
        })
        .collect()
}

#[derive(Serialize)]
struct Explained {
    text: String,
    kb: Option<String>,
}

/// CodeBook explainer for one SPN/FMI plus the KB's own entry for that SPN, if any.
#[tauri::command]
fn explain_code(spn: i32, fmi: i32, kb: State<'_, Kb>) -> Explained {
    let kb = kb.0.lock().unwrap_or_else(|e| e.into_inner());
    Explained {
        text: codebook::explain(spn, fmi, &kb.names),
        kb: kb.spn_text(spn, fmi),
    }
}

#[derive(Serialize)]
struct SwitchRowDto {
    id: String,
    name: String,
    state: String,
    short: String,
    detail: String,
}

/// The INLINE 7 Guidanz feature reference (status text only; nothing is written to an ECU).
#[tauri::command]
fn guidanz_features() -> Vec<SwitchRowDto> {
    featurebook::guidanz_features()
        .into_iter()
        .map(|r| SwitchRowDto {
            id: r.id,
            name: r.name,
            state: r.state,
            short: r.short,
            detail: r.detail,
        })
        .collect()
}

#[tauri::command]
fn smoke_mode(smoke: State<'_, Smoke>) -> bool {
    smoke.0.load(Ordering::SeqCst)
}

#[tauri::command]
fn smoke_report(ok: bool, detail: String, smoke: State<'_, Smoke>, app: AppHandle) {
    if !smoke.0.load(Ordering::SeqCst) {
        return;
    }
    smoke_line(&format!(
        "SMOKE {}: {detail}",
        if ok { "PASS" } else { "FAIL" }
    ));
    app.exit(if ok { 0 } else { 1 });
}

/// Prints a smoke line, and appends it to TECHBENCH_SMOKE_OUT when set: Windows release
/// builds have no console, so CI reads the file instead.
fn smoke_line(line: &str) {
    println!("{line}");
    if let Some(path) = std::env::var_os("TECHBENCH_SMOKE_OUT") {
        use std::io::Write;
        if let Ok(mut f) = std::fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(path)
        {
            let _ = writeln!(f, "{line}");
        }
    }
}

fn has_kb(root: &Path) -> bool {
    root.join("data").join("kb.json").is_file()
}

/// Same order as the WinForms app: TECHBENCH_KB overrides, then the copy shipped with the app.
/// Debug builds fall back to the repo's kb/ so `tauri dev` works from a checkout.
fn kb_root(app: &AppHandle) -> PathBuf {
    if let Some(p) = std::env::var_os("TECHBENCH_KB").map(PathBuf::from) {
        if has_kb(&p) {
            return p;
        }
    }
    let bundled = app
        .path()
        .resource_dir()
        .map(|d| d.join("kb"))
        .unwrap_or_default();
    if has_kb(&bundled) || !cfg!(debug_assertions) {
        return bundled;
    }
    Path::new(env!("CARGO_MANIFEST_DIR")).join("..").join("kb")
}

fn install_panic_hook() {
    let default = std::panic::take_hook();
    std::panic::set_hook(Box::new(move |info| {
        let log = std::env::temp_dir().join("techbench-crash.log");
        let _ = std::fs::write(&log, format!("{info}\n"));
        eprintln!("Tech Bench crashed: {info} (written to {})", log.display());
        default(info);
    }));
}

pub fn run() {
    install_panic_hook();
    let smoke = std::env::args().any(|a| a == "--smoke");
    tauri::Builder::default()
        .manage(Kb(Mutex::new(KbIndex::new())))
        .manage(Smoke(AtomicBool::new(smoke)))
        .setup(move |app| {
            let root = kb_root(app.handle());
            {
                let kb = app.state::<Kb>();
                let mut kb = kb.0.lock().unwrap_or_else(|e| e.into_inner());
                kb.load(&root.to_string_lossy());
                if smoke {
                    smoke_line(&format!("SMOKE kb: {}", kb.status));
                }
            }
            if smoke {
                std::thread::spawn(|| {
                    std::thread::sleep(SMOKE_TIMEOUT);
                    smoke_line(&format!(
                        "SMOKE FAIL: the UI did not report within {SMOKE_TIMEOUT:?}"
                    ));
                    std::process::exit(2);
                });
            }
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            kb_info,
            kb_search,
            cheatsheet,
            explain_code,
            guidanz_features,
            smoke_mode,
            smoke_report
        ])
        .run(tauri::generate_context!())
        .expect("error while running Tech Bench");
}
