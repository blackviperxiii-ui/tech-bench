//! Just enough INI reading for the RP1210 vendor files (port of `src/Features/Adapters/Ini.cs`).
//! Section and key lookups are case-insensitive; keys before any header live in the "" section.

use std::collections::HashMap;

#[derive(Debug, Clone, Default)]
pub struct Ini {
    /// Upper-cased section name -> (upper-cased key -> value). The C# dictionaries use
    /// `StringComparer.OrdinalIgnoreCase`, which compares upper-cased characters.
    sections: HashMap<String, HashMap<String, String>>,
}

fn fold(s: &str) -> String {
    s.to_uppercase()
}

impl Ini {
    /// `None` text gives an empty INI with no sections at all (not even the sectionless one).
    pub fn parse(text: Option<&str>) -> Ini {
        let mut ini = Ini::default();
        let Some(text) = text else { return ini };
        let mut current = String::new();
        ini.sections.insert(current.clone(), HashMap::new());
        for raw in text.replace("\r\n", "\n").replace('\r', "\n").split('\n') {
            let line = raw.trim();
            if line.is_empty() || line.starts_with(';') || line.starts_with('#') {
                continue;
            }
            if let Some(rest) = line.strip_prefix('[') {
                // C#: close > 1 ? Substring(1, close - 1) : Substring(1)
                let name = match line.find(']') {
                    Some(close) if close > 1 => &line[1..close],
                    _ => rest,
                };
                current = fold(name.trim());
                ini.sections.entry(current.clone()).or_default();
                continue;
            }
            let Some(eq) = line.find('=') else { continue };
            if eq < 1 {
                continue;
            }
            let key = line[..eq].trim();
            let mut val = line[eq + 1..].trim();
            // Vendor INIs quote some values; RP1210 readers are expected to tolerate that.
            if val.len() >= 2 && val.starts_with('"') && val.ends_with('"') {
                val = &val[1..val.len() - 1];
            }
            ini.sections
                .get_mut(&current)
                .expect("current section exists")
                .insert(fold(key), val.to_string());
        }
        ini
    }

    /// Read and parse a file; `None` when it is missing or unreadable.
    pub fn load(path: &std::path::Path) -> Option<Ini> {
        if !path.is_file() {
            return None;
        }
        let bytes = std::fs::read(path).ok()?;
        Some(Ini::parse(Some(&String::from_utf8_lossy(&bytes))))
    }

    pub fn has_section(&self, section: Option<&str>) -> bool {
        self.sections.contains_key(&fold(section.unwrap_or("")))
    }

    pub fn get(&self, section: Option<&str>, key: Option<&str>) -> String {
        self.sections
            .get(&fold(section.unwrap_or("")))
            .and_then(|s| s.get(&fold(key.unwrap_or(""))))
            .cloned()
            .unwrap_or_default()
    }

    pub fn get_int(&self, section: &str, key: &str, fallback: i32) -> i32 {
        self.get(Some(section), Some(key))
            .trim()
            .parse()
            .unwrap_or(fallback)
    }

    /// Comma-separated value list, empties dropped.
    pub fn get_list(&self, section: &str, key: &str) -> Vec<String> {
        self.get(Some(section), Some(key))
            .split(',')
            .map(str::trim)
            .filter(|t| !t.is_empty())
            .map(str::to_string)
            .collect()
    }

    /// Integers from the list, non-numbers skipped, duplicates dropped in first-seen order.
    pub fn get_int_list(&self, section: &str, key: &str) -> Vec<i32> {
        let mut out = Vec::new();
        for s in self.get_list(section, key) {
            if let Ok(v) = s.parse::<i32>() {
                if !out.contains(&v) {
                    out.push(v);
                }
            }
        }
        out
    }
}
