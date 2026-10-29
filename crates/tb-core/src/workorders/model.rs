//! One WO-keyed job packet: header, notes, report, media names, shop clock
//! (port of `src/Features/WorkOrders/WorkOrder.cs` and `WorkOrderStore.ResolveMediaPath`).

use crate::json::{self, Value};
use crate::kb::paths::full_path;
use crate::settings::store::{field, str_field};
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

fn sv(v: &str) -> Value {
    Value::String(v.to_string())
}

/// A string field that keeps the C# default when the key is missing (null reads as empty).
fn str_or(obj: &Value, name: &str, default: &str) -> Result<String, ()> {
    match field(obj, name) {
        None => Ok(default.to_string()),
        Some(_) => str_field(obj, name),
    }
}

impl WorkOrderClock {
    fn to_value(&self) -> Value {
        Value::Object(vec![
            ("Kind".into(), sv(&self.kind)),
            ("WhenUtc".into(), sv(&self.when_utc)),
            ("Detail".into(), sv(&self.detail)),
        ])
    }

    fn from_value(v: &Value) -> Result<Self, ()> {
        if !v.is_object() {
            return Err(());
        }
        Ok(Self {
            kind: str_field(v, "Kind")?,
            when_utc: str_field(v, "WhenUtc")?,
            detail: str_field(v, "Detail")?,
        })
    }
}

impl WorkOrder {
    /// `wo.json` text: the C# `JavaScriptSerializer` field order. `Folder` is always written
    /// empty, like the store's `Shallow` snapshot.
    pub fn to_json(&self) -> String {
        Value::Object(vec![
            ("Number".into(), sv(&self.number)),
            ("Segment".into(), sv(&self.segment)),
            ("Customer".into(), sv(&self.customer)),
            ("CustomerNo".into(), sv(&self.customer_no)),
            ("Model".into(), sv(&self.model)),
            ("Serial".into(), sv(&self.serial)),
            ("Stock".into(), sv(&self.stock)),
            ("Description".into(), sv(&self.description)),
            ("AssignedTech".into(), sv(&self.assigned_tech)),
            ("Notes".into(), sv(&self.notes)),
            ("ReportText".into(), sv(&self.report_text)),
            ("Source".into(), sv(&self.source)),
            ("ClockState".into(), sv(&self.clock_state)),
            ("UpdatedUtc".into(), sv(&self.updated_utc)),
            ("Folder".into(), sv("")),
            (
                "Media".into(),
                Value::Array(self.media.iter().map(|m| sv(m)).collect()),
            ),
            (
                "Clock".into(),
                Value::Array(self.clock.iter().map(WorkOrderClock::to_value).collect()),
            ),
        ])
        .to_json()
    }

    /// Parse `wo.json`. `None` for bad JSON or a wrong-shaped field, as the C# deserializer throws.
    pub fn from_json(text: &str) -> Option<WorkOrder> {
        let o = json::parse(text).ok()?;
        if !o.is_object() {
            return None;
        }
        let d = WorkOrder::default();
        let list = |name: &str| -> Result<Vec<Value>, ()> {
            match field(&o, name) {
                None | Some(Value::Null) => Ok(Vec::new()),
                Some(Value::Array(a)) => Ok(a.clone()),
                Some(_) => Err(()),
            }
        };
        (|| {
            Ok::<_, ()>(WorkOrder {
                number: str_or(&o, "Number", &d.number)?,
                segment: str_or(&o, "Segment", &d.segment)?,
                customer: str_or(&o, "Customer", &d.customer)?,
                customer_no: str_or(&o, "CustomerNo", &d.customer_no)?,
                model: str_or(&o, "Model", &d.model)?,
                serial: str_or(&o, "Serial", &d.serial)?,
                stock: str_or(&o, "Stock", &d.stock)?,
                description: str_or(&o, "Description", &d.description)?,
                assigned_tech: str_or(&o, "AssignedTech", &d.assigned_tech)?,
                notes: str_or(&o, "Notes", &d.notes)?,
                report_text: str_or(&o, "ReportText", &d.report_text)?,
                source: str_or(&o, "Source", &d.source)?,
                clock_state: str_or(&o, "ClockState", &d.clock_state)?,
                updated_utc: str_or(&o, "UpdatedUtc", &d.updated_utc)?,
                folder: str_or(&o, "Folder", &d.folder)?,
                media: list("Media")?
                    .iter()
                    .map(|v| match v {
                        Value::String(s) => Ok(s.clone()),
                        _ => Err(()),
                    })
                    .collect::<Result<_, _>>()?,
                clock: list("Clock")?
                    .iter()
                    .map(WorkOrderClock::from_value)
                    .collect::<Result<_, _>>()?,
            })
        })()
        .ok()
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
