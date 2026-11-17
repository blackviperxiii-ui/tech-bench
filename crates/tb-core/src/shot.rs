//! The INLINE 7 Shot button's file (`SessionIo.Screenshot`): `shot_<stamp>.png` in the session
//! folder. The new app draws the picture itself, so this only checks it is a PNG and writes it.

use std::io;
use std::path::{Path, PathBuf};

/// The eight bytes every PNG file starts with.
pub const PNG_MAGIC: [u8; 8] = [0x89, b'P', b'N', b'G', 0x0D, 0x0A, 0x1A, 0x0A];

/// `shot_<stamp>.png`, the WinForms name (stamp is yyyyMMdd_HHmmss local time).
pub fn file_name(stamp: &str) -> String {
    format!("shot_{stamp}.png")
}

/// Writes `png` as `dir/shot_<stamp>.png`, creating `dir`. Refuses anything that is not a PNG
/// or a stamp that would step outside `dir`.
pub fn save(dir: &Path, stamp: &str, png: &[u8]) -> io::Result<PathBuf> {
    if !png.starts_with(&PNG_MAGIC) {
        return Err(io::Error::new(
            io::ErrorKind::InvalidData,
            "The shot is not a PNG picture.",
        ));
    }
    if stamp.is_empty() || !stamp.chars().all(|c| c.is_ascii_digit() || c == '_') {
        return Err(io::Error::new(
            io::ErrorKind::InvalidInput,
            "The shot needs a yyyyMMdd_HHmmss stamp.",
        ));
    }
    std::fs::create_dir_all(dir)?;
    let path = dir.join(file_name(stamp));
    std::fs::write(&path, png)?;
    Ok(path)
}
