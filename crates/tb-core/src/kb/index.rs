//! In-memory KB index: loads the shipped JSON, notes and files, and ranks search hits
//! (port of `KbIndex` in `src/Features/Kb/KbIndex.cs`).
//!
//! Shop shards (`data/shop/<tech>/...` and `data/shop/_resolved/...`) load the same way the
//! WinForms index reads them; copying shards to a sync folder is not ported yet.

use super::paths::{file_name, resolve_document_path};
use super::shop;
use crate::j1939::Names;
use crate::json::{self, s, Value};
use std::collections::{HashMap, HashSet};
use std::fs;
use std::path::{Path, PathBuf};

pub const MAX_RESULTS: usize = 300;

const NL: &str = "\r\n";

#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Hit {
    pub kind: String,
    pub title: String,
    pub subtitle: String,
    pub body: String,
    pub path: String,
    pub hay: String,
    /// Primary identifier (fault code, equipment name) lowercased, for exact-match ranking.
    pub key: String,
    pub title_low: String,
    pub score: i32,
}

impl std::fmt::Display for Hit {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "[{}]  {}", self.kind, self.title)
    }
}

#[derive(Debug, Default)]
pub struct KbIndex {
    pub all: Vec<Hit>,
    pub errors: Vec<String>,
    pub root: String,
    pub status: String,
    /// Built-in names plus any `j1939-names.json` overrides from the last load.
    pub names: Names,
}

type LoadResult = Result<usize, String>;

fn read_root(path: &Path) -> Result<Option<Value>, String> {
    if !path.is_file() {
        return Ok(None);
    }
    let text = fs::read_to_string(path).map_err(|e| e.to_string())?;
    json::parse(&text).map(Some).map_err(|e| e.to_string())
}

fn add_line(sb: &mut String, label: &str, d: &Value, key: &str) {
    let v = s(d, key);
    if !v.is_empty() {
        sb.push_str(&format!("{label}: {v}{NL}"));
    }
}

fn join_arr(v: Option<&Value>) -> String {
    v.map(|a| {
        a.items()
            .iter()
            .map(Value::text)
            .collect::<Vec<_>>()
            .join(" ")
    })
    .unwrap_or_default()
}

fn first_line(text: &str) -> String {
    text.replace("\r\n", "\n")
        .split('\n')
        .map(str::trim)
        .find(|t| !t.is_empty())
        .unwrap_or("")
        .to_string()
}

fn ext_lower(p: &Path) -> String {
    p.extension()
        .map(|e| format!(".{}", e.to_string_lossy().to_lowercase()))
        .unwrap_or_default()
}

/// Files directly in `dir`, sorted by name (NTFS lists them that way; other file systems don't promise it).
fn files_in(dir: &Path) -> Result<Vec<PathBuf>, String> {
    let mut out: Vec<PathBuf> = fs::read_dir(dir)
        .map_err(|e| e.to_string())?
        .filter_map(Result::ok)
        .map(|e| e.path())
        .filter(|p| p.is_file())
        .collect();
    out.sort();
    Ok(out)
}

/// Keys may be decimal or 0x-prefixed hex.
fn parse_key(s: &str) -> Option<i32> {
    let s = s.trim();
    if s.is_empty() {
        return None;
    }
    match s.get(..2) {
        Some(p) if p.eq_ignore_ascii_case("0x") => i32::from_str_radix(&s[2..], 16).ok(),
        _ => s.parse().ok(),
    }
}

fn name_map(o: Option<&Value>) -> HashMap<i32, String> {
    let mut map = HashMap::new();
    if let Some(Value::Object(members)) = o {
        for (k, v) in members {
            if let Some(key) = parse_key(k) {
                let t = v.text().trim().to_string();
                if !t.is_empty() {
                    map.insert(key, t);
                }
            }
        }
    }
    map
}

fn merge_name_map(
    src: HashMap<i32, String>,
    dest: &mut HashMap<i32, String>,
    overwrite: bool,
) -> usize {
    let mut n = 0;
    for (k, v) in src {
        match dest.get(&k) {
            Some(existing) if !overwrite || *existing == v => continue,
            _ => {
                dest.insert(k, v);
                n += 1;
            }
        }
    }
    n
}

impl KbIndex {
    pub fn new() -> Self {
        Self::default()
    }

    /// Load everything under `root`. Each file loads on its own, so one bad file costs that
    /// section only and is listed in `errors` and `status`.
    pub fn load(&mut self, root: &str) {
        self.all.clear();
        self.errors.clear();
        self.root = root.to_string();
        self.names.reset();
        if root.trim().is_empty() {
            self.status = "No knowledge base folder configured.".to_string();
            return;
        }
        let data = Path::new(root).join("data");
        let mut spn = HashMap::new();
        let mut pgn = HashMap::new();
        let mut sa = HashMap::new();

        let none = HashSet::new();
        let r = self.load_codes(&data.join("kb.json"), "", &none, &none, None);
        let mut n_code = self.track("kb.json", r);
        let r = self.load_passwords(&data.join("passwords").join("ifix-passwords.json"));
        let n_pwd = self.track("ifix-passwords.json", r);
        let r = self.load_manuals(&data.join("usb-manuals.json"));
        let n_man = self.track("usb-manuals.json", r);
        let r = self.load_filters(&data.join("rental-portable-filters-oil.json"));
        let n_fil = self.track("rental-portable-filters-oil.json", r);
        let r = self.load_equipment(&data.join("rental-equipment-info.json"));
        let n_eq = self.track("rental-equipment-info.json", r);
        let r = merge_names(
            &data.join("j1939-names.json"),
            &mut spn,
            &mut pgn,
            &mut sa,
            false,
        );
        let mut n_names = self.track("j1939-names.json", r);
        let r = self.load_note_dir(&data.join("notes"), "");
        let mut n_notes = self.track("notes", r);
        let r = self.load_file_dir(&data.join("files"), "");
        let mut n_files = self.track("files", r);

        let kb = Path::new(root);
        let shop_root = shop::shop_root(kb);
        let skip = shop::resolved_keys(kb);
        let keep_both = shop::keep_both_keys(kb);
        let mut seen_user = HashSet::new();
        let r = self.load_codes(
            &shop_root.join("_resolved").join("user-codes.json"),
            "resolved",
            &none,
            &none,
            Some(&mut seen_user),
        );
        n_code += self.track("_resolved\\user-codes.json", r);
        for tech in shop::tech_ids(kb) {
            let shard = shop::shard_dir(kb, &tech);
            let r = self.load_codes(
                &shard.join("user-codes.json"),
                &tech,
                &skip,
                &keep_both,
                Some(&mut seen_user),
            );
            n_code += self.track(&format!("{tech}\\user-codes.json"), r);
            let r = merge_names(
                &shard.join("j1939-names.json"),
                &mut spn,
                &mut pgn,
                &mut sa,
                false,
            );
            n_names += self.track(&format!("{tech}\\j1939-names.json"), r);
            let r = self.load_note_dir(&shard.join("notes"), &tech);
            n_notes += self.track(&format!("{tech}\\notes"), r);
            let r = self.load_file_dir(&shard.join("files"), &tech);
            n_files += self.track(&format!("{tech}\\files"), r);
        }
        let r = self.load_codes(
            &data.join("user-codes.json"),
            "",
            &none,
            &none,
            Some(&mut seen_user),
        );
        n_code += self.track("user-codes.json", r);
        let r = merge_names(
            &shop_root.join("_resolved").join("j1939-names.json"),
            &mut spn,
            &mut pgn,
            &mut sa,
            true,
        );
        n_names += self.track("_resolved\\j1939-names.json", r);
        self.names.load(Some(spn), Some(pgn), Some(sa));

        self.status = format!(
            "Codes {n_code}  ·  Passwords {n_pwd}  ·  Manuals {n_man}  ·  Filters {n_fil}  ·  Equipment {n_eq}"
        );
        if n_names > 0 {
            self.status += &format!("  ·  J1939 names {n_names}");
        }
        if n_notes > 0 {
            self.status += &format!("  ·  Notes {n_notes}");
        }
        if n_files > 0 {
            self.status += &format!("  ·  Files {n_files}");
        }
        if self.all.is_empty() {
            self.status += &format!("  ·  nothing loaded from {root}");
        }
        if !self.errors.is_empty() {
            self.status += &format!(
                "  ·  {} file(s) failed: {}",
                self.errors.len(),
                self.errors.join("; ")
            );
        }
    }

    fn track(&mut self, label: &str, r: LoadResult) -> usize {
        r.unwrap_or_else(|e| {
            self.errors.push(format!("{label} ({e})"));
            0
        })
    }

    fn add(&mut self, mut h: Hit, key: &str) {
        h.key = key.trim().to_lowercase();
        h.title_low = h.title.to_lowercase();
        self.all.push(h);
    }

    fn load_passwords(&mut self, path: &Path) -> LoadResult {
        let Some(root) = read_root(path)? else {
            return Ok(0);
        };
        let mut n = 0;
        for d in root.get("passwords").map(Value::items).unwrap_or_default() {
            if !d.is_object() {
                continue;
            }
            let (mfg, ctl, lvl, code) = (
                s(d, "manufacturer"),
                s(d, "controller"),
                s(d, "access_level"),
                s(d, "code_or_procedure"),
            );
            let hay = format!("{mfg} {ctl} {lvl} {code}").to_lowercase();
            let h = Hit {
                kind: "PASSWORD".into(),
                title: format!("{mfg}  ·  {ctl}  ·  {lvl}"),
                subtitle: "Service access — trained use only".into(),
                body: code,
                hay,
                ..Hit::default()
            };
            self.add(h, &ctl);
            n += 1;
        }
        Ok(n)
    }

    fn load_manuals(&mut self, path: &Path) -> LoadResult {
        let Some(root) = read_root(path)? else {
            return Ok(0);
        };
        let mut n = 0;
        for d in root.get("documents").map(Value::items).unwrap_or_default() {
            if !d.is_object() {
                continue;
            }
            let mut title = s(d, "document_name");
            if title.is_empty() {
                title = s(d, "filename");
            }
            let (mfg, ctl, local, filename) = (
                s(d, "manufacturer"),
                s(d, "controller"),
                s(d, "local_path"),
                s(d, "filename"),
            );
            // Only expose an openable path when it resolves under the KB tree.
            let open = resolve_document_path(&self.root, &local, &filename);
            let hay = format!("{title} {mfg} {ctl} {filename} {}", s(d, "category")).to_lowercase();
            let subtitle = if ctl.is_empty() {
                mfg
            } else {
                format!("{mfg}  ·  {ctl}")
            };
            let h = Hit {
                kind: "MANUAL".into(),
                title: title.clone(),
                subtitle,
                body: local,
                path: open
                    .map(|p| p.to_string_lossy().into_owned())
                    .unwrap_or_default(),
                hay,
                ..Hit::default()
            };
            self.add(h, &title);
            n += 1;
        }
        Ok(n)
    }

    fn load_filters(&mut self, path: &Path) -> LoadResult {
        let Some(root) = read_root(path)? else {
            return Ok(0);
        };
        let mut n = 0;
        for d in root.get("rows").map(Value::items).unwrap_or_default() {
            if !d.is_object() {
                continue;
            }
            let (ty, code) = (s(d, "comp_type"), s(d, "code"));
            let mut body = String::new();
            add_line(&mut body, "Engine oil filter", d, "eng_oil_filter");
            add_line(&mut body, "Fuel/water", d, "eng_fuel_water");
            add_line(&mut body, "Fuel filter", d, "fuel_filter");
            add_line(&mut body, "Separator", d, "separator");
            add_line(&mut body, "Air primary", d, "air_filter_primary");
            add_line(&mut body, "Air filter", d, "air_filter");
            add_line(&mut body, "Comp oil filter", d, "comp_oil_filter");
            add_line(&mut body, "Other", d, "other_filters");
            add_line(&mut body, "Engine oil qty", d, "engine_oil_qty");
            add_line(&mut body, "Comp oil qty", d, "comp_oil_qty");
            body.push_str(&format!("Verify ENG vs A/E piping before ordering.{NL}"));
            let hay = format!("{ty} {code} {body}").to_lowercase();
            let h = Hit {
                kind: "FILTER".into(),
                title: format!("{code}  ·  {ty}"),
                subtitle: "Rental filter / oil chart".into(),
                body,
                hay,
                ..Hit::default()
            };
            self.add(h, &code);
            n += 1;
        }
        Ok(n)
    }

    fn load_equipment(&mut self, path: &Path) -> LoadResult {
        let Some(root) = read_root(path)? else {
            return Ok(0);
        };
        let mut n = 0;
        for d in root.get("rows").map(Value::items).unwrap_or_default() {
            if !d.is_object() {
                continue;
            }
            let eq = s(d, "equipment");
            let body = format!(
                "W {}  L {}  H {}{NL}Weight {}{NL}Rim {}  Tire {}{NL}Shop {}",
                s(d, "width"),
                s(d, "length"),
                s(d, "height"),
                s(d, "weight"),
                s(d, "rim_size"),
                s(d, "tire_size"),
                s(d, "acs_part_no")
            );
            let hay = format!("{eq} {body} {}", s(d, "acs_part_no")).to_lowercase();
            let h = Hit {
                kind: "EQUIP".into(),
                title: eq.clone(),
                subtitle: "Dims / tires / Shop".into(),
                body,
                hay,
                ..Hit::default()
            };
            self.add(h, &eq);
            n += 1;
        }
        Ok(n)
    }

    /// Fault codes from `kb.json` or a `user-codes.json`. A brand+code in `skip` (already resolved)
    /// or in `seen` (loaded from an earlier user-codes file) is skipped unless it is in
    /// `keep_both`.
    fn load_codes(
        &mut self,
        path: &Path,
        author: &str,
        skip: &HashSet<String>,
        keep_both: &HashSet<String>,
        mut seen: Option<&mut HashSet<String>>,
    ) -> LoadResult {
        let Some(root) = read_root(path)? else {
            return Ok(0);
        };
        let mut n = 0;
        for d in root.get("codes").map(Value::items).unwrap_or_default() {
            if !d.is_object() {
                continue;
            }
            let (brand, code) = (s(d, "brand_id"), s(d, "code"));
            let key = format!(
                "{}\u{1}{}",
                brand.trim().to_lowercase(),
                code.trim().to_lowercase()
            );
            let kept = keep_both.contains(&key);
            if !kept
                && (skip.contains(&key) || seen.as_ref().is_some_and(|seen| seen.contains(&key)))
            {
                continue;
            }
            let (title, desc) = (s(d, "title"), s(d, "description"));
            let mut body = String::new();
            let mut line = |t: &str| {
                body.push_str(t);
                body.push_str(NL);
            };
            if !desc.is_empty() {
                line(&desc);
            }
            line("");
            line("Likely causes:");
            for x in d.get("likely_causes").map(Value::items).unwrap_or_default() {
                line(&format!("  • {}", x.text()));
            }
            line("");
            line("Checks:");
            for x in d.get("checks").map(Value::items).unwrap_or_default() {
                line(&format!("  • {}", x.text()));
            }
            let reset = s(d, "reset_notes");
            if !reset.is_empty() {
                line("");
                line(&format!("Reset: {reset}"));
            }
            let safety = s(d, "safety");
            if !safety.is_empty() {
                line("");
                line(&format!("Safety: {safety}"));
            }
            let mut sub = format!(
                "{}  ·  {}  ·  {}",
                s(d, "severity"),
                s(d, "controller_id"),
                s(d, "confidence")
            );
            if !author.is_empty() {
                sub += &format!("  ·  {author}");
            }
            let hay = format!(
                "{brand} {code} {title} {desc} {} {author} {}",
                s(d, "controller_id"),
                join_arr(d.get("tags"))
            )
            .to_lowercase();
            let h = Hit {
                kind: "CODE".into(),
                title: format!("{brand}  {code}  —  {title}"),
                subtitle: sub,
                body,
                hay,
                ..Hit::default()
            };
            self.add(h, &code);
            n += 1;
            if let Some(seen) = seen.as_deref_mut() {
                seen.insert(key);
            }
        }
        Ok(n)
    }

    fn load_note_dir(&mut self, dir: &Path, author: &str) -> LoadResult {
        if !dir.is_dir() {
            return Ok(0);
        }
        let mut n = 0;
        for f in files_in(dir)? {
            let ext = ext_lower(&f);
            if ext != ".txt" && ext != ".md" {
                continue;
            }
            let Ok(body) = fs::read_to_string(&f) else {
                continue;
            };
            let first = first_line(&body);
            let title = if first.is_empty() {
                f.file_stem()
                    .map(|s| s.to_string_lossy().into_owned())
                    .unwrap_or_default()
            } else {
                first
            };
            let mut sub = "Tech note".to_string();
            if !author.is_empty() {
                sub += &format!("  ·  {author}");
            }
            let name = f
                .file_name()
                .map(|s| s.to_string_lossy().into_owned())
                .unwrap_or_default();
            let hay = format!("{title} {author} {name} {body}").to_lowercase();
            let h = Hit {
                kind: "NOTE".into(),
                title: title.clone(),
                subtitle: sub,
                body,
                path: f.to_string_lossy().into_owned(),
                hay,
                ..Hit::default()
            };
            self.add(h, &title);
            n += 1;
        }
        Ok(n)
    }

    fn load_file_dir(&mut self, dir: &Path, author: &str) -> LoadResult {
        if !dir.is_dir() {
            return Ok(0);
        }
        let mut n = 0;
        for f in files_in(dir)? {
            let full = f.to_string_lossy().into_owned();
            let name = file_name(&full).to_string();
            let ext = ext_lower(&f);
            let lower = name.to_lowercase();
            if ext == ".tmp" || ext == ".bak" || lower.contains(".tmp-") || lower == "desktop.ini" {
                continue;
            }
            let mut sub = "Shop file".to_string();
            if !author.is_empty() {
                sub += &format!("  ·  {author}");
            }
            let h = Hit {
                kind: "FILE".into(),
                title: name.clone(),
                subtitle: sub,
                body: full.clone(),
                path: full,
                hay: format!("{name} {author}").to_lowercase(),
                ..Hit::default()
            };
            self.add(h, &name);
            n += 1;
        }
        Ok(n)
    }

    /// Up to `max` ranked hits of `kind` ("ALL" for every kind), and the total match count so the
    /// UI can say when it capped. An empty query lists entries in load order.
    pub fn search(&self, query: &str, kind: &str, max: usize) -> (Vec<Hit>, usize) {
        if query.trim().is_empty() {
            let all: Vec<&Hit> = self
                .all
                .iter()
                .filter(|h| kind == "ALL" || h.kind == kind)
                .collect();
            let total = all.len();
            return (all.into_iter().take(max).cloned().collect(), total);
        }
        let mut matches = self.matches(&query.trim().to_lowercase(), kind);
        let total = matches.len();
        rank(&mut matches);
        matches.truncate(max);
        (matches, total)
    }

    /// Every entry of `kind` whose text holds all of the query's tokens, scored, unsorted.
    fn matches(&self, q: &str, kind: &str) -> Vec<Hit> {
        self.match_indices(q, kind)
            .into_iter()
            .map(|(i, score)| Hit {
                score,
                ..self.all[i].clone()
            })
            .collect()
    }

    fn match_indices(&self, q: &str, kind: &str) -> Vec<(usize, i32)> {
        let toks: Vec<&str> = q.split(' ').filter(|t| !t.is_empty()).collect();
        self.all
            .iter()
            .enumerate()
            .filter(|(_, h)| kind == "ALL" || h.kind == kind)
            .filter(|(_, h)| toks.iter().all(|t| h.hay.contains(t)))
            .map(|(i, h)| (i, score(h, q, &toks)))
            .collect()
    }

    /// KB entries that look relevant to a live J1939 fault, so the INLINE 7 page can show shop
    /// knowledge next to the built-in CodeBook text. One row per entry, at its best score.
    pub fn spn_hits(&self, spn: i32, _fmi: i32, max: usize) -> Vec<Hit> {
        if max < 1 {
            return Vec::new();
        }
        let spn_name = self.names.spn(spn);
        let mut queries = vec![format!("spn {spn}"), spn.to_string()];
        if !spn_name.is_empty() {
            queries.push(spn_name);
        }
        // Keyed by position in `all`: two notes with the same first line are still two entries.
        let mut best: HashMap<usize, i32> = HashMap::new();
        let mut order: Vec<usize> = Vec::new();
        for q in &queries {
            for (i, sc) in self.match_indices(&q.trim().to_lowercase(), "ALL") {
                match best.get(&i) {
                    None => {
                        order.push(i);
                        best.insert(i, sc);
                    }
                    Some(&prev) if prev < sc => {
                        best.insert(i, sc);
                    }
                    _ => {}
                }
            }
        }
        let mut hits: Vec<Hit> = order
            .into_iter()
            .map(|i| Hit {
                score: best[&i],
                ..self.all[i].clone()
            })
            .collect();
        rank(&mut hits);
        hits.truncate(max);
        hits
    }

    /// The top three [`spn_hits`](Self::spn_hits) as text, or `None` when nothing matches.
    pub fn spn_text(&self, spn: i32, fmi: i32) -> Option<String> {
        let hits = self.spn_hits(spn, fmi, 3);
        if hits.is_empty() {
            return None;
        }
        let mut sb = String::new();
        for h in hits {
            sb.push_str(&format!("[{}] {}{NL}", h.kind, h.title));
            if !h.subtitle.trim().is_empty() {
                sb.push_str(&format!("{}{NL}", h.subtitle));
            }
            let mut body = h.body.trim().to_string();
            if body.encode_utf16().count() > 900 {
                // .NET Substring counts UTF-16 units; cut on a char boundary at or below 900.
                let cut: String =
                    String::from_utf16_lossy(&body.encode_utf16().take(900).collect::<Vec<_>>());
                body = format!("{}…", cut.trim_end_matches('\u{FFFD}'));
            }
            if !body.is_empty() {
                sb.push_str(&format!("{body}{NL}"));
            }
            sb.push_str(NL);
        }
        Some(sb)
    }
}

fn merge_names(
    path: &Path,
    spn: &mut HashMap<i32, String>,
    pgn: &mut HashMap<i32, String>,
    sa: &mut HashMap<i32, String>,
    overwrite: bool,
) -> LoadResult {
    let Some(root) = read_root(path)? else {
        return Ok(0);
    };
    Ok(merge_name_map(name_map(root.get("spn")), spn, overwrite)
        + merge_name_map(name_map(root.get("pgn")), pgn, overwrite)
        + merge_name_map(name_map(root.get("sa")), sa, overwrite))
}

/// Highest score first, then title, case-insensitive. An exact fault-code match has to come
/// first: unranked, "F68" buried the F68 entry behind every row that merely mentioned it.
fn rank(matches: &mut [Hit]) {
    matches.sort_by(|a, b| {
        b.score
            .cmp(&a.score)
            .then_with(|| cmp_ignore_case(&a.title, &b.title))
    });
}

/// .NET `StringComparison.OrdinalIgnoreCase`: compare upper-cased UTF-16 code units.
fn cmp_ignore_case(a: &str, b: &str) -> std::cmp::Ordering {
    let up = |s: &str| -> Vec<u16> { s.to_uppercase().encode_utf16().collect() };
    up(a).cmp(&up(b))
}

fn score(h: &Hit, q: &str, toks: &[&str]) -> i32 {
    let mut s = 0;
    if !h.key.is_empty() {
        if h.key == q {
            s += 1000;
        } else if h.key.starts_with(q) {
            s += 400;
        } else if h.key.contains(q) {
            s += 150;
        }
    }
    if h.title_low.contains(q) {
        s += 120;
    }
    for t in toks {
        if h.title_low.contains(t) {
            s += 30;
        }
        if !h.key.is_empty() && h.key.contains(t) {
            s += 20;
        }
    }
    if h.kind == "CODE" {
        s += 10;
    }
    s
}
