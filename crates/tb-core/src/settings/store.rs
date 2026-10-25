//! Settings files and the startup write gate.
//!
//! The C# app keeps the gate depth and path overrides in statics; here one [`SettingsStore`]
//! value owns the folder and the gate, so callers and tests don't share hidden state.

use super::atomic::atomic_write_text;
use crate::json::{self, Value};
use crate::workorders::WorkOrder;
use std::fs;
use std::io;
use std::path::{Path, PathBuf};

/// Shell pages in switch-bar order: Search, Work orders, INLINE 7, Adapters.
pub const SHELL_PAGE_COUNT: i32 = 4;
pub const INNER_PAGE_COUNT: i32 = 7;

/// Tiny local settings: last job (model/serial/customer/WO key) and window size.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct AppSettings {
    pub model: String,
    pub serial: String,
    pub customer: String,
    pub last_wo: String,
    pub x: i32,
    pub y: i32,
    pub width: i32,
    pub height: i32,
    pub maximized: bool,
    pub manifest_url: String,
}

/// Last RP1210 adapter and INLINE 7 page.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct AdapterPrefsData {
    pub adapter_id: String,
    pub shell_page: i32,
    pub inner_page: i32,
}

/// Field lookup by the C# property name, exact first, then case-insensitive.
fn field<'a>(obj: &'a Value, name: &str) -> Option<&'a Value> {
    if let Some(v) = obj.get(name) {
        return Some(v);
    }
    match obj {
        Value::Object(m) => m
            .iter()
            .find(|(k, _)| k.eq_ignore_ascii_case(name))
            .map(|(_, v)| v),
        _ => None,
    }
}

/// A string field: missing or null is empty, anything but a string is a bad file.
fn str_field(obj: &Value, name: &str) -> Result<String, ()> {
    match field(obj, name) {
        None | Some(Value::Null) => Ok(String::new()),
        Some(Value::String(s)) => Ok(s.clone()),
        Some(Value::Number(n)) => Ok(n.clone()),
        Some(Value::Bool(b)) => Ok(Value::Bool(*b).text()),
        Some(_) => Err(()),
    }
}

/// An int field: missing is 0; a non-integer (e.g. `"abc"`) makes the whole file a bad file,
/// as `JavaScriptSerializer` throws on it.
fn int_field(obj: &Value, name: &str) -> Result<i32, ()> {
    match field(obj, name) {
        None => Ok(0),
        Some(Value::Number(n)) | Some(Value::String(n)) => n.trim().parse().map_err(|_| ()),
        Some(_) => Err(()),
    }
}

fn bool_field(obj: &Value, name: &str) -> Result<bool, ()> {
    match field(obj, name) {
        None => Ok(false),
        Some(Value::Bool(b)) => Ok(*b),
        Some(Value::String(s)) if s.eq_ignore_ascii_case("true") => Ok(true),
        Some(Value::String(s)) if s.eq_ignore_ascii_case("false") => Ok(false),
        Some(_) => Err(()),
    }
}

fn read_object(path: &Path) -> Option<Value> {
    let text = fs::read_to_string(path).ok()?;
    let v = json::parse(&text).ok()?;
    v.is_object().then_some(v)
}

fn s(v: &str) -> Value {
    Value::String(v.to_string())
}

fn n(v: i32) -> Value {
    Value::Number(v.to_string())
}

impl AppSettings {
    /// Missing, corrupt or wrong-shaped file gives defaults; the app still launches.
    pub fn load_from(path: &Path) -> AppSettings {
        let Some(o) = read_object(path) else {
            return AppSettings::default();
        };
        let parsed = (|| {
            Ok::<_, ()>(AppSettings {
                model: str_field(&o, "Model")?,
                serial: str_field(&o, "Serial")?,
                customer: str_field(&o, "Customer")?,
                last_wo: str_field(&o, "LastWo")?,
                x: int_field(&o, "X")?,
                y: int_field(&o, "Y")?,
                width: int_field(&o, "Width")?,
                height: int_field(&o, "Height")?,
                maximized: bool_field(&o, "Maximized")?,
                manifest_url: str_field(&o, "ManifestUrl")?,
            })
        })();
        parsed.unwrap_or_default()
    }

    /// Same field names and order as the C# serializer, so either app reads the other's file.
    pub fn to_json(&self) -> String {
        Value::Object(vec![
            ("Model".into(), s(&self.model)),
            ("Serial".into(), s(&self.serial)),
            ("Customer".into(), s(&self.customer)),
            ("LastWo".into(), s(&self.last_wo)),
            ("X".into(), n(self.x)),
            ("Y".into(), n(self.y)),
            ("Width".into(), n(self.width)),
            ("Height".into(), n(self.height)),
            ("Maximized".into(), Value::Bool(self.maximized)),
            ("ManifestUrl".into(), s(&self.manifest_url)),
        ])
        .to_json()
    }
}

pub fn clamp_shell(page: i32) -> i32 {
    page.clamp(0, SHELL_PAGE_COUNT - 1)
}

pub fn clamp_inner(page: i32) -> i32 {
    page.clamp(0, INNER_PAGE_COUNT - 1)
}

impl AdapterPrefsData {
    pub fn load_from(path: &Path) -> AdapterPrefsData {
        let Some(o) = read_object(path) else {
            return AdapterPrefsData::default();
        };
        let parsed = (|| {
            Ok::<_, ()>(AdapterPrefsData {
                adapter_id: str_field(&o, "AdapterId")?,
                shell_page: clamp_shell(int_field(&o, "ShellPage")?),
                inner_page: clamp_inner(int_field(&o, "InnerPage")?),
            })
        })();
        parsed.unwrap_or_default()
    }

    pub fn to_json(&self) -> String {
        Value::Object(vec![
            ("AdapterId".into(), s(&self.adapter_id)),
            ("ShellPage".into(), n(clamp_shell(self.shell_page))),
            ("InnerPage".into(), n(clamp_inner(self.inner_page))),
        ])
        .to_json()
    }

    /// True when `adapter_id` matches a non-empty saved id, ignoring case.
    pub fn saved_adapter_matches(saved: &str, adapter_id: &str) -> bool {
        !saved.is_empty() && saved.to_uppercase() == adapter_id.to_uppercase()
    }
}

/// Match a saved work-order key against a loaded list; empty if absent.
pub fn match_work_order_key(orders: &[WorkOrder], key: &str) -> String {
    let want = key.trim().to_uppercase();
    if want.is_empty() {
        return String::new();
    }
    orders
        .iter()
        .map(WorkOrder::key)
        .find(|k| k.to_uppercase() == want)
        .unwrap_or_default()
}

/// Front door for `settings.json` and `adapter-prefs.json`. While a load is in progress
/// (`begin_load` .. `end_load`, nestable) every write is refused, so controls filling at
/// startup can't overwrite saved state with blanks.
#[derive(Debug)]
pub struct SettingsStore {
    folder: PathBuf,
    load_depth: u32,
}

impl SettingsStore {
    /// Settings live directly in `folder` (the app data folder, or a temp folder in tests).
    pub fn new(folder: impl Into<PathBuf>) -> Self {
        Self {
            folder: folder.into(),
            load_depth: 0,
        }
    }

    pub fn settings_path(&self) -> PathBuf {
        self.folder.join("settings.json")
    }

    pub fn prefs_path(&self) -> PathBuf {
        self.folder.join("adapter-prefs.json")
    }

    pub fn begin_load(&mut self) {
        self.load_depth += 1;
    }

    pub fn end_load(&mut self) {
        self.load_depth = self.load_depth.saturating_sub(1);
    }

    pub fn is_loading(&self) -> bool {
        self.load_depth > 0
    }

    pub fn allow_write(&self) -> bool {
        self.load_depth == 0
    }

    /// Atomic write that is a no-op while loading.
    pub fn write_text(&self, path: &Path, text: &str) -> io::Result<()> {
        if !self.allow_write() || path.as_os_str().is_empty() {
            return Ok(());
        }
        atomic_write_text(path, text)
    }

    pub fn load_app(&self) -> AppSettings {
        AppSettings::load_from(&self.settings_path())
    }

    pub fn save_app_to(&self, settings: &AppSettings, path: &Path) -> io::Result<()> {
        self.write_text(path, &settings.to_json())
    }

    /// Save, keeping the on-disk customer and last WO when this copy has them blank
    /// (the C# `AppSettings.Save` merge).
    pub fn save_app(&self, settings: &mut AppSettings) -> io::Result<()> {
        let path = self.settings_path();
        if path.exists() {
            let on_disk = AppSettings::load_from(&path);
            if settings.customer.is_empty() && !on_disk.customer.is_empty() {
                settings.customer = on_disk.customer;
            }
            if settings.last_wo.is_empty() && !on_disk.last_wo.is_empty() {
                settings.last_wo = on_disk.last_wo;
            }
        }
        self.save_app_to(settings, &path)
    }

    /// Job-strip customer field. Returns what is saved; an unchanged customer is not rewritten.
    pub fn save_job_customer(&self, customer: &str) -> io::Result<String> {
        let mut disk = self.load_app();
        if !self.allow_write() {
            return Ok(disk.customer);
        }
        let next = customer.trim().to_string();
        // The job strip pushes the header on every keystroke in Model and Serial too; only a
        // changed customer is worth a temp, replace and .bak cycle.
        if disk.customer == next {
            return Ok(next);
        }
        disk.customer = next;
        self.save_app_to(&disk, &self.settings_path())?;
        Ok(disk.customer)
    }

    /// Job-strip last work-order key. `None` saves empty.
    pub fn save_job_work_order(&self, last_wo_key: Option<&str>) -> io::Result<String> {
        let mut disk = self.load_app();
        if !self.allow_write() {
            return Ok(disk.last_wo);
        }
        disk.last_wo = last_wo_key.map(str::trim).unwrap_or("").to_string();
        self.save_app_to(&disk, &self.settings_path())?;
        Ok(disk.last_wo)
    }

    /// Explicit wipe of the saved job customer and last WO.
    pub fn clear_job_strip(&self) -> io::Result<()> {
        if !self.allow_write() {
            return Ok(());
        }
        let mut disk = self.load_app();
        disk.customer.clear();
        disk.last_wo.clear();
        self.save_app_to(&disk, &self.settings_path())
    }

    pub fn load_prefs(&self) -> AdapterPrefsData {
        AdapterPrefsData::load_from(&self.prefs_path())
    }

    pub fn save_prefs(&self, data: &AdapterPrefsData) -> io::Result<()> {
        self.write_text(&self.prefs_path(), &data.to_json())
    }

    /// Merge the given fields into the saved prefs. An empty adapter id leaves the saved one
    /// alone: a page switch on a PC with no RP1210 adapter must not wipe the id the INLINE 7
    /// page restores against when the adapter is back.
    pub fn merge_adapter_prefs(
        &self,
        adapter_id: Option<&str>,
        shell_page: Option<i32>,
        inner_page: Option<i32>,
    ) -> io::Result<()> {
        let mut d = self.load_prefs();
        if let Some(id) = adapter_id.filter(|id| !id.is_empty()) {
            d.adapter_id = id.to_string();
        }
        if let Some(p) = shell_page {
            d.shell_page = clamp_shell(p);
        }
        if let Some(p) = inner_page {
            d.inner_page = clamp_inner(p);
        }
        self.save_prefs(&d)
    }
}
