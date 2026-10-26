//! Unit history (port of `src/Features/Adapters/History.cs`): reads back the `*_dtcs.csv` files
//! earlier INLINE 7 sessions saved, so a unit that has been here before says so.

use crate::time::{parse_utc_ticks, round_trip_from_ticks};
use std::collections::HashMap;
use std::fs;
use std::path::Path;
use std::time::UNIX_EPOCH;

/// One fault row from a saved session CSV.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct HistoryEntry {
    /// 100 ns ticks since the Unix epoch, from the `time` column (else the file's write time).
    pub when: i64,
    pub job: String,
    pub state: String,
    pub spn: i64,
    pub fmi: i64,
    pub occ: i64,
    pub name: String,
    pub file: String,
}

/// One fault seen across past visits for a job.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct HistoryFault {
    pub spn: i64,
    pub fmi: i64,
    pub name: String,
    pub visits: usize,
    pub first: i64,
    pub last: i64,
}

/// `yyyy-MM-dd` of a tick count.
pub fn date_of(ticks: i64) -> String {
    round_trip_from_ticks(ticks.max(0) as u64)[..10].to_string()
}

impl std::fmt::Display for HistoryFault {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        let name = if self.name.is_empty() {
            String::new()
        } else {
            format!("  {}", self.name)
        };
        write!(
            f,
            "SPN {:<6} FMI {:<3} seen on {} visit{}   last {}{}",
            self.spn,
            self.fmi,
            self.visits,
            if self.visits == 1 { "" } else { "s" },
            date_of(self.last),
            name
        )
    }
}

#[derive(Debug, Clone, Default)]
pub struct History {
    pub entries: Vec<HistoryEntry>,
    pub files_read: usize,
    pub error: String,
}

/// Quote-aware split; job tags and fault names contain commas.
pub fn split_csv(line: &str) -> Vec<String> {
    let mut cells = Vec::new();
    let mut cur = String::new();
    let mut in_q = false;
    let mut it = line.chars().peekable();
    while let Some(c) = it.next() {
        if in_q {
            if c == '"' {
                if it.peek() == Some(&'"') {
                    cur.push('"');
                    it.next();
                } else {
                    in_q = false;
                }
            } else {
                cur.push(c);
            }
        } else if c == '"' {
            in_q = true;
        } else if c == ',' {
            cells.push(std::mem::take(&mut cur));
        } else {
            cur.push(c);
        }
    }
    cells.push(cur);
    cells
}

/// Loose match so "HP450 12345" finds sessions tagged "HP450  12345" or just the serial: every
/// whitespace-separated token of 3+ characters has to appear in the stored job tag.
pub fn job_matches(stored_job: &str, query: &str) -> bool {
    if query.trim().is_empty() {
        return true;
    }
    let j = stored_job.to_lowercase();
    let mut any = false;
    for tok in query
        .to_lowercase()
        .split([' ', '\t'])
        .filter(|t| !t.is_empty())
    {
        if tok.chars().count() < 3 {
            continue;
        }
        any = true;
        if !j.contains(tok) {
            return false;
        }
    }
    any
}

fn write_ticks(path: &Path) -> i64 {
    fs::metadata(path)
        .and_then(|m| m.modified())
        .ok()
        .and_then(|t| t.duration_since(UNIX_EPOCH).ok())
        .map(|d| (d.as_nanos() / 100) as i64)
        .unwrap_or(0)
}

impl History {
    /// Every `*_dtcs.csv` in `folder`, in name order. A missing folder reads nothing.
    pub fn load(folder: &Path) -> History {
        let mut h = History::default();
        let rd = match fs::read_dir(folder) {
            Ok(rd) => rd,
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => return h,
            Err(e) => {
                h.error = e.to_string();
                return h;
            }
        };
        let mut files: Vec<_> = rd
            .filter_map(Result::ok)
            .map(|e| e.path())
            .filter(|p| {
                p.is_file()
                    && p.file_name()
                        .map(|n| n.to_string_lossy().ends_with("_dtcs.csv"))
                        .unwrap_or(false)
            })
            .collect();
        files.sort();
        for f in files {
            if h.read_file(&f).is_ok() {
                h.files_read += 1;
            }
        }
        h
    }

    fn read_file(&mut self, path: &Path) -> std::io::Result<()> {
        let bytes = fs::read(path)?;
        let text = String::from_utf8_lossy(&bytes);
        let text = text.strip_prefix('\u{feff}').unwrap_or(&text);
        let lines: Vec<&str> = text.lines().collect();
        if lines.len() < 2 {
            return Ok(());
        }
        let header = split_csv(lines[0]);
        let col = |name: &str| header.iter().position(|h| h == name);
        let (i_spn, i_fmi) = match (col("spn"), col("fmi")) {
            (Some(s), Some(f)) => (s, f),
            _ => return Ok(()),
        };
        let (i_time, i_job, i_state, i_occ, i_name) = (
            col("time"),
            col("job"),
            col("state"),
            col("occurrences"),
            col("name"),
        );
        let file = path
            .file_name()
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_default();
        for line in &lines[1..] {
            if line.trim().is_empty() {
                continue;
            }
            let c = split_csv(line);
            let at = |i: Option<usize>| i.and_then(|i| c.get(i)).cloned().unwrap_or_default();
            let (Ok(spn), Ok(fmi)) = (
                at(Some(i_spn)).trim().parse::<i64>(),
                at(Some(i_fmi)).trim().parse::<i64>(),
            ) else {
                continue;
            };
            let when = parse_utc_ticks(&at(i_time)).unwrap_or_else(|| write_ticks(path));
            self.entries.push(HistoryEntry {
                when,
                job: at(i_job),
                state: at(i_state),
                spn,
                fmi,
                occ: at(i_occ).trim().parse().unwrap_or(0),
                name: at(i_name),
                file: file.clone(),
            });
        }
        Ok(())
    }

    /// Faults on file for this job: most visits first, then most recent.
    pub fn for_job(&self, job: &str) -> Vec<HistoryFault> {
        let mut order: Vec<(i64, i64)> = Vec::new();
        let mut by_key: HashMap<(i64, i64), (HistoryFault, Vec<&str>)> = HashMap::new();
        for e in self.entries.iter().filter(|e| job_matches(&e.job, job)) {
            let key = (e.spn, e.fmi);
            let (f, files) = by_key.entry(key).or_insert_with(|| {
                order.push(key);
                (
                    HistoryFault {
                        spn: e.spn,
                        fmi: e.fmi,
                        name: e.name.clone(),
                        visits: 0,
                        first: e.when,
                        last: e.when,
                    },
                    Vec::new(),
                )
            });
            if !e.name.is_empty() {
                f.name = e.name.clone();
            }
            f.first = f.first.min(e.when);
            f.last = f.last.max(e.when);
            if !files.contains(&e.file.as_str()) {
                files.push(&e.file);
            }
        }
        let mut list: Vec<HistoryFault> = order
            .into_iter()
            .map(|k| {
                let (mut f, files) = by_key.remove(&k).unwrap();
                f.visits = files.len();
                f
            })
            .collect();
        list.sort_by(|a, b| b.visits.cmp(&a.visits).then(b.last.cmp(&a.last)));
        list
    }

    /// Distinct job tags seen, most recent first.
    pub fn jobs(&self) -> Vec<String> {
        let mut last: Vec<(String, i64)> = Vec::new();
        for e in &self.entries {
            if e.job.trim().is_empty() {
                continue;
            }
            match last.iter_mut().find(|(j, _)| *j == e.job) {
                Some(slot) => slot.1 = slot.1.max(e.when),
                None => last.push((e.job.clone(), e.when)),
            }
        }
        last.sort_by_key(|(_, t)| std::cmp::Reverse(*t));
        last.into_iter().map(|(j, _)| j).collect()
    }

    /// The History panel text and the job report's UNIT HISTORY section.
    pub fn summary(&self, job: &str) -> String {
        let mut out = Vec::new();
        if !self.error.is_empty() {
            out.push(format!("(could not read session folder: {})", self.error));
        }
        if self.files_read == 0 {
            out.push("No saved sessions yet.".to_string());
            out.push(String::new());
            out.push(
                "Press Save on the INLINE 7 tab and this unit's faults become searchable history."
                    .to_string(),
            );
            return out.join("\n") + "\n";
        }
        if job.trim().is_empty() {
            out.push("Type a model/serial in the job strip to see that unit's history.".into());
            out.push(format!(
                "{} saved session file(s) indexed, {} job(s).",
                self.files_read,
                self.jobs().len()
            ));
            return out.join("\n") + "\n";
        }
        let faults = self.for_job(job);
        if faults.is_empty() {
            out.push(format!("Nothing on file for \"{job}\"."));
            out.push(format!("{} session file(s) indexed.", self.files_read));
            return out.join("\n") + "\n";
        }
        out.push(format!(
            "This unit has been here before — {} distinct fault(s) on file:",
            faults.len()
        ));
        out.push(String::new());
        for f in &faults {
            out.push(format!("  {f}"));
            if f.visits > 1 {
                out.push(format!(
                    "      repeat offender — first seen {}",
                    date_of(f.first)
                ));
            }
        }
        out.join("\n") + "\n"
    }
}
