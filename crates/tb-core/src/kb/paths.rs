//! KB document path confinement (the path half of `KbIndex.cs`): a manual only gets an openable
//! path when it resolves under the KB root. Paths are normalized lexically like .NET
//! `Path.GetFullPath`, so `..` can't climb out and the file system isn't touched to decide.

use std::path::{Component, Path, PathBuf};

/// True for rooted paths, Windows drive paths, and UNC, on every OS.
pub fn is_absolute_path(path: &str) -> bool {
    if path.trim().is_empty() {
        return false;
    }
    if Path::new(path).has_root() {
        return true;
    }
    let p = path.trim();
    let b = p.as_bytes();
    if b.len() >= 3 && b[0].is_ascii_alphabetic() && b[1] == b':' && (b[2] == b'\\' || b[2] == b'/')
    {
        return true;
    }
    p.starts_with("\\\\") || p.starts_with("//")
}

/// `Path.GetFullPath` without touching the disk: absolute against the current directory, with
/// `.` and `..` folded away.
fn full_path(path: &Path) -> Option<PathBuf> {
    let joined = if path.is_absolute() {
        path.to_path_buf()
    } else {
        std::env::current_dir().ok()?.join(path)
    };
    let mut out = PathBuf::new();
    for c in joined.components() {
        match c {
            Component::ParentDir => {
                out.pop();
            }
            Component::CurDir => {}
            other => out.push(other.as_os_str()),
        }
    }
    Some(out)
}

fn path_under_root(kb_root: &str, path: &str) -> Option<PathBuf> {
    if kb_root.trim().is_empty() || path.trim().is_empty() {
        return None;
    }
    let root_full = full_path(Path::new(kb_root))?;
    let candidate = if is_absolute_path(path) {
        // A drive or UNC path is never joined onto the KB root, even where the OS would call it relative.
        let p = Path::new(path);
        if !p.is_absolute() {
            return None;
        }
        full_path(p)?
    } else {
        full_path(&root_full.join(path))?
    };
    // Case-insensitive, like the C# OrdinalIgnoreCase prefix check (Windows file systems).
    let root_s = root_full.to_string_lossy().to_lowercase();
    let cand_s = candidate.to_string_lossy().to_lowercase();
    let sep = std::path::MAIN_SEPARATOR;
    let prefix = if root_s.ends_with(sep) {
        root_s.clone()
    } else {
        format!("{root_s}{sep}")
    };
    if !cand_s.starts_with(&prefix) && cand_s != root_s {
        return None;
    }
    Some(candidate)
}

/// True when `path` resolves under `kb_root` (or equals it).
pub fn is_path_under_root(kb_root: &str, path: &str) -> bool {
    path_under_root(kb_root, path).is_some()
}

/// Resolve a KB document path that is allowed to open. Absolute paths outside the KB root are
/// refused; relative paths and basename lookups under the KB (and `manuals/`) are accepted when present.
pub fn resolve_document_path(kb_root: &str, local_path: &str, filename: &str) -> Option<PathBuf> {
    if let Some(under) = path_under_root(kb_root, local_path) {
        return Some(under);
    }
    let src = if filename.trim().is_empty() {
        local_path
    } else {
        filename
    };
    let name = file_name(src);
    if name.is_empty() {
        return None;
    }
    for rel in [
        name.to_string(),
        format!("manuals{}{}", std::path::MAIN_SEPARATOR, name),
    ] {
        if let Some(under) = path_under_root(kb_root, &rel) {
            if under.is_file() {
                return Some(under);
            }
        }
    }
    None
}

/// Last path segment, splitting on both separators so a recorded Windows path works on any OS.
pub(crate) fn file_name(path: &str) -> &str {
    path.rsplit(['/', '\\']).next().unwrap_or("")
}
