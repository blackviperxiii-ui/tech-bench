//! Atomic text write (port of `WorkOrderStore.AtomicWriteText`): flush a sibling temp file,
//! keep the previous version as `path.bak`, then move the temp into place. A failed write
//! leaves the old file (or its `.bak`) and no temp behind.

use std::fs;
use std::io::{self, Write};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};
use std::time::{SystemTime, UNIX_EPOCH};

static SEQ: AtomicU64 = AtomicU64::new(0);

fn temp_sibling(path: &Path) -> PathBuf {
    let nanos = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_nanos())
        .unwrap_or(0);
    let n = SEQ.fetch_add(1, Ordering::Relaxed);
    let mut name = path
        .file_name()
        .map(|s| s.to_os_string())
        .unwrap_or_default();
    name.push(format!(".tmp-{:x}{:x}{n:x}", std::process::id(), nanos));
    path.with_file_name(name)
}

pub fn bak_path(path: &Path) -> PathBuf {
    let mut name = path
        .file_name()
        .map(|s| s.to_os_string())
        .unwrap_or_default();
    name.push(".bak");
    path.with_file_name(name)
}

/// Write `text` as UTF-8 (no BOM) to `path` atomically, keeping `path.bak` when replacing.
pub fn atomic_write_text(path: &Path, text: &str) -> io::Result<()> {
    atomic_write_bytes(path, text.as_bytes())
}

/// Like [`atomic_write_text`] with a UTF-8 BOM first, as C# `Encoding.UTF8` writes
/// (work-order `notes.txt` / `report.txt`).
pub fn atomic_write_text_bom(path: &Path, text: &str) -> io::Result<()> {
    let mut bytes = Vec::with_capacity(text.len() + 3);
    bytes.extend_from_slice(&[0xEF, 0xBB, 0xBF]);
    bytes.extend_from_slice(text.as_bytes());
    atomic_write_bytes(path, &bytes)
}

/// Flush `text` to a sibling `path.tmp-*` and return that path without replacing `path`:
/// what a crash between the two steps leaves behind (the SelfTest crash seam).
pub fn write_atomic_temp(path: &Path, text: &str) -> io::Result<PathBuf> {
    let temp = temp_sibling(path);
    let mut f = fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&temp)?;
    f.write_all(text.as_bytes())?;
    f.sync_all()?;
    Ok(temp)
}

/// Copy `src` onto `dest` through a sibling temp file, with no `.bak`
/// (port of `WorkOrderStore.AtomicCopyFile`).
pub fn atomic_copy_file(src: &Path, dest: &Path) -> io::Result<()> {
    if let Some(dir) = dest.parent().filter(|d| !d.as_os_str().is_empty()) {
        fs::create_dir_all(dir)?;
    }
    let temp = temp_sibling(dest);
    let result = fs::copy(src, &temp).and_then(|_| fs::rename(&temp, dest));
    if result.is_err() {
        let _ = fs::remove_file(&temp);
    }
    result
}

fn atomic_write_bytes(path: &Path, bytes: &[u8]) -> io::Result<()> {
    if path.as_os_str().is_empty() {
        return Err(io::Error::new(
            io::ErrorKind::InvalidInput,
            "path is required",
        ));
    }
    if let Some(dir) = path.parent().filter(|d| !d.as_os_str().is_empty()) {
        fs::create_dir_all(dir)?;
    }
    let temp = temp_sibling(path);
    let result = (|| {
        let mut f = fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&temp)?;
        f.write_all(bytes)?;
        f.sync_all()?;
        drop(f);
        if path.exists() {
            fs::copy(path, bak_path(path))?;
        }
        // rename replaces the target in one step on Windows and POSIX.
        fs::rename(&temp, path)
    })();
    if result.is_err() {
        let _ = fs::remove_file(&temp);
    }
    result
}

/// Read a text file like C# `File.ReadAllText`: a leading UTF-8 BOM is dropped.
pub fn read_text(path: &Path) -> io::Result<String> {
    let text = fs::read_to_string(path)?;
    Ok(match text.strip_prefix('\u{feff}') {
        Some(rest) => rest.to_string(),
        None => text,
    })
}
