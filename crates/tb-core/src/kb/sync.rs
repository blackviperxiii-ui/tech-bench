//! Two-way shop sync (port of `ShopSync.Run` / `Resolve` / `Describe` in `src/Safety/ShopSync.cs`).
//!
//! Shared KB tree (no sync folder, or the sync folder is the KB itself): each tech writes only
//! `data/shop/<tech>/`, so a cloud-synced KB never fights over one file; nothing is copied.
//!
//! Sync folder (USB stick, network share, a second cloud folder): `data/shop`, `data/notes` and
//! `data/files` are copied both ways by content hash. The hashes each side had after the last sync
//! live in `sync-state-<id>.json` in the settings folder, so a file only one side changed is
//! copied and a file both sides changed is a conflict, never a silent overwrite.

use super::shop::{
    keep_both_keys, resolved_keys, sanitize_tech_id, shard_codes_path, shard_dir, shop_root,
    tech_ids, SyncSettings,
};
use super::user_codes::{self, fingerprint, key_of, quote, UserCode};
use crate::json::{self, Value};
use crate::settings::atomic::{atomic_copy_file, atomic_write_text, read_text};
use crate::sha256::{hash_file, hash_text};
use crate::workorders::{ShopConflict, WorkOrderStore};
use std::collections::{HashMap, HashSet};
use std::fs;
use std::io;
use std::path::{Path, PathBuf};

/// Files over this size are skipped, not copied.
pub const MAX_BYTES: u64 = 50 * 1024 * 1024;

/// The trees a sync folder carries, with the state-key prefix each one uses.
const TREES: [(&str, &str); 3] = [("shop", "shop"), ("notes", "notes"), ("files", "files")];

pub const NO_KB_STATUS: &str = "No knowledge base folder — nowhere to sync.";

/// What one sync did (`ShopSyncResult`). `conflicts` holds file, note, code and work-order
/// conflicts alike.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct ShopSyncResult {
    pub pulled: usize,
    pub pushed: usize,
    pub skipped: usize,
    pub techs: usize,
    pub shared_tree: bool,
    pub status: String,
    pub conflicts: Vec<ShopConflict>,
    pub notes: Vec<String>,
}

/// `SyncSettings` with `sync-path.txt` beside the app as the fallback sync folder (first line
/// that is not blank or `#`), as `ShopSync.LoadSettings` does.
pub fn load_settings(settings_folder: &Path, user_name: &str, exe_dir: &Path) -> SyncSettings {
    let mut s = SyncSettings::load(settings_folder, user_name);
    if s.sync_folder.trim().is_empty() {
        s.sync_folder = read_text(&exe_dir.join("sync-path.txt"))
            .ok()
            .and_then(|t| {
                t.lines()
                    .map(str::trim)
                    .find(|l| !l.is_empty() && !l.starts_with('#'))
                    .map(str::to_string)
            })
            .unwrap_or_default();
    }
    s
}

/// `ShopSync.SaveSettings`: `sync.json` as `{"TechId":…,"SyncFolder":…}` with the tech id
/// sanitized. Returns what was written.
pub fn save_settings(settings_folder: &Path, s: &SyncSettings) -> io::Result<SyncSettings> {
    let saved = SyncSettings {
        tech_id: sanitize_tech_id(&s.tech_id),
        sync_folder: s.sync_folder.clone(),
    };
    let obj = Value::Object(vec![
        ("TechId".into(), Value::String(saved.tech_id.clone())),
        (
            "SyncFolder".into(),
            Value::String(saved.sync_folder.clone()),
        ),
    ]);
    atomic_write_text(&settings_folder.join("sync.json"), &obj.to_json())?;
    Ok(saved)
}

fn full_path(p: &Path) -> Option<PathBuf> {
    p.canonicalize()
        .ok()
        .or_else(|| std::path::absolute(p).ok())
}

fn norm(p: &Path) -> String {
    let s = p.to_string_lossy();
    let t = s.trim_end_matches(['\\', '/']);
    if cfg!(windows) {
        t.to_lowercase()
    } else {
        t.to_string()
    }
}

/// `ShopSync.SamePath`: both non-blank and the same folder once made absolute (ignoring case on
/// Windows and a trailing separator).
pub fn same_path(a: &str, b: &str) -> bool {
    if a.trim().is_empty() || b.trim().is_empty() {
        return false;
    }
    match (full_path(Path::new(a)), full_path(Path::new(b))) {
        (Some(x), Some(y)) => norm(&x) == norm(&y),
        _ => false,
    }
}

/// No sync folder, or the sync folder is the KB root or its `data/shop`.
pub fn is_shared_tree(kb_root: &str, s: &SyncSettings) -> bool {
    let folder = s.sync_folder.trim();
    folder.is_empty()
        || same_path(folder, kb_root)
        || same_path(folder, &shop_root(Path::new(kb_root)).to_string_lossy())
}

/// `ShopSync.Describe`: the one-line status the sync chip and Sync screen show.
pub fn describe(s: &SyncSettings, r: &ShopSyncResult) -> String {
    let place = if r.shared_tree {
        "sharing KB folder".to_string()
    } else {
        format!("folder {}", s.sync_folder)
    };
    let mut body = format!("Sync: {place}  ·  tech {}", sanitize_tech_id(&s.tech_id));
    let plural = |n: usize| if n == 1 { "" } else { "s" };
    if r.techs > 0 {
        body += &format!("  ·  {} tech folder{}", r.techs, plural(r.techs));
    }
    if r.pulled > 0 {
        body += &format!("  ·  pulled {}", r.pulled);
    }
    if r.pushed > 0 {
        body += &format!("  ·  pushed {}", r.pushed);
    }
    let n = r.conflicts.len();
    if n > 0 {
        body += &format!("  ·  {n} conflict{} — Shop → Sync", plural(n));
    } else if r.shared_tree && r.pulled == 0 && r.pushed == 0 {
        body += "  ·  notes and files in data\\shop stay in this tree";
    }
    if r.skipped > 0 {
        body += &format!("  ·  skipped {}", r.skipped);
    }
    if let Some(first) = r.notes.first() {
        body += &format!("  ·  {first}");
    }
    body
}

/// What the Sync screen shows after a run (`SyncForm.ShowResult`): the status, every note, then
/// how to settle the first conflict.
pub fn screen_text(r: &ShopSyncResult) -> String {
    let mut lines = vec![r.status.clone()];
    lines.extend(r.notes.iter().cloned());
    if let Some(first) = r.conflicts.first() {
        lines.push(String::new());
        lines.push("Select a conflict, then Keep mine / Keep theirs / Keep both.".into());
        if first.kind == "code" {
            lines.push("This code exists in two tech folders with different text.".into());
        }
    }
    lines.join("\n")
}

/// The status before or between runs (`SyncForm.RefreshStatus`): the last result (or an empty
/// one) with the tech folders counted now, then `extra` on its own line.
pub fn idle_text(
    kb_root: &str,
    s: &SyncSettings,
    last: Option<&ShopSyncResult>,
    extra: &str,
) -> String {
    let mut r = last.cloned().unwrap_or_else(|| ShopSyncResult {
        shared_tree: is_shared_tree(kb_root, s),
        ..Default::default()
    });
    r.techs = tech_ids(Path::new(kb_root)).len();
    let mut line = describe(s, &r);
    if !extra.is_empty() {
        line.push('\n');
        line.push_str(extra);
    }
    line
}

/// Run one sync. `more` adds the work-order conflicts (the caller owns the work-order store)
/// before the status line is written. An error means this tech's shard folder could not be made.
pub fn run(
    kb_root: &str,
    settings: &SyncSettings,
    settings_folder: &Path,
    now_utc: &str,
    more: impl FnOnce(&mut ShopSyncResult),
) -> io::Result<ShopSyncResult> {
    let mut result = ShopSyncResult::default();
    if kb_root.trim().is_empty() {
        result.status = NO_KB_STATUS.to_string();
        return Ok(result);
    }
    let kb = Path::new(kb_root);
    let mut settings = settings.clone();
    settings.tech_id = sanitize_tech_id(&settings.tech_id);
    fs::create_dir_all(shard_dir(kb, &settings.tech_id))?;
    import_legacy_codes(kb, &settings.tech_id, &mut result);

    result.shared_tree = is_shared_tree(kb_root, &settings);
    if !result.shared_tree {
        let remote = settings.sync_folder.trim().to_string();
        if let Err(e) = sync_folder(kb, &remote, settings_folder, now_utc, &mut result) {
            result.notes.push(format!("Sync folder: {e}"));
        }
    }

    collect_code_conflicts(kb, &mut result);
    more(&mut result);
    result.techs = tech_ids(kb).len();
    result.status = describe(&settings, &result);
    Ok(result)
}

fn sync_folder(
    kb: &Path,
    remote: &str,
    settings_folder: &Path,
    now_utc: &str,
    result: &mut ShopSyncResult,
) -> io::Result<()> {
    fs::create_dir_all(remote)?;
    let kb_root = kb.to_string_lossy();
    let mut state = SyncState::load(&state_path(settings_folder, &kb_root, remote));
    for (dir, prefix) in TREES {
        two_way(
            &kb.join("data").join(dir),
            &Path::new(remote).join("data").join(dir),
            prefix,
            &mut state,
            result,
        )?;
    }
    state.last_sync_utc = now_utc.to_string();
    state.save(&state_path(settings_folder, &kb_root, remote))
}

/// `sync-state-<first 12 hex of SHA-256(kbRoot + "\n" + syncFolder)>.json`, so one PC can sync
/// several KB / folder pairs without mixing their history.
pub fn state_path(settings_folder: &Path, kb_root: &str, sync_folder: &str) -> PathBuf {
    let id = hash_text(&format!("{kb_root}\n{sync_folder}"));
    settings_folder.join(format!("sync-state-{}.json", &id[..12]))
}

fn import_legacy_codes(kb: &Path, tech_id: &str, result: &mut ShopSyncResult) {
    let shard = shard_codes_path(kb, tech_id);
    let legacy = user_codes::path_for(kb);
    if !legacy.is_file() || shard.exists() {
        return;
    }
    match user_codes::load_from(&legacy) {
        Ok(codes) if codes.is_empty() => {}
        Ok(codes) => match user_codes::save_to(&shard, &codes) {
            Ok(()) => result
                .notes
                .push("Copied data\\user-codes.json into your shop folder.".into()),
            Err(e) => result.notes.push(format!("Legacy codes: {e}")),
        },
        Err(e) => result.notes.push(format!("Legacy codes: {e}")),
    }
}

/// Codes with the same brand + code but different text in two tech shards, unless already
/// resolved or marked keep-both.
pub fn collect_code_conflicts(kb: &Path, result: &mut ShopSyncResult) {
    let keep_both = keep_both_keys(kb);
    let resolved = resolved_keys(kb);
    let mut order: Vec<String> = Vec::new();
    let mut by_key: HashMap<String, Vec<UserCode>> = HashMap::new();
    for tech in tech_ids(kb) {
        let path = shard_codes_path(kb, &tech);
        let codes = match user_codes::load_from(&path) {
            Ok(c) => c,
            Err(e) => {
                result.notes.push(format!("{}: {e}", path.display()));
                continue;
            }
        };
        for mut c in codes {
            c.author = tech.clone();
            let k = key_of(&c);
            by_key
                .entry(k.clone())
                .or_insert_with(|| {
                    order.push(k);
                    Vec::new()
                })
                .push(c);
        }
    }
    for k in order {
        if keep_both.contains(&k) || resolved.contains(&k) {
            continue;
        }
        let list = &by_key[&k];
        let first = &list[0];
        let fp0 = fingerprint(first);
        let Some(other) = list.iter().skip(1).find(|c| fingerprint(c) != fp0) else {
            continue;
        };
        result.conflicts.push(ShopConflict {
            kind: "code".into(),
            key: k.clone(),
            relative_path: "user-codes.json".into(),
            local_path: path_text(&shard_codes_path(kb, &first.author)),
            remote_path: path_text(&shard_codes_path(kb, &other.author)),
            local_summary: format!("{}: {}", first.author, first.title),
            remote_summary: format!("{}: {}", other.author, other.title),
            prefix: "shop".into(),
        });
    }
}

fn path_text(p: &Path) -> String {
    p.to_string_lossy().into_owned()
}

/// Settle one conflict: `local` copies this PC's file over the sync folder's, `remote` the
/// reverse, `both` keeps theirs beside ours as `<name> (from other tech).<ext>` and then sends
/// ours to the sync folder. Code
/// conflicts go to `_resolved/user-codes.json` or `_resolved/keep-both.txt`; work-order
/// conflicts go to the work-order store. Anything else does nothing.
pub fn resolve(
    kb_root: &str,
    c: &ShopConflict,
    choice: &str,
    settings: &SyncSettings,
    settings_folder: &Path,
    work_orders: &WorkOrderStore,
) -> io::Result<()> {
    let choice = choice.trim().to_lowercase();
    let kb = Path::new(kb_root);
    match c.kind.as_str() {
        "code" => return resolve_code(kb, c, &choice),
        "work-order" => return work_orders.resolve(c, &choice, kb_root),
        _ => {}
    }
    let local = Path::new(&c.local_path);
    let remote = Path::new(&c.remote_path);
    match choice.as_str() {
        "local" if local.is_file() => atomic_copy_file(local, remote)?,
        "remote" if remote.is_file() => atomic_copy_file(remote, local)?,
        // The WinForms build only copied theirs aside, so the same conflict came back on every
        // sync. Here theirs is kept beside ours and ours goes to the sync folder, which settles it.
        "both" => {
            keep_both_files(c)?;
            if local.is_file() {
                atomic_copy_file(local, remote)?;
            }
        }
        "local" | "remote" => {}
        _ => return Ok(()),
    }
    let folder = settings.sync_folder.trim();
    if is_shared_tree(kb_root, settings) {
        return Ok(());
    }
    let path = state_path(settings_folder, kb_root, folder);
    let mut state = SyncState::load(&path);
    let prefix = if c.prefix.is_empty() {
        "shop"
    } else {
        &c.prefix
    };
    let key = format!("{prefix}/{}", rel_key(&c.relative_path));
    let h = if local.is_file() {
        hash_file(local)?
    } else {
        String::new()
    };
    let rh = if remote.is_file() {
        hash_file(remote)?
    } else {
        h.clone()
    };
    match choice.as_str() {
        "local" | "both" => state.set(&key, &h, &h),
        "remote" => state.set(&key, &rh, &rh),
        _ => {}
    }
    state.save(&path)
}

fn resolve_code(kb: &Path, c: &ShopConflict, choice: &str) -> io::Result<()> {
    let dir = shop_root(kb).join("_resolved");
    fs::create_dir_all(&dir)?;
    if choice == "both" {
        let path = dir.join("keep-both.txt");
        let mut lines: Vec<String> = read_text(&path)
            .map(|t| t.lines().map(str::to_string).collect())
            .unwrap_or_default();
        if !lines.iter().any(|l| l.trim().eq_ignore_ascii_case(&c.key)) {
            lines.push(c.key.clone());
        }
        let text: String = lines.iter().map(|l| format!("{l}\r\n")).collect();
        return fs::write(path, text);
    }
    let from = match choice {
        "local" => &c.local_path,
        "remote" => &c.remote_path,
        _ => return Ok(()),
    };
    let Some(pick) = user_codes::load_from(Path::new(from))?
        .into_iter()
        .find(|x| key_of(x) == c.key)
    else {
        return Ok(());
    };
    let resolved = dir.join("user-codes.json");
    let mut list = user_codes::load_from(&resolved)?;
    user_codes::upsert(&mut list, pick);
    user_codes::save_to(&resolved, &list)
}

fn keep_both_files(c: &ShopConflict) -> io::Result<()> {
    let remote = Path::new(&c.remote_path);
    if !remote.is_file() || c.local_path.is_empty() {
        return Ok(());
    }
    let local = Path::new(&c.local_path);
    let dir = local.parent().unwrap_or(Path::new(""));
    fs::create_dir_all(dir)?;
    let stem = local.file_stem().unwrap_or_default().to_string_lossy();
    let ext = local
        .extension()
        .map(|e| format!(".{}", e.to_string_lossy()))
        .unwrap_or_default();
    let mut extra = dir.join(format!("{stem} (from other tech){ext}"));
    let mut n = 2;
    while extra.exists() {
        extra = dir.join(format!("{stem} (from other tech {n}){ext}"));
        n += 1;
    }
    atomic_copy_file(remote, &extra)
}

/// What kind of conflict a path under a synced tree is.
pub fn kind_of(rel: &str) -> &'static str {
    let low = rel_key(rel).to_lowercase();
    if low.contains("\\notes\\") || low.starts_with("notes\\") {
        "note"
    } else if low.ends_with("user-codes.json") {
        "code-file"
    } else if low.ends_with("j1939-names.json") {
        "names"
    } else {
        "file"
    }
}

/// Temp, backup, Office lock and `desktop.ini` files, and anything under `_conflicts`, are never
/// synced. `rel` is relative to the tree root.
pub fn is_ignored(rel: &str) -> bool {
    let key = rel_key(rel);
    let name = key.rsplit('\\').next().unwrap_or("").to_lowercase();
    if name.starts_with("~$")
        || name.ends_with(".tmp")
        || name.contains(".tmp-")
        || name.ends_with(".bak")
        || name == "desktop.ini"
    {
        return true;
    }
    format!("\\{}", key.to_lowercase()).contains("\\_conflicts\\")
}

/// State keys and conflict paths use `\` on every OS, matching the WinForms app's state file.
fn rel_key(rel: &str) -> String {
    rel.replace('/', "\\")
}

fn join_rel(root: &Path, rel: &str) -> PathBuf {
    rel.split('\\')
        .fold(root.to_path_buf(), |p, part| p.join(part))
}

/// Every file under `root` as a `\`-separated relative path, sorted ignoring case.
fn list_rel(root: &Path) -> Vec<String> {
    let mut out = Vec::new();
    walk(root, "", &mut out);
    out.retain(|r| !is_ignored(r));
    out.sort_by_key(|r| r.to_lowercase());
    out
}

fn walk(dir: &Path, prefix: &str, out: &mut Vec<String>) {
    let Ok(rd) = fs::read_dir(dir) else {
        return;
    };
    for e in rd.filter_map(Result::ok) {
        let name = e.file_name().to_string_lossy().into_owned();
        let rel = if prefix.is_empty() {
            name
        } else {
            format!("{prefix}\\{name}")
        };
        match e.file_type() {
            Ok(t) if t.is_dir() => walk(&e.path(), &rel, out),
            Ok(t) if t.is_file() => out.push(rel),
            _ => {}
        }
    }
}

fn try_hash(path: &Path, result: &mut ShopSyncResult) -> Option<String> {
    let name = path
        .file_name()
        .map(|n| n.to_string_lossy().into_owned())
        .unwrap_or_default();
    let hashed = fs::metadata(path).and_then(|m| {
        if m.len() > MAX_BYTES {
            Ok(None)
        } else {
            hash_file(path).map(Some)
        }
    });
    match hashed {
        Ok(Some(h)) => Some(h),
        Ok(None) => {
            result.skipped += 1;
            result.notes.push(format!("Skipped (over 50 MB): {name}"));
            None
        }
        Err(e) => {
            result.skipped += 1;
            result.notes.push(format!("{name}: {e}"));
            None
        }
    }
}

fn two_way(
    local_root: &Path,
    remote_root: &Path,
    prefix: &str,
    state: &mut SyncState,
    result: &mut ShopSyncResult,
) -> io::Result<()> {
    fs::create_dir_all(local_root)?;
    fs::create_dir_all(remote_root)?;
    let mut seen = HashSet::new();
    let names: Vec<String> = list_rel(local_root)
        .into_iter()
        .chain(list_rel(remote_root))
        .filter(|r| seen.insert(r.to_lowercase()))
        .collect();
    for rel in names {
        let lp = join_rel(local_root, &rel);
        let rp = join_rel(remote_root, &rel);
        let key = format!("{prefix}/{}", rel_key(&rel));
        let (last_l, last_r) = state.get(&key);
        let conflict = || ShopConflict {
            kind: kind_of(&rel).into(),
            key: String::new(),
            relative_path: rel.clone(),
            local_path: path_text(&lp),
            remote_path: path_text(&rp),
            local_summary: "this PC".into(),
            remote_summary: "sync folder".into(),
            prefix: prefix.into(),
        };
        match (lp.is_file(), rp.is_file()) {
            (true, true) => {
                let Some(lh) = try_hash(&lp, result) else {
                    continue;
                };
                let Some(rh) = try_hash(&rp, result) else {
                    continue;
                };
                if lh == rh {
                    state.set(&key, &lh, &rh);
                    continue;
                }
                let local_changed = last_l.as_deref() != Some(lh.as_str());
                let remote_changed = last_r.as_deref() != Some(rh.as_str());
                match (local_changed, remote_changed) {
                    (true, false) => {
                        atomic_copy_file(&lp, &rp)?;
                        state.set(&key, &lh, &lh);
                        result.pushed += 1;
                    }
                    (false, true) => {
                        atomic_copy_file(&rp, &lp)?;
                        state.set(&key, &rh, &rh);
                        result.pulled += 1;
                    }
                    _ => result.conflicts.push(conflict()),
                }
            }
            (true, false) => {
                let Some(lh) = try_hash(&lp, result) else {
                    continue;
                };
                atomic_copy_file(&lp, &rp)?;
                state.set(&key, &lh, &lh);
                result.pushed += 1;
            }
            (false, true) => {
                let Some(rh) = try_hash(&rp, result) else {
                    continue;
                };
                atomic_copy_file(&rp, &lp)?;
                state.set(&key, &rh, &rh);
                result.pulled += 1;
            }
            (false, false) => {}
        }
    }
    Ok(())
}

/// Hash maps keyed ignoring case that remember the first spelling of each key.
#[derive(Default)]
struct CiMap(HashMap<String, (String, String)>);

impl CiMap {
    fn get(&self, key: &str) -> Option<String> {
        self.0.get(&key.to_lowercase()).map(|(_, v)| v.clone())
    }

    fn set(&mut self, key: &str, value: &str) {
        let slot = self
            .0
            .entry(key.to_lowercase())
            .or_insert_with(|| (key.to_string(), String::new()));
        slot.1 = value.to_string();
    }

    fn read(&mut self, v: Option<&Value>) {
        if let Some(Value::Object(members)) = v {
            for (k, x) in members {
                self.set(k, &x.text());
            }
        }
    }

    /// Entries sorted like `StringComparer.OrdinalIgnoreCase` (uppercase ordinal).
    fn sorted(&self) -> Vec<(&str, &str)> {
        let mut v: Vec<(&str, &str)> = self
            .0
            .values()
            .map(|(k, x)| (k.as_str(), x.as_str()))
            .collect();
        v.sort_by_key(|(k, _)| k.to_uppercase());
        v
    }
}

#[derive(Default)]
struct SyncState {
    last_sync_utc: String,
    local: CiMap,
    remote: CiMap,
}

impl SyncState {
    /// An unreadable or missing state file is a first sync, not an error.
    fn load(path: &Path) -> SyncState {
        let mut st = SyncState::default();
        let Some(d) = read_text(path).ok().and_then(|t| json::parse(&t).ok()) else {
            return st;
        };
        st.last_sync_utc = json::s(&d, "lastSyncUtc");
        st.local.read(d.get("local"));
        st.remote.read(d.get("remote"));
        st
    }

    fn get(&self, key: &str) -> (Option<String>, Option<String>) {
        (self.local.get(key), self.remote.get(key))
    }

    fn set(&mut self, key: &str, local: &str, remote: &str) {
        self.local.set(key, local);
        self.remote.set(key, remote);
    }

    /// Indented with CRLF, the way `ShopSync.SaveState` writes it.
    fn save(&self, path: &Path) -> io::Result<()> {
        let mut sb = String::new();
        sb.push_str("{\r\n");
        sb.push_str(&format!(
            "  \"lastSyncUtc\": {},\r\n",
            quote(&self.last_sync_utc)
        ));
        for (name, map, comma) in [
            ("local", &self.local, true),
            ("remote", &self.remote, false),
        ] {
            sb.push_str(&format!("  \"{name}\": {{\r\n"));
            let entries = map.sorted();
            for (i, (k, v)) in entries.iter().enumerate() {
                let sep = if i + 1 < entries.len() { "," } else { "" };
                sb.push_str(&format!("    {}: {}{sep}\r\n", quote(k), quote(v)));
            }
            sb.push_str(if comma { "  },\r\n" } else { "  }\r\n" });
        }
        sb.push_str("}\r\n");
        if let Some(dir) = path.parent() {
            fs::create_dir_all(dir)?;
        }
        fs::write(path, sb)
    }
}
