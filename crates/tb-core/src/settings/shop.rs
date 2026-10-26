//! Shop identity for work orders: tech id, extra share folder, assigned-WO file
//! (the fields of `src/Core/Settings/ShopSettings.cs` that the work-order store uses).

#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct ShopSettings {
    pub tech_id: String,
    pub share_folder: String,
    pub assigned_file: String,
}

/// Folder-safe tech id: letters, digits, `-`, `_`; spaces and dots become `-`; max 40;
/// `tech` when nothing is left (`ShopSettings.Sanitize`).
pub fn sanitize(raw: &str) -> String {
    let mut s: String = raw
        .trim()
        .chars()
        .filter_map(|c| match c {
            c if c.is_alphanumeric() || c == '-' || c == '_' => Some(c),
            ' ' | '.' => Some('-'),
            _ => None,
        })
        .collect();
    s = s.trim_matches('-').to_string();
    if s.is_empty() {
        return "tech".to_string();
    }
    s.chars().take(40).collect()
}

pub const FILE_NAME: &str = "shop-settings.json";

impl ShopSettings {
    /// `shop-settings.json` as the WinForms app writes it; `None` when missing or unreadable
    /// (so a caller can fall back), like `ShopSettings.TryLoad`.
    pub fn load_from(path: &std::path::Path) -> Option<ShopSettings> {
        let text = crate::settings::atomic::read_text(path).ok()?;
        let text = text.trim();
        if !text.starts_with('{') || !text.ends_with('}') {
            return None;
        }
        let o = crate::json::parse(text).ok()?;
        use crate::settings::store::str_field;
        Some(ShopSettings {
            tech_id: str_field(&o, "TechId").ok()?,
            share_folder: str_field(&o, "ShareFolder").ok()?,
            assigned_file: str_field(&o, "AssignedFile").ok()?,
        })
    }

    /// The JSON the WinForms app writes (`JavaScriptSerializer` field order).
    pub fn to_json(&self) -> String {
        use crate::json::Value;
        Value::Object(vec![
            ("TechId".into(), Value::String(self.tech_id.clone())),
            (
                "ShareFolder".into(),
                Value::String(self.share_folder.clone()),
            ),
            (
                "AssignedFile".into(),
                Value::String(self.assigned_file.clone()),
            ),
        ])
        .to_json()
    }

    /// Atomic save with a `.bak` copy, no BOM (`ShopSettings.SaveTo`).
    pub fn save_to(&self, path: &std::path::Path) -> std::io::Result<()> {
        if let Some(dir) = path.parent() {
            std::fs::create_dir_all(dir)?;
        }
        crate::settings::atomic::atomic_write_text(path, &self.to_json())
    }

    /// What the Shop settings form keeps: a sanitized tech id and trimmed paths
    /// (`ShopSettingsForm.Collect`).
    pub fn collect(tech_id: &str, share_folder: &str, assigned_file: &str) -> ShopSettings {
        ShopSettings {
            tech_id: sanitize(tech_id),
            share_folder: share_folder.trim().to_string(),
            assigned_file: assigned_file.trim().to_string(),
        }
    }

    /// Load `<folder>/shop-settings.json`; a blank tech id becomes `sanitize(user_name)`.
    pub fn load(folder: &std::path::Path, user_name: &str) -> ShopSettings {
        let mut s = Self::load_from(&folder.join(FILE_NAME)).unwrap_or_default();
        if s.tech_id.trim().is_empty() {
            s.tech_id = sanitize(user_name);
        }
        s
    }
}
