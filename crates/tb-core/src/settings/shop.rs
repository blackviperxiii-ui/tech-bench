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
