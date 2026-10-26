//! One fault's life on this hookup (port of `src/Features/Adapters/Timeline.cs`). "Red Stop came
//! on" is a symptom; "5246 latched 4 s after crank at 180 RPM" is a diagnosis, and that needs the
//! transition recorded when it happens.

use super::decode::Dtc;
use super::trend::iso_ms;
use crate::fmt::fixed;
use std::fmt;

#[derive(Debug, Clone, PartialEq)]
pub struct DtcEvent {
    pub spn: i32,
    pub fmi: i32,
    pub name: String,
    pub fmi_text: String,
    pub occ: i32,
    pub first_seen: i64,
    pub last_seen: i64,
    pub rpm_at_first: f64,
    pub rpm_at_last: f64,
    pub active: bool,
    /// How many times it went inactive and came back (starts at 1).
    pub cycles: u32,
}

impl Default for DtcEvent {
    fn default() -> Self {
        Self {
            spn: 0,
            fmi: 0,
            name: String::new(),
            fmi_text: String::new(),
            occ: 0,
            first_seen: 0,
            last_seen: 0,
            rpm_at_first: f64::NAN,
            rpm_at_last: f64::NAN,
            active: false,
            cycles: 1,
        }
    }
}

impl DtcEvent {
    pub fn key(&self) -> (i32, i32) {
        (self.spn, self.fmi)
    }

    pub fn duration(&self) -> String {
        let ms = self.last_seen - self.first_seen;
        if ms < 1000 {
            return "<1s".to_string();
        }
        if ms < 60_000 {
            return format!("{}s", fixed(ms as f64 / 1000.0, 0));
        }
        format!("{}m{:02}s", ms / 60_000, (ms / 1000) % 60)
    }
}

impl fmt::Display for DtcEvent {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        let rpm = if self.rpm_at_first.is_nan() {
            "—".to_string()
        } else {
            fixed(self.rpm_at_first, 0)
        };
        let iso = iso_ms(self.first_seen);
        write!(
            f,
            "{}  {:<6} SPN {:<6} FMI {:<3} {:<5} @{:<5} RPM  {}{}",
            &iso[11..19],
            if self.active { "ON" } else { "cleared" },
            self.spn,
            self.fmi,
            self.duration(),
            rpm,
            if self.name.is_empty() {
                &self.fmi_text
            } else {
                &self.name
            },
            if self.cycles > 1 {
                format!("  (x{})", self.cycles)
            } else {
                String::new()
            }
        )
    }
}

/// DM1 comings and goings for one module.
#[derive(Debug, Clone, Default)]
pub struct DtcTimeline {
    order: Vec<DtcEvent>,
}

impl DtcTimeline {
    pub fn len(&self) -> usize {
        self.order.len()
    }

    pub fn is_empty(&self) -> bool {
        self.order.is_empty()
    }

    pub fn clear(&mut self) {
        self.order.clear();
    }

    /// Fold a fresh DM1 list in. Codes missing from `active` are marked cleared but kept, because
    /// "it was there and went away when I wiggled the connector" is the finding.
    pub fn update(&mut self, active: Option<&[Dtc]>, now: i64, rpm: f64) {
        let mut seen: Vec<(i32, i32)> = Vec::new();
        for d in active.unwrap_or(&[]) {
            let key = (d.spn, d.fmi);
            seen.push(key);
            if let Some(e) = self.order.iter_mut().find(|e| e.key() == key) {
                if !e.active {
                    e.active = true;
                    e.cycles += 1;
                    e.rpm_at_first = rpm;
                    e.first_seen = now;
                }
                e.last_seen = now;
                e.rpm_at_last = rpm;
                e.occ = d.occ;
            } else {
                self.order.push(DtcEvent {
                    spn: d.spn,
                    fmi: d.fmi,
                    name: d.name.clone(),
                    fmi_text: d.fmi_text.clone(),
                    occ: d.occ,
                    first_seen: now,
                    last_seen: now,
                    rpm_at_first: rpm,
                    rpm_at_last: rpm,
                    active: true,
                    cycles: 1,
                });
            }
        }
        for e in &mut self.order {
            if e.active && !seen.contains(&e.key()) {
                e.active = false;
            }
        }
    }

    /// Newest first: the last thing that changed is what you want to see.
    pub fn recent(&self) -> Vec<DtcEvent> {
        let mut list = self.order.clone();
        list.sort_by(|a, b| {
            b.last_seen
                .cmp(&a.last_seen)
                .then(b.first_seen.cmp(&a.first_seen))
        });
        list
    }

    pub fn in_order(&self) -> &[DtcEvent] {
        &self.order
    }

    pub fn csv(&self, job_tag: &str) -> String {
        let mut out = String::from(
            "first_seen,last_seen,job,spn,fmi,state,cycles,occurrences,rpm_at_first,rpm_at_last,name,fmi_text\r\n",
        );
        for e in &self.order {
            let rpm = |v: f64| {
                if v.is_nan() {
                    String::new()
                } else {
                    fixed(v, 0)
                }
            };
            let row = [
                iso_ms(e.first_seen),
                iso_ms(e.last_seen),
                csvq(job_tag),
                e.spn.to_string(),
                e.fmi.to_string(),
                if e.active { "active" } else { "cleared" }.to_string(),
                e.cycles.to_string(),
                e.occ.to_string(),
                rpm(e.rpm_at_first),
                rpm(e.rpm_at_last),
                csvq(&e.name),
                csvq(&e.fmi_text),
            ];
            out.push_str(&row.join(","));
            out.push_str("\r\n");
        }
        out
    }
}

fn csvq(s: &str) -> String {
    format!("\"{}\"", s.replace('"', "\"\""))
}
