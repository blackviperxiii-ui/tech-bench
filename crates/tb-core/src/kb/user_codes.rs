//! Read/write for a `user-codes.json` (port of `src/Features/Kb/UserCodes.cs`) and what the
//! Add code / Add note actions pre-fill and search for (`src/Features/Kb/ShopAdd.cs`).

use crate::json::{self, s, Value};
use crate::settings::atomic::{atomic_write_text, read_text};
use std::io;
use std::path::{Path, PathBuf};

#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct UserCode {
    pub brand: String,
    pub code: String,
    pub title: String,
    pub description: String,
    pub severity: String,
    pub controller: String,
    pub causes: Vec<String>,
    pub checks: Vec<String>,
    pub reset: String,
    pub safety: String,
    /// Which shop shard this came from. Not written to JSON.
    pub author: String,
}

impl UserCode {
    pub fn is_usable(&self) -> bool {
        !self.code.trim().is_empty() || !self.title.trim().is_empty()
    }
}

pub fn path_for(kb_root: &Path) -> PathBuf {
    kb_root.join("data").join("user-codes.json")
}

/// Brand + code, trimmed and lowercased: the upsert and merge key.
pub fn key_of(c: &UserCode) -> String {
    format!(
        "{}\u{1}{}",
        c.brand.trim().to_lowercase(),
        c.code.trim().to_lowercase()
    )
}

fn str_list(d: &Value, key: &str) -> Vec<String> {
    d.get(key)
        .map(Value::items)
        .unwrap_or_default()
        .iter()
        .map(|v| v.text().trim().to_string())
        .filter(|t| !t.is_empty())
        .collect()
}

/// Codes in `path`; empty when the file is missing. A parse failure or a file without a `codes`
/// array is an error, never "empty": upsert + save would otherwise rewrite a full shop file from
/// one new entry.
pub fn load_from(path: &Path) -> io::Result<Vec<UserCode>> {
    if !path.is_file() {
        return Ok(Vec::new());
    }
    let bad = |msg: String| io::Error::new(io::ErrorKind::InvalidData, msg);
    let root = json::parse(&read_text(path)?).map_err(|e| bad(e.to_string()))?;
    let codes = match root.get("codes") {
        Some(Value::Array(a)) => a,
        Some(Value::Null) | None => {
            return Err(bad(format!(
                "user-codes.json is missing a codes array: {}",
                path.display()
            )))
        }
        Some(_) => {
            return Err(bad(format!(
                "user-codes.json codes value is not an array: {}",
                path.display()
            )))
        }
    };
    Ok(codes
        .iter()
        .filter(|d| d.is_object())
        .map(|d| UserCode {
            brand: s(d, "brand_id"),
            code: s(d, "code"),
            title: s(d, "title"),
            description: s(d, "description"),
            severity: s(d, "severity"),
            controller: s(d, "controller_id"),
            causes: str_list(d, "likely_causes"),
            checks: str_list(d, "checks"),
            reset: s(d, "reset_notes"),
            safety: s(d, "safety"),
            author: String::new(),
        })
        .collect())
}

/// Replace the entry with the same brand + code, else append. Unusable entries are ignored.
pub fn upsert(list: &mut Vec<UserCode>, entry: UserCode) {
    if !entry.is_usable() {
        return;
    }
    let key = key_of(&entry);
    match list.iter_mut().find(|c| key_of(c) == key) {
        Some(slot) => *slot = entry,
        None => list.push(entry),
    }
}

/// JSON string literal the way `UserCodes.Quote` writes it.
pub fn quote(text: &str) -> String {
    let mut out = String::from("\"");
    for c in text.chars() {
        match c {
            '"' => out.push_str("\\\""),
            '\\' => out.push_str("\\\\"),
            '\n' => out.push_str("\\n"),
            '\r' => out.push_str("\\r"),
            '\t' => out.push_str("\\t"),
            c if (c as u32) < 32 => out.push_str(&format!("\\u{:04x}", c as u32)),
            c => out.push(c),
        }
    }
    out.push('"');
    out
}

/// Indented output (CRLF, like `StringBuilder.AppendLine` on Windows) because this file is meant
/// to stay hand-editable.
pub fn to_json(codes: &[UserCode]) -> String {
    const NL: &str = "\r\n";
    let mut sb = String::new();
    let mut line = |t: &str| {
        sb.push_str(t);
        sb.push_str(NL);
    };
    line("{");
    line("  \"codes\": [");
    for (i, c) in codes.iter().enumerate() {
        line("    {");
        let field = |name: &str, v: &str, comma: bool| {
            format!(
                "      \"{name}\": {}{}",
                quote(v),
                if comma { "," } else { "" }
            )
        };
        line(&field("brand_id", &c.brand, true));
        line(&field("code", &c.code, true));
        line(&field("title", &c.title, true));
        line(&field("description", &c.description, true));
        line(&field("severity", &c.severity, true));
        line(&field("controller_id", &c.controller, true));
        for (name, values) in [("likely_causes", &c.causes), ("checks", &c.checks)] {
            if values.is_empty() {
                line(&format!("      \"{name}\": [],"));
                continue;
            }
            line(&format!("      \"{name}\": ["));
            for (j, v) in values.iter().enumerate() {
                let comma = if j < values.len() - 1 { "," } else { "" };
                line(&format!("        {}{comma}", quote(v)));
            }
            line("      ],");
        }
        line(&field("reset_notes", &c.reset, true));
        line(&field("safety", &c.safety, true));
        line(&field("source", "tech-bench", false));
        line(if i < codes.len() - 1 {
            "    },"
        } else {
            "    }"
        });
    }
    line("  ]");
    line("}");
    sb
}

/// Atomic temp → replace with a `.bak` copy, no BOM.
pub fn save_to(path: &Path, codes: &[UserCode]) -> io::Result<()> {
    if let Some(dir) = path.parent() {
        std::fs::create_dir_all(dir)?;
    }
    atomic_write_text(path, &to_json(codes))
}

/// One entry per non-blank line, without a leading bullet (`•`, `-`, `*`).
pub fn split_lines(text: &str) -> Vec<String> {
    text.replace("\r\n", "\n")
        .split('\n')
        .map(|raw| {
            raw.trim()
                .trim_start_matches(['•', '-', '*'])
                .trim()
                .to_string()
        })
        .filter(|t| !t.is_empty())
        .collect()
}

pub const NO_KB_MESSAGE: &str =
    "No knowledge base folder is configured, so there is nowhere to save.";

/// Add code seed: the highlighted CODE hit's saved entry, or a blank code titled after the hit.
pub fn code_seed(saved: &[UserCode], hit_kind: &str, hit_title: &str) -> UserCode {
    if hit_kind != "CODE" {
        return UserCode::default();
    }
    let want = hit_title.to_lowercase();
    let mut seed = saved
        .iter()
        .find(|c| format!("{}  {}  —  {}", c.brand, c.code, c.title).to_lowercase() == want)
        .cloned()
        .unwrap_or_default();
    if !seed.is_usable() {
        seed.title = hit_title.to_string();
    }
    seed
}

pub fn code_query(code: &UserCode) -> String {
    format!("{} {}", code.code, code.title).trim().to_string()
}

/// The note title, else its first non-blank line.
pub fn note_query(title: &str, body: &str) -> String {
    if !title.trim().is_empty() {
        return title.to_string();
    }
    body.replace("\r\n", "\n")
        .split('\n')
        .map(str::trim)
        .find(|t| !t.is_empty())
        .unwrap_or(title)
        .to_string()
}
