//! Crash log and crash-screen text (the portable half of `src/Features/Crash/CrashForm.cs`).
//! Each front end writes the log where it can and shows the screen; the text is the same.

use std::fs::OpenOptions;
use std::io::{self, Write};
use std::path::{Path, PathBuf};

/// Same file name as the WinForms build.
pub const LOG_FILE_NAME: &str = "techbench-crash.log";

/// One crash record as `CrashForm.WriteCrashLog` appends it: a header line with the time and
/// where it happened, the error, then a blank line.
pub fn record(stamp: &str, where_: &str, details: &str) -> String {
    let details = if details.is_empty() {
        "(no exception object)"
    } else {
        details
    };
    format!("=== {stamp}  {where_} ===\n{details}\n\n")
}

/// Appends one record to `dir/techbench-crash.log` and returns the log's path.
pub fn append_log(dir: &Path, stamp: &str, where_: &str, details: &str) -> io::Result<PathBuf> {
    std::fs::create_dir_all(dir)?;
    let path = dir.join(LOG_FILE_NAME);
    OpenOptions::new()
        .create(true)
        .append(true)
        .open(&path)?
        .write_all(record(stamp, where_, details).as_bytes())?;
    Ok(path)
}

/// What Copy details puts on the clipboard (`CrashForm.BuildClipboardText`).
pub fn clipboard_text(where_: &str, details: &str, log_path: &str) -> String {
    let mut s = format!("Tech Bench crash\nWhere: {where_}\n");
    if !log_path.is_empty() {
        s.push_str(&format!("Log: {log_path}\n"));
    }
    s.push('\n');
    s.push_str(if details.is_empty() {
        "(no exception object)"
    } else {
        details
    });
    s.push('\n');
    s
}
