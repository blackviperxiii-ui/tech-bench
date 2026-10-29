//! The shop shards under `<kb>/data/shop/<tech>/` that Add code / Add note / Add file write and the
//! index reads (the shard half of `src/Safety/ShopSync.cs`; copying them to a sync folder is
//! [`super::sync`]).

use super::user_codes::{self, key_of, UserCode};
use crate::json::{self, s};
use crate::settings::atomic::read_text;
use std::collections::HashSet;
use std::fs;
use std::io;
use std::path::{Path, PathBuf};

/// `ShopSync.SanitizeTechId`: letters, digits, `-`, `_`; spaces and dots become `-`; at most 40
/// characters, then leading and trailing `-`/`_` trimmed; `tech` when nothing is left.
pub fn sanitize_tech_id(raw: &str) -> String {
    let mut out = String::new();
    let mut n = 0;
    for c in raw.chars() {
        if c.is_alphanumeric() || c == '-' || c == '_' {
            out.push(c);
            n += 1;
        } else if c == ' ' || c == '.' {
            out.push('-');
            n += 1;
        }
        if n >= 40 {
            break;
        }
    }
    let t = out.trim_matches(['-', '_']);
    if t.is_empty() {
        "tech".to_string()
    } else {
        t.to_string()
    }
}

pub fn shop_root(kb_root: &Path) -> PathBuf {
    kb_root.join("data").join("shop")
}

pub fn shard_dir(kb_root: &Path, tech_id: &str) -> PathBuf {
    shop_root(kb_root).join(sanitize_tech_id(tech_id))
}

pub fn shard_codes_path(kb_root: &Path, tech_id: &str) -> PathBuf {
    shard_dir(kb_root, tech_id).join("user-codes.json")
}

fn is_meta_folder(name: &str) -> bool {
    name.eq_ignore_ascii_case("_conflicts") || name.eq_ignore_ascii_case("_resolved")
}

/// Shard folder names, sorted ignoring case (`_conflicts` and `_resolved` are not techs).
pub fn tech_ids(kb_root: &Path) -> Vec<String> {
    let Ok(rd) = fs::read_dir(shop_root(kb_root)) else {
        return Vec::new();
    };
    let mut list: Vec<String> = rd
        .filter_map(Result::ok)
        .filter(|e| e.path().is_dir())
        .map(|e| e.file_name().to_string_lossy().into_owned())
        .filter(|n| !is_meta_folder(n))
        .collect();
    list.sort_by_key(|n| n.to_lowercase());
    list
}

/// Brand+code keys listed in `_resolved/keep-both.txt` (lowercase; `#` lines are comments).
pub fn keep_both_keys(kb_root: &Path) -> HashSet<String> {
    let path = shop_root(kb_root).join("_resolved").join("keep-both.txt");
    read_text(&path)
        .map(|t| {
            t.lines()
                .map(str::trim)
                .filter(|l| !l.is_empty() && !l.starts_with('#'))
                .map(str::to_lowercase)
                .collect()
        })
        .unwrap_or_default()
}

/// Keys of the codes a conflict resolution already picked (`_resolved/user-codes.json`).
pub fn resolved_keys(kb_root: &Path) -> HashSet<String> {
    let path = shop_root(kb_root).join("_resolved").join("user-codes.json");
    user_codes::load_from(&path)
        .unwrap_or_default()
        .iter()
        .map(key_of)
        .collect()
}

/// Every user code: `data/user-codes.json`, each shard (author = tech), then `_resolved`.
pub fn load_all_codes(kb_root: &Path) -> io::Result<Vec<UserCode>> {
    let mut all = Vec::new();
    let mut seen = HashSet::new();
    let mut add = |path: PathBuf, author: &str| -> io::Result<()> {
        let full = path.canonicalize().unwrap_or_else(|_| path.clone());
        if !seen.insert(full) {
            return Ok(());
        }
        for mut c in user_codes::load_from(&path)? {
            if !author.is_empty() && c.author.is_empty() {
                c.author = author.to_string();
            }
            all.push(c);
        }
        Ok(())
    };
    add(user_codes::path_for(kb_root), "")?;
    for tech in tech_ids(kb_root) {
        add(shard_codes_path(kb_root, &tech), &tech)?;
    }
    add(
        shop_root(kb_root).join("_resolved").join("user-codes.json"),
        "resolved",
    )?;
    Ok(all)
}

/// Add or replace (by brand + code) one code in this tech's shard.
pub fn save_user_code(kb_root: &Path, tech_id: &str, entry: UserCode) -> io::Result<PathBuf> {
    let path = shard_codes_path(kb_root, tech_id);
    let mut mine = user_codes::load_from(&path)?;
    user_codes::upsert(&mut mine, entry);
    user_codes::save_to(&path, &mine)?;
    Ok(path)
}

fn first_non_blank(text: &str) -> &str {
    text.lines()
        .map(str::trim)
        .find(|t| !t.is_empty())
        .unwrap_or("")
}

/// Write a tech note as `<shard>/notes/<stamp>-<slug>.txt` (UTF-8, no BOM): the title, a blank
/// line, then the body. `stamp` is the local `yyyyMMdd_HHmmss`.
pub fn save_note(
    kb_root: &Path,
    tech_id: &str,
    title: &str,
    body: &str,
    stamp: &str,
) -> io::Result<PathBuf> {
    let dir = shard_dir(kb_root, tech_id).join("notes");
    fs::create_dir_all(&dir)?;
    let mut slug = sanitize_tech_id(title);
    if title.trim().is_empty() {
        let first = first_non_blank(body);
        if !first.is_empty() {
            slug = sanitize_tech_id(first);
        }
    }
    if slug == "tech" {
        slug = "note".to_string();
    }
    let path = dir.join(format!("{stamp}-{slug}.txt"));
    let mut text = String::new();
    if !title.trim().is_empty() {
        text.push_str(title.trim());
        text.push_str("\r\n");
        if !body.trim().is_empty() {
            text.push_str("\r\n");
        }
    }
    text.push_str(body);
    fs::write(&path, text)?;
    Ok(path)
}

/// Copy a shop file into `<shard>/files/`. A different file with the same name gets `-<stamp>`
/// before its extension; the same file added again is left as it is.
pub fn add_file(kb_root: &Path, tech_id: &str, source: &Path, stamp: &str) -> io::Result<PathBuf> {
    if !source.is_file() {
        return Err(io::Error::new(io::ErrorKind::NotFound, "No file to add."));
    }
    let dir = shard_dir(kb_root, tech_id).join("files");
    fs::create_dir_all(&dir)?;
    let name = source.file_name().unwrap_or_default();
    let mut dest = dir.join(name);
    if dest.exists() {
        if fs::read(&dest)? == fs::read(source)? {
            return Ok(dest);
        }
        let stem = source.file_stem().unwrap_or_default().to_string_lossy();
        let ext = source
            .extension()
            .map(|e| format!(".{}", e.to_string_lossy()))
            .unwrap_or_default();
        dest = dir.join(format!("{stem}-{stamp}{ext}"));
    }
    fs::copy(source, &dest)?;
    Ok(dest)
}

/// `sync.json` in the settings folder: the tech id the shop shards are written under (sanitized;
/// the user name when blank) and the optional sync folder (`ShopSync.LoadSettings`).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct SyncSettings {
    pub tech_id: String,
    pub sync_folder: String,
}

impl SyncSettings {
    pub fn load(settings_folder: &Path, user_name: &str) -> SyncSettings {
        let mut s = read_text(&settings_folder.join("sync.json"))
            .ok()
            .and_then(|t| json::parse(&t).ok())
            .map(|o| SyncSettings {
                tech_id: s(&o, "TechId"),
                sync_folder: s(&o, "SyncFolder"),
            })
            .unwrap_or_default();
        s.tech_id = if s.tech_id.trim().is_empty() {
            sanitize_tech_id(user_name)
        } else {
            sanitize_tech_id(&s.tech_id)
        };
        s
    }
}
