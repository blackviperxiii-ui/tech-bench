//! File-backed work-order packets (port of `src/Features/WorkOrders/WorkOrderStore.cs`).
//!
//! A packet is a folder named by the safe WO key holding `wo.json` (UTF-8, no BOM), `notes.txt`
//! and `report.txt` (UTF-8 with BOM, as C# `Encoding.UTF8` writes) and `media/`. Packets live in
//! the local store and are copied into the KB shop tree (`data/shop/{tech}/work-orders`) so shop
//! sync can carry them; `share` also copies into `_shared` and the shop share folder. Listing
//! merges every source by key and records disagreements as conflicts instead of guessing.

use crate::json::{self, Value};
use crate::settings::atomic::{
    atomic_copy_file, atomic_write_text, atomic_write_text_bom, read_text,
};
use crate::settings::shop::{sanitize, ShopSettings};
use crate::time::parse_utc_ticks;
use crate::workorders::model::WorkOrder;
use std::cmp::Ordering;
use std::collections::HashMap;
use std::fmt;
use std::fs;
use std::io;
use std::path::{Path, PathBuf};

pub const SHARED_TECH: &str = "_shared";
pub const WO_REL: &str = "work-orders";

/// One packet that two sources disagree on (the work-order kind of C# `ShopConflict`).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ShopConflict {
    pub kind: String,
    pub key: String,
    pub relative_path: String,
    pub local_path: String,
    pub remote_path: String,
    pub local_summary: String,
    pub remote_summary: String,
    pub prefix: String,
}

impl fmt::Display for ShopConflict {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self.kind.as_str() {
            "code" => write!(
                f,
                "CODE  {}  —  {}  vs  {}",
                self.key, self.local_summary, self.remote_summary
            ),
            "work-order" => write!(
                f,
                "WO  {}  —  {}  vs  {}",
                self.key, self.local_summary, self.remote_summary
            ),
            _ => write!(f, "{}  {}", self.kind.to_uppercase(), self.relative_path),
        }
    }
}

/// Folder-safe WO key: letters, digits, `-`, `_`; anything else becomes `-`; trimmed of `-`;
/// at most 60 characters.
pub fn safe_key(raw: &str) -> String {
    if raw.trim().is_empty() {
        return String::new();
    }
    let s: String = raw
        .trim()
        .chars()
        .map(|c| {
            if c.is_alphanumeric() || c == '-' || c == '_' {
                c
            } else {
                '-'
            }
        })
        .collect();
    s.trim_matches('-').chars().take(60).collect()
}

pub fn packet_dir(root: &Path, key: &str) -> PathBuf {
    root.join(safe_key(key))
}

fn shop_root(kb_root: &str) -> PathBuf {
    Path::new(kb_root).join("data").join("shop")
}

pub fn shop_tech_dir(kb_root: &str, tech_id: &str) -> PathBuf {
    shop_root(kb_root).join(sanitize(tech_id)).join(WO_REL)
}

pub fn shop_shared_dir(kb_root: &str) -> PathBuf {
    shop_root(kb_root).join(SHARED_TECH).join(WO_REL)
}

/// `.bak` backups and `.tmp-*` temps are never packets or media.
pub fn is_ignored_packet_file(path: &Path) -> bool {
    let name = path
        .file_name()
        .map(|n| n.to_string_lossy().to_lowercase())
        .unwrap_or_default();
    !name.is_empty() && (name.ends_with(".bak") || name.contains(".tmp-"))
}

fn eq_ci(a: &str, b: &str) -> bool {
    a.to_uppercase() == b.to_uppercase()
}

fn contains_name(names: &[String], name: &str) -> bool {
    names.iter().any(|n| eq_ci(n, name))
}

fn kb_exists(kb_root: &str) -> bool {
    !kb_root.is_empty() && Path::new(kb_root).is_dir()
}

/// Entries of `dir` that pass `keep`, sorted by name (NTFS order) so results are stable.
fn sorted_entries(dir: &Path, keep: impl Fn(&Path) -> bool) -> Vec<PathBuf> {
    let mut out: Vec<PathBuf> = fs::read_dir(dir)
        .map(|rd| {
            rd.filter_map(Result::ok)
                .map(|e| e.path())
                .filter(|p| keep(p))
                .collect()
        })
        .unwrap_or_default();
    out.sort();
    out
}

fn files_of(dir: &Path) -> Vec<PathBuf> {
    sorted_entries(dir, |p| p.is_file())
}

/// `name` in `dir`, or `stem-2.ext`, `stem-3.ext`, … when taken (`UniqueName`).
fn unique_name(dir: &Path, file_name: &str) -> String {
    let base = Path::new(file_name)
        .file_name()
        .map(|n| n.to_string_lossy().into_owned())
        .unwrap_or_default();
    let name = if base.is_empty() {
        "file.bin".to_string()
    } else {
        base
    };
    if !dir.join(&name).exists() {
        return name;
    }
    // .NET GetFileNameWithoutExtension / GetExtension: split at the last dot.
    let (stem, ext) = match name.rfind('.') {
        Some(i) => (&name[..i], &name[i..]),
        None => (name.as_str(), ""),
    };
    for i in 2..1000 {
        let n = format!("{stem}-{i}{ext}");
        if !dir.join(&n).exists() {
            return n;
        }
    }
    format!(
        "{stem}-{}{ext}",
        crate::time::utc_now_o().replace([':', '.'], "")
    )
}

/// Copy a packet's files and media onto `dest`, skipping `.bak` and `.tmp-*`.
fn copy_packet(src: &Path, dest: &Path) -> io::Result<()> {
    if src.as_os_str().is_empty() || !src.is_dir() {
        return Ok(());
    }
    fs::create_dir_all(dest.join("media"))?;
    for f in files_of(src) {
        if !is_ignored_packet_file(&f) {
            atomic_copy_file(&f, &dest.join(f.file_name().unwrap_or_default()))?;
        }
    }
    let media = src.join("media");
    if media.is_dir() {
        let dm = dest.join("media");
        for f in files_of(&media) {
            if !is_ignored_packet_file(&f) {
                atomic_copy_file(&f, &dm.join(f.file_name().unwrap_or_default()))?;
            }
        }
    }
    Ok(())
}

/// Everything that makes two packets "the same job state" (`PacketHash` input).
fn packet_text(wo: &WorkOrder) -> String {
    let mut sb = String::new();
    for part in [
        &wo.number,
        &wo.segment,
        &wo.customer,
        &wo.customer_no,
        &wo.model,
        &wo.serial,
        &wo.stock,
        &wo.description,
        &wo.assigned_tech,
        &wo.notes,
        &wo.report_text,
        &wo.clock_state,
    ] {
        sb.push_str(part);
        sb.push('\n');
    }
    let mut names = wo.media.clone();
    names.sort_by_key(|n| n.to_uppercase());
    for n in names {
        sb.push_str(&n);
        sb.push('\n');
    }
    sb
}

fn compare_utc(a: &WorkOrder, b: &WorkOrder) -> Ordering {
    let t = |w: &WorkOrder| parse_utc_ticks(&w.updated_utc).unwrap_or(i64::MIN);
    t(a).cmp(&t(b))
}

fn note_preview(wo: &WorkOrder) -> String {
    let n = wo.notes.replace(['\r', '\n'], " ");
    let n = n.trim();
    if n.is_empty() {
        "(no notes)".to_string()
    } else {
        n.chars().take(60).collect()
    }
}

fn compare_wo(a: &WorkOrder, b: &WorkOrder) -> Ordering {
    a.number
        .to_uppercase()
        .cmp(&b.number.to_uppercase())
        .then_with(|| a.segment.to_uppercase().cmp(&b.segment.to_uppercase()))
}

/// Text of a field picked by any of `names`, ignoring case (`Pick`).
fn pick(row: &[(String, Value)], names: &[&str]) -> String {
    for n in names {
        for (k, v) in row {
            if k.eq_ignore_ascii_case(n) && !matches!(v, Value::Null) {
                return v.text().trim().to_string();
            }
        }
    }
    String::new()
}

/// One assigned-WO row from any of the export shapes; `None` without a number.
pub fn import_row(row: &[(String, Value)]) -> Option<WorkOrder> {
    let wo = WorkOrder {
        source: "file".into(),
        number: pick(
            row,
            &[
                "Number",
                "WorkOrder",
                "WorkOrderNumber",
                "WO",
                "OrderNumber",
                "orderNumber",
            ],
        ),
        segment: pick(row, &["Segment", "Seg", "SegmentNumber"]),
        customer: pick(row, &["Customer", "CustomerName", "Name"]),
        customer_no: pick(row, &["CustomerNo", "CustomerNumber", "CustomerId"]),
        model: pick(row, &["Model", "Machine", "MakeModel"]),
        serial: pick(row, &["Serial", "SerialNumber", "MachineSerialNumber"]),
        stock: pick(row, &["Stock", "StockNumber"]),
        description: pick(row, &["Description", "History", "Complaint"]),
        assigned_tech: pick(row, &["AssignedTech", "Technician", "Tech"]),
        ..WorkOrder::default()
    };
    (!wo.number.trim().is_empty()).then_some(wo)
}

fn enumerate_rows<'a>(root: &'a Value, out: &mut Vec<&'a [(String, Value)]>) {
    match root {
        Value::Array(items) => {
            for item in items {
                if let Value::Object(m) = item {
                    out.push(m);
                }
            }
        }
        Value::Object(m) => {
            for key in [
                "value",
                "workOrders",
                "WorkOrders",
                "data",
                "items",
                "Assigned",
            ] {
                if let Some(v) = root.get(key) {
                    enumerate_rows(v, out);
                    return;
                }
            }
            out.push(m);
        }
        _ => {}
    }
}

/// Assigned work orders from JSON: an array of rows, an OData-style wrapper
/// (`value`, `workOrders`, `data`, `items`, `Assigned`), or one row. Bad JSON gives none.
pub fn parse_assigned_json(text: &str) -> Vec<WorkOrder> {
    if text.trim().is_empty() {
        return Vec::new();
    }
    let Ok(root) = json::parse(text) else {
        return Vec::new();
    };
    let mut rows = Vec::new();
    enumerate_rows(&root, &mut rows);
    rows.into_iter().filter_map(import_row).collect()
}

/// Simple quote-aware split; quotes toggle and are dropped (`SplitCsv`).
fn split_csv(line: &str) -> Vec<String> {
    let mut cells = Vec::new();
    let mut cur = String::new();
    let mut quoted = false;
    for c in line.chars() {
        match c {
            '"' => quoted = !quoted,
            ',' if !quoted => cells.push(std::mem::take(&mut cur)),
            _ => cur.push(c),
        }
    }
    cells.push(cur);
    cells
}

/// Assigned work orders from CSV with a header row.
pub fn parse_assigned_csv(text: &str) -> Vec<WorkOrder> {
    if text.trim().is_empty() {
        return Vec::new();
    }
    let normalized = text.replace("\r\n", "\n").replace('\r', "\n");
    let mut lines = normalized.split('\n');
    let head = split_csv(lines.next().unwrap_or_default());
    lines
        .filter(|l| !l.trim().is_empty())
        .filter_map(|l| {
            let cells = split_csv(l);
            let mut row: Vec<(String, Value)> = Vec::new();
            for (h, c) in head.iter().zip(cells) {
                let h = h.trim().to_string();
                row.retain(|(k, _)| !k.eq_ignore_ascii_case(&h));
                row.push((h, Value::String(c)));
            }
            import_row(&row)
        })
        .collect()
}

/// Packets keyed by WO key, ignoring case (the C# `OrdinalIgnoreCase` dictionary).
type ByKey = HashMap<String, WorkOrder>;

fn key_of(k: &str) -> String {
    k.to_uppercase()
}

pub struct WorkOrderStore {
    /// Local packets (`%LOCALAPPDATA%\TechBench\work-orders` in the WinForms app).
    pub local_root: PathBuf,
    /// Shop settings folder: keep-both list when there is no KB.
    pub settings_folder: PathBuf,
    /// Folder beside the app checked for `id-work-orders.json` / `.csv`.
    pub exe_dir: PathBuf,
    /// Disagreements found by the last [`WorkOrderStore::list_all`].
    pub conflicts: Vec<ShopConflict>,
}

impl WorkOrderStore {
    /// Store rooted at a shop settings folder, with packets in `<folder>/work-orders`.
    pub fn new(settings_folder: impl Into<PathBuf>, exe_dir: impl Into<PathBuf>) -> Self {
        let settings_folder = settings_folder.into();
        Self {
            local_root: settings_folder.join(WO_REL),
            settings_folder,
            exe_dir: exe_dir.into(),
            conflicts: Vec::new(),
        }
    }

    /// Write the packet locally and copy it into the KB shop tree. Stamps `updated_utc` and
    /// sets `wo.folder` to the local packet.
    pub fn save(
        &self,
        wo: &mut WorkOrder,
        settings: &ShopSettings,
        kb_root: &str,
    ) -> io::Result<()> {
        if wo.number.trim().is_empty() {
            return Err(io::Error::new(
                io::ErrorKind::InvalidInput,
                "Work order number is required.",
            ));
        }
        wo.touch();
        let key = safe_key(&wo.key());
        let local = packet_dir(&self.local_root, &key);
        write_packet(&local, wo)?;
        wo.folder = local.to_string_lossy().into_owned();
        if kb_exists(kb_root) {
            copy_packet(
                &local,
                &packet_dir(&shop_tech_dir(kb_root, &settings.tech_id), &key),
            )?;
        }
        Ok(())
    }

    /// A packet folder, or `None` when `wo.json` is missing or unreadable. Media lists the
    /// files actually in `media/`, without `.bak` or `.tmp-*`.
    pub fn load(folder: &Path) -> Option<WorkOrder> {
        if folder.as_os_str().is_empty() {
            return None;
        }
        let json_path = folder.join("wo.json");
        if !json_path.is_file() {
            return None;
        }
        let mut wo = WorkOrder::from_json(&read_text(&json_path).ok()?)?;
        wo.folder = folder.to_string_lossy().into_owned();
        let notes = folder.join("notes.txt");
        if notes.is_file() {
            wo.notes = read_text(&notes).ok()?;
        }
        let report = folder.join("report.txt");
        if report.is_file() {
            wo.report_text = read_text(&report).ok()?;
        }
        for f in files_of(&folder.join("media")) {
            if is_ignored_packet_file(&f) {
                continue;
            }
            let name = f.file_name()?.to_string_lossy().into_owned();
            if !contains_name(&wo.media, &name) {
                wo.media.push(name);
            }
        }
        Some(wo)
    }

    /// Every packet this tech can see: local, own shop shard, `_shared` in the KB and the share
    /// folder, then assigned-WO files. Sorted by number and segment.
    pub fn list_all(&mut self, settings: &ShopSettings, kb_root: &str) -> Vec<WorkOrder> {
        self.conflicts.clear();
        let mut by_key = ByKey::new();
        let local_root = self.local_root.clone();
        self.absorb(&mut by_key, &local_root, Some("file"), kb_root);
        if kb_exists(kb_root) {
            self.absorb(
                &mut by_key,
                &shop_tech_dir(kb_root, &settings.tech_id),
                Some("file"),
                kb_root,
            );
            self.absorb(&mut by_key, &shop_shared_dir(kb_root), None, kb_root);
        }
        let share = settings.share_folder.trim();
        if !share.is_empty() && Path::new(&settings.share_folder).is_dir() {
            let share = Path::new(&settings.share_folder);
            self.absorb(&mut by_key, &share.join(WO_REL), None, kb_root);
            let shared = share
                .join("data")
                .join("shop")
                .join(SHARED_TECH)
                .join(WO_REL);
            self.absorb(&mut by_key, &shared, None, kb_root);
        }
        import_assigned_file(&mut by_key, Path::new(&settings.assigned_file));
        let mut sidecars = vec![
            self.exe_dir.join("id-work-orders.json"),
            self.exe_dir.join("id-work-orders.csv"),
        ];
        if !kb_root.is_empty() {
            let data = Path::new(kb_root).join("data");
            sidecars.push(data.join("id-work-orders.json"));
            sidecars.push(data.join("id-work-orders.csv"));
        }
        for f in sidecars {
            import_assigned_file(&mut by_key, &f);
        }
        let mut list: Vec<WorkOrder> = by_key.into_values().collect();
        list.sort_by(compare_wo);
        list
    }

    /// `source` `None` marks every packet as shared (`AbsorbShared`); `Some` only fills a blank.
    fn absorb(&mut self, by_key: &mut ByKey, root: &Path, source: Option<&str>, kb_root: &str) {
        if root.as_os_str().is_empty() || !root.is_dir() {
            return;
        }
        for d in sorted_entries(root, |p| p.is_dir()) {
            let Some(mut wo) = Self::load(&d) else {
                continue;
            };
            if wo.key().trim().is_empty() {
                continue;
            }
            match source {
                None => wo.source = "shared".into(),
                Some(s) if wo.source.trim().is_empty() => wo.source = s.into(),
                Some(_) => {}
            }
            self.merge(by_key, wo, kb_root);
        }
    }

    fn merge(&mut self, by_key: &mut ByKey, incoming: WorkOrder, kb_root: &str) {
        let k = incoming.key();
        if k.trim().is_empty() {
            return;
        }
        let slot = key_of(&k);
        let Some(have) = by_key.get(&slot) else {
            by_key.insert(slot, incoming);
            return;
        };
        let newer = compare_utc(&incoming, have) == Ordering::Greater;
        if packet_text(have) == packet_text(&incoming) {
            if newer {
                by_key.insert(slot, incoming);
            }
            return;
        }
        if self
            .conflicts
            .iter()
            .any(|c| c.kind == "work-order" && eq_ci(&c.key, &k))
        {
            return;
        }
        let conflict = ShopConflict {
            kind: "work-order".into(),
            key: k.clone(),
            relative_path: Path::new(WO_REL)
                .join(safe_key(&k))
                .to_string_lossy()
                .into_owned(),
            local_path: have.folder.clone(),
            remote_path: incoming.folder.clone(),
            local_summary: note_preview(have),
            remote_summary: note_preview(&incoming),
            prefix: "wo".into(),
        };
        if newer {
            by_key.insert(slot, incoming);
        }
        if !self.keep_both(kb_root, &k) {
            self.conflicts.push(conflict);
        }
    }

    fn keep_both_path(&self, kb_root: &str) -> PathBuf {
        if kb_root.is_empty() {
            self.settings_folder.join("keep-both-wo.txt")
        } else {
            shop_root(kb_root)
                .join("_resolved")
                .join("keep-both-wo.txt")
        }
    }

    fn keep_both(&self, kb_root: &str, key: &str) -> bool {
        if key.trim().is_empty() {
            return false;
        }
        read_text(&self.keep_both_path(kb_root))
            .map(|t| t.lines().any(|l| eq_ci(l.trim(), key)))
            .unwrap_or(false)
    }

    fn mark_keep_both(&self, kb_root: &str, key: &str) -> io::Result<()> {
        if key.trim().is_empty() || self.keep_both(kb_root, key) {
            return Ok(());
        }
        let path = self.keep_both_path(kb_root);
        if let Some(dir) = path.parent() {
            fs::create_dir_all(dir)?;
        }
        let mut text = read_text(&path).unwrap_or_default();
        if !text.is_empty() && !text.ends_with('\n') {
            text.push_str("\r\n");
        }
        text.push_str(key);
        text.push_str("\r\n");
        fs::write(&path, text)
    }

    /// Settle a conflict: `local` copies ours over theirs, `remote` the reverse, `both` keeps
    /// both and stops reporting that key. Anything else does nothing.
    pub fn resolve(&self, c: &ShopConflict, choice: &str, kb_root: &str) -> io::Result<()> {
        match choice.trim().to_lowercase().as_str() {
            "local" => copy_packet(Path::new(&c.local_path), Path::new(&c.remote_path)),
            "remote" => copy_packet(Path::new(&c.remote_path), Path::new(&c.local_path)),
            "both" => self.mark_keep_both(kb_root, &c.key),
            _ => Ok(()),
        }
    }

    fn media_dir(wo: &WorkOrder) -> io::Result<PathBuf> {
        let dir = Path::new(&wo.folder).join("media");
        fs::create_dir_all(&dir)?;
        Ok(dir)
    }

    /// Copy a file into the packet's `media/` under a free name. `None` if `source` is missing.
    pub fn attach_file(
        &self,
        wo: &mut WorkOrder,
        source: &Path,
        settings: &ShopSettings,
        kb_root: &str,
    ) -> io::Result<Option<PathBuf>> {
        if !source.is_file() {
            return Ok(None);
        }
        self.save(wo, settings, kb_root)?;
        let dir = Self::media_dir(wo)?;
        let name = unique_name(
            &dir,
            &source.file_name().unwrap_or_default().to_string_lossy(),
        );
        let dest = dir.join(&name);
        fs::copy(source, &dest)?;
        if !contains_name(&wo.media, &name) {
            wo.media.push(name);
        }
        self.save(wo, settings, kb_root)?;
        Ok(Some(dest))
    }

    /// Write `text` as a media file (UTF-8 with BOM) under a free name.
    pub fn attach_text(
        &self,
        wo: &mut WorkOrder,
        file_name: &str,
        text: &str,
        settings: &ShopSettings,
        kb_root: &str,
    ) -> io::Result<PathBuf> {
        self.save(wo, settings, kb_root)?;
        let dir = Self::media_dir(wo)?;
        let name = unique_name(&dir, file_name);
        let dest = dir.join(&name);
        atomic_write_text_bom(&dest, text)?;
        if !contains_name(&wo.media, &name) {
            wo.media.push(name);
        }
        self.save(wo, settings, kb_root)?;
        Ok(dest)
    }

    /// Copy the packet to every shop share target and log a `share` clock entry. Errors when
    /// there is neither a KB nor a share folder. Returns the last target.
    pub fn share(
        &self,
        wo: &mut WorkOrder,
        settings: &ShopSettings,
        kb_root: &str,
    ) -> io::Result<PathBuf> {
        self.save(wo, settings, kb_root)?;
        let key = safe_key(&wo.key());
        let mut dests = Vec::new();
        if kb_exists(kb_root) {
            dests.push(packet_dir(&shop_shared_dir(kb_root), &key));
        }
        if !settings.share_folder.trim().is_empty() {
            let share = Path::new(&settings.share_folder);
            fs::create_dir_all(share)?;
            dests.push(packet_dir(&share.join(WO_REL), &key));
            dests.push(packet_dir(
                &share
                    .join("data")
                    .join("shop")
                    .join(SHARED_TECH)
                    .join(WO_REL),
                &key,
            ));
        }
        if dests.is_empty() {
            return Err(io::Error::other(
                "No shop share folder. Set one in Shop → Shop settings, or keep the knowledge base on a shared OneDrive tree.",
            ));
        }
        for d in &dests {
            copy_packet(Path::new(&wo.folder), d)?;
        }
        wo.source = "file".into();
        wo.add_clock("share", "Copied packet for other techs");
        self.save(wo, settings, kb_root)?;
        Ok(dests.pop().unwrap_or_default())
    }
}

fn write_packet(dir: &Path, wo: &WorkOrder) -> io::Result<()> {
    fs::create_dir_all(dir.join("media"))?;
    atomic_write_text_bom(&dir.join("notes.txt"), &wo.notes)?;
    if !wo.report_text.is_empty() {
        atomic_write_text_bom(&dir.join("report.txt"), &wo.report_text)?;
    }
    atomic_write_text(&dir.join("wo.json"), &wo.to_json())
}

fn import_assigned_file(by_key: &mut ByKey, path: &Path) {
    if path.as_os_str().is_empty() || !path.is_file() {
        return;
    }
    let Ok(text) = read_text(path) else {
        return;
    };
    let is_csv = path
        .extension()
        .is_some_and(|e| e.eq_ignore_ascii_case("csv"));
    let rows = if is_csv {
        parse_assigned_csv(&text)
    } else {
        parse_assigned_json(&text)
    };
    for mut wo in rows {
        let k = wo.key();
        if k.is_empty() || by_key.contains_key(&key_of(&k)) {
            continue;
        }
        wo.source = "file".into();
        by_key.insert(key_of(&k), wo);
    }
}
