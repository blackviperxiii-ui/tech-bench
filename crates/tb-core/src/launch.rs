//! Shell-open guard for knowledge-base files and work-order attachments (the policy half of
//! `src/Safety/SafeOpen.cs`; the shell call itself belongs to each front end).

const BLOCKED_EXTENSIONS: &[&str] = &[
    ".exe",
    ".com",
    ".bat",
    ".cmd",
    ".scr",
    ".pif",
    ".msi",
    ".msp",
    ".ps1",
    ".psm1",
    ".vbs",
    ".vbe",
    ".js",
    ".jse",
    ".wsf",
    ".wsh",
    ".hta",
    ".cpl",
    ".reg",
    ".lnk",
    ".jar",
    ".appref-ms",
    ".application",
    ".gadget",
    ".msc",
    ".psc1",
    ".scf",
    ".url",
    ".vb",
    ".ws",
    ".inf",
    ".dll",
    ".sys",
    ".chm",
    ".docm",
    ".xlsm",
    ".pptm",
    ".dotm",
    ".xltm",
    ".ppsm",
];

fn trim_end_dots_spaces(s: &str) -> &str {
    s.trim_end_matches([' ', '.'])
}

fn starts_with_drive(path: &str) -> bool {
    let mut c = path.chars();
    matches!((c.next(), c.next()), (Some(l), Some(':')) if l.is_alphabetic())
}

/// `file` and the alternate-stream part after the first colon past any drive letter.
fn split_stream(path: &str) -> (&str, &str) {
    let start = if starts_with_drive(path) {
        path.char_indices().nth(2).map_or(path.len(), |(i, _)| i)
    } else {
        0
    };
    match path[start..].find(':') {
        Some(i) => (&path[..start + i], &path[start + i + 1..]),
        None => (path, ""),
    }
}

fn extension_of(path: &str) -> &str {
    let start = path.rfind(['\\', '/']).map_or(0, |i| i + 1);
    let mut end = path.len();
    if let Some(colon) = path.rfind(':') {
        let drive_colon =
            colon == path.chars().next().map_or(0, char::len_utf8) && starts_with_drive(path);
        if colon >= start && !drive_colon {
            end = colon;
        }
    }
    if end < start {
        return "";
    }
    let name = trim_end_dots_spaces(&path[start..end]);
    match name.rfind('.') {
        Some(dot) if dot < name.len() - 1 => &name[dot..],
        _ => "",
    }
}

fn is_blocked_extension(ext: &str) -> bool {
    !ext.is_empty()
        && BLOCKED_EXTENSIONS
            .iter()
            .any(|b| b.eq_ignore_ascii_case(ext))
}

/// True when this path must not be handed to the OS opener. Empty paths are not launched.
pub fn is_blocked_launch_path(path: &str) -> bool {
    if path.trim().is_empty() {
        return true;
    }
    let trimmed = trim_end_dots_spaces(path);
    if trimmed.is_empty() {
        return true;
    }
    let (file, stream) = split_stream(trimmed);
    is_blocked_extension(extension_of(file))
        || (!stream.is_empty() && is_blocked_extension(extension_of(stream)))
}

/// What to tell the tech when a path is refused.
pub fn blocked_launch_message(path: &str) -> String {
    let mut shown = "(no file)".to_string();
    if !path.trim().is_empty() {
        let trimmed = trim_end_dots_spaces(path.trim());
        let name = trimmed.rsplit(['\\', '/', ':']).next().unwrap_or("");
        shown = if name.is_empty() { path.trim() } else { name }.to_string();
    }
    format!(
        "Tech Bench won't open program or script files from the knowledge base: {shown}. Open it from Explorer if you trust it."
    )
}

/// What to tell the tech when the file is gone (`SafeOpen.MissingFileMessage`).
pub fn missing_file_message(path: &str) -> String {
    let shown = if path.trim().is_empty() {
        "(no file)"
    } else {
        path.trim()
    };
    format!("Cannot find this file:\n{shown}\n\nCheck that the USB drive or network share is connected.")
}
