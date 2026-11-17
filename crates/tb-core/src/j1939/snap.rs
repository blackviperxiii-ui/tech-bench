//! Snapshot A / B and Diff on the INLINE 7 Bus tab (`Snap` and `SessionIo.Diff` in
//! `src/Core/J1939/SessionData.cs`, `TakeSnap` in `Inline7Control.cs`): readings and codes
//! before and after a repair, and what changed.

use super::monitor::{fmt_reading, BusSnapshot};

const NL: &str = "\r\n";

/// One snapshot. `rpm` is -1 when the engine was not reporting it.
#[derive(Clone, Debug, PartialEq)]
pub struct Snap {
    /// Local time of day, `HH:mm:ss`.
    pub time: String,
    pub label: String,
    pub rpm: f64,
    pub red: bool,
    pub amber: bool,
    pub def: String,
    pub coolant: String,
    pub oil: String,
    pub batt: String,
    pub fuel: String,
    pub active: Vec<String>,
    pub prev: Vec<String>,
}

impl Snap {
    /// `TakeSnap`: the readings and code lists of `s` at `time`.
    pub fn take(s: &BusSnapshot, time: &str, label: &str) -> Snap {
        Snap {
            time: time.to_string(),
            label: label.to_string(),
            rpm: if s.rpm.is_nan() { -1.0 } else { s.rpm },
            red: s.red,
            amber: s.amber,
            def: s.def_text.clone(),
            coolant: fmt_reading(s.coolant_c, " C", 0),
            oil: s.oil_text.clone(),
            batt: fmt_reading(s.battery_v, " V", 2),
            fuel: fmt_reading(s.fuel_lph, " L/h", 2),
            active: s.active.iter().map(ToString::to_string).collect(),
            prev: s.prev.iter().map(ToString::to_string).collect(),
        }
    }
}

fn fmt_rpm(r: f64) -> String {
    if r < 0.0 {
        "—".to_string()
    } else {
        format!("{r:.0}")
    }
}

fn on_off(v: bool) -> &'static str {
    if v {
        "ON"
    } else {
        "off"
    }
}

fn missing(out: &mut String, have: &[String], other: &[String]) {
    let mut n = 0;
    for s in have.iter().filter(|s| !other.contains(s)) {
        out.push_str(&format!("  {s}{NL}"));
        n += 1;
    }
    if n == 0 {
        out.push_str(&format!("  (none){NL}"));
    }
}

/// `SessionIo.Diff`: what changed between Snapshot A and Snapshot B.
pub fn diff(a: Option<&Snap>, b: Option<&Snap>) -> String {
    let (Some(a), Some(b)) = (a, b) else {
        return "Need Snapshot A and Snapshot B first.".to_string();
    };
    let mut s = String::new();
    let mut line = |t: String| {
        s.push_str(&t);
        s.push_str(NL);
    };
    line(format!("BEFORE  {}  ({})", a.time, a.label));
    line(format!("AFTER   {}  ({})", b.time, b.label));
    line(String::new());
    line(format!("RPM     {}  →  {}", fmt_rpm(a.rpm), fmt_rpm(b.rpm)));
    line(format!("Red     {}  →  {}", on_off(a.red), on_off(b.red)));
    line(format!(
        "Amber   {}  →  {}",
        on_off(a.amber),
        on_off(b.amber)
    ));
    line(format!("DEF     {}  →  {}", a.def, b.def));
    line(String::new());
    line("Active codes gone (in A, not B):".to_string());
    missing(&mut s, &a.active, &b.active);
    s.push_str(&format!("Active codes new (in B, not A):{NL}"));
    missing(&mut s, &b.active, &a.active);
    s.push_str(&format!("Unchanged active:{NL}"));
    let same: Vec<&String> = a.active.iter().filter(|x| b.active.contains(x)).collect();
    for x in &same {
        s.push_str(&format!("  {x}{NL}"));
    }
    if same.is_empty() {
        s.push_str(&format!("  (none){NL}"));
    }
    // A repair shows up in previously-active: a fault that stopped recurring moves there.
    s.push_str(NL);
    s.push_str(&format!("Previously-active gone (in A, not B):{NL}"));
    missing(&mut s, &a.prev, &b.prev);
    s.push_str(&format!("Previously-active new (in B, not A):{NL}"));
    missing(&mut s, &b.prev, &a.prev);
    s
}
