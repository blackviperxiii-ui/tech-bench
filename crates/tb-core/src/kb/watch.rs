//! Which shop folders to watch for changes (port of `src/Features/Sync/ShopWatch.cs`), and a
//! std-only snapshot of them so a shell can poll for changes without a platform file watcher.

use super::shop::SyncSettings;
use super::sync::is_shared_tree;
use std::collections::BTreeMap;
use std::fs;
use std::path::{Path, PathBuf};
use std::time::SystemTime;

/// `ShopWatch.Dirs`: the KB's `data/shop`, `data/notes` and `data/files`, then the sync folder
/// when one is set, is not the KB itself and exists. Creates nothing.
pub fn watch_dirs(kb_root: &str, settings: Option<&SyncSettings>) -> Vec<PathBuf> {
    if kb_root.is_empty() {
        return Vec::new();
    }
    let data = Path::new(kb_root).join("data");
    let mut dirs = vec![data.join("shop"), data.join("notes"), data.join("files")];
    if let Some(s) = settings {
        if !is_shared_tree(kb_root, s) && Path::new(&s.sync_folder).is_dir() {
            dirs.push(PathBuf::from(&s.sync_folder));
        }
    }
    dirs
}

/// Every file and folder under the watched folders with its size and modified time. Two equal
/// snapshots mean nothing a sync cares about changed in between.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Snapshot(BTreeMap<PathBuf, (u64, Option<SystemTime>)>);

impl Snapshot {
    /// Missing folders are simply empty.
    pub fn take(dirs: &[PathBuf]) -> Snapshot {
        let mut map = BTreeMap::new();
        for d in dirs {
            walk(d, &mut map);
        }
        Snapshot(map)
    }

    pub fn len(&self) -> usize {
        self.0.len()
    }

    pub fn is_empty(&self) -> bool {
        self.0.is_empty()
    }
}

fn walk(dir: &Path, map: &mut BTreeMap<PathBuf, (u64, Option<SystemTime>)>) {
    let Ok(rd) = fs::read_dir(dir) else {
        return;
    };
    for e in rd.filter_map(Result::ok) {
        let Ok(meta) = e.metadata() else {
            continue;
        };
        let path = e.path();
        if meta.is_dir() {
            map.insert(path.clone(), (0, None));
            walk(&path, map);
        } else {
            map.insert(path, (meta.len(), meta.modified().ok()));
        }
    }
}
