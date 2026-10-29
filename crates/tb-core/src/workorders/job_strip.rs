//! What the job strip and window title show: the job tag, the sync chip and the work-order
//! pick list (`src/Features/WorkOrders/JobStrip.cs`). No UI.

use super::WorkOrder;

pub const NO_WORK_ORDER: &str = "(no work order)";
pub const SYNC_IDLE: &str = "Sync: idle";

pub fn window_title(version: &str, job_tag: &str) -> String {
    let head = format!("Tech Bench {version}");
    if job_tag.is_empty() {
        head
    } else {
        format!("{head}  ·  {job_tag}")
    }
}

/// The open work order's tag, else the typed model and serial.
pub fn job_tag(wo: Option<&WorkOrder>, model: &str, serial: &str) -> String {
    match wo {
        Some(w) if !w.number.trim().is_empty() => w.job_tag(),
        _ => format!("{model} {serial}").trim().to_string(),
    }
}

/// What "Search this job" searches for: the typed model and serial.
pub fn search_query(model: &str, serial: &str) -> String {
    format!("{model} {serial}").trim().to_string()
}

/// Customer shown at launch: settings.json first, else the work-order page's saved job.
pub fn startup_customer<'a>(saved: &'a str, saved_job_customer: &'a str) -> &'a str {
    if saved.is_empty() {
        saved_job_customer
    } else {
        saved
    }
}

pub fn sync_chip(conflicts: usize, tech: &str) -> String {
    if conflicts > 0 {
        format!("{conflicts} sync conflict(s) — click")
    } else {
        format!("Sync · {tech} · click")
    }
}

/// Pick-list rows: the no-work-order row, then each order; the selected row is the one matching
/// `keep_key` (ignoring case), else 0.
pub fn pick_items(all: &[WorkOrder], keep_key: &str) -> (Vec<String>, usize) {
    let mut items = vec![NO_WORK_ORDER.to_string()];
    let mut selected = 0;
    for (i, wo) in all.iter().enumerate() {
        items.push(wo.list_label());
        if !keep_key.is_empty() && wo.key().to_lowercase() == keep_key.to_lowercase() {
            selected = i + 1;
        }
    }
    (items, selected)
}

/// The work order under a pick-list row; `None` for the no-work-order row or a stale index.
pub fn picked(all: &[WorkOrder], row: usize) -> Option<&WorkOrder> {
    row.checked_sub(1).and_then(|i| all.get(i))
}
