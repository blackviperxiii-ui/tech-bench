//! One WO-keyed job packet: header, notes, report, media names, shop clock
//! (port of `src/Features/WorkOrders/WorkOrder.cs` and `WorkOrderStore.ResolveMediaPath`).

use crate::kb::paths::full_path;
use crate::time::utc_now_o;
use std::path::{Path, PathBuf};

#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct WorkOrderClock {
    pub kind: String,
    pub when_utc: String,
    pub detail: String,
}

impl std::fmt::Display for WorkOrderClock {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(
            f,
            "{}  {}  [shop]  {}",
            self.when_utc, self.kind, self.detail
        )
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct WorkOrder {
    pub number: String,
    pub segment: String,
    pub customer: String,
    pub customer_no: String,
    pub model: String,
    pub serial: String,
    pub stock: String,
    pub description: String,
    pub assigned_tech: String,
    pub notes: String,
    pub report_text: String,
    pub source: String,
    pub clock_state: String,
    pub updated_utc: String,
    pub folder: String,
    pub media: Vec<String>,
    pub clock: Vec<WorkOrderClock>,
}

impl Default for WorkOrder {
    fn default() -> Self {
        Self {
            number: String::new(),
            segment: String::new(),
            customer: String::new(),
            customer_no: String::new(),
            model: String::new(),
            serial: String::new(),
            stock: String::new(),
            description: String::new(),
            assigned_tech: String::new(),
            notes: String::new(),
            report_text: String::new(),
            source: "file".to_string(),
            clock_state: "idle".to_string(),
            updated_utc: String::new(),
            folder: String::new(),
            media: Vec::new(),
            clock: Vec::new(),
        }
    }
}

impl WorkOrder {
    /// "1234-2", "1234" with no segment, "" with no number.
    pub fn key(&self) -> String {
        let n = self.number.trim();
        let seg = self.segment.trim();
        if n.is_empty() {
            String::new()
        } else if seg.is_empty() {
            n.to_string()
        } else {
            format!("{n}-{seg}")
        }
    }

    fn unit(&self) -> String {
        format!("{} {}", self.model, self.serial).trim().to_string()
    }

    pub fn job_tag(&self) -> String {
        let mut sb = String::new();
        let k = self.key();
        if !k.is_empty() {
            sb.push_str("WO ");
            sb.push_str(&k);
        }
        let unit = self.unit();
        if !unit.is_empty() {
            if !sb.is_empty() {
                sb.push_str("  ");
            }
            sb.push_str(&unit);
        }
        sb
    }

    pub fn list_label(&self) -> String {
        let k = self.key();
        let mut sb = if k.is_empty() {
            "(no number)".to_string()
        } else {
            k
        };
        if !self.customer.trim().is_empty() {
            sb.push_str("  ");
            sb.push_str(self.customer.trim());
        }
        let unit = self.unit();
        if !unit.is_empty() {
            sb.push_str("  ");
            sb.push_str(&unit);
        }
        match self.clock_state.as_str() {
            "logged-on" => sb.push_str("  [logged on]"),
            "logged-off" => sb.push_str("  [logged off]"),
            _ => {}
        }
        if self.source.eq_ignore_ascii_case("shared") {
            sb.push_str("  (shared)");
        }
        sb
    }

    pub fn touch(&mut self) {
        self.updated_utc = utc_now_o();
    }

    pub fn add_clock(&mut self, kind: &str, detail: &str) -> WorkOrderClock {
        let ev = WorkOrderClock {
            kind: kind.to_string(),
            when_utc: utc_now_o(),
            detail: detail.to_string(),
        };
        self.clock.push(ev.clone());
        self.touch();
        ev
    }
}

/// Full path of a media file in `<wo_folder>/media`, or `None` unless `media_name` is a bare
/// file name that stays inside that folder.
pub fn resolve_media_path(wo_folder: &str, media_name: &str) -> Option<PathBuf> {
    if wo_folder.trim().is_empty() || media_name.trim().is_empty() {
        return None;
    }
    let raw = media_name.trim();
    if raw.contains("..") || raw.contains('/') || raw.contains('\\') {
        return None;
    }
    let media_root = full_path(&Path::new(wo_folder).join("media"))?;
    let candidate = full_path(&media_root.join(raw))?;
    if candidate.parent() != Some(media_root.as_path()) {
        return None;
    }
    Some(candidate)
}
