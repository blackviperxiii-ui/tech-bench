//! The job report (`JobReport.cs`) and the saved-session files (`Inline7Control.SaveSessionCore`
//! plus `SessionIo.BaseName`). Text only, so it is tested without a printer or a disk. Local
//! clock strings come from the caller: tb-core has no time zone.

use super::monitor::{fmt_reading, BusSnapshot, SaRow};
use super::timeline::DtcEvent;
use super::{Dtc, Names};
use crate::fmt::fixed;

const NL: &str = "\r\n";

/// What one report shows; `when` is local time as `yyyy-MM-dd HH:mm`.
#[derive(Clone, Debug)]
pub struct ReportData {
    pub when: String,
    pub job_tag: String,
    pub adapter: String,
    pub vin: String,
    pub sw: String,
    pub comp_id: String,
    pub hours: String,
    pub rpm: f64,
    pub red: bool,
    pub amber: bool,
    pub protect: bool,
    pub mil: bool,
    pub def: String,
    pub coolant: String,
    pub oil: String,
    pub batt: String,
    pub fuel: String,
    pub active: Vec<Dtc>,
    pub prev: Vec<Dtc>,
    pub timeline: Vec<DtcEvent>,
    pub markers: Vec<String>,
    pub diff: String,
    pub history_note: String,
    /// The last DM11/DM3 clear report from this hookup, if one was run.
    pub code_clear: String,
}

impl Default for ReportData {
    fn default() -> Self {
        let dash = || "—".to_string();
        Self {
            when: String::new(),
            job_tag: String::new(),
            adapter: String::new(),
            vin: String::new(),
            sw: String::new(),
            comp_id: String::new(),
            hours: String::new(),
            rpm: f64::NAN,
            red: false,
            amber: false,
            protect: false,
            mil: false,
            def: dash(),
            coolant: dash(),
            oil: dash(),
            batt: dash(),
            fuel: dash(),
            active: Vec::new(),
            prev: Vec::new(),
            timeline: Vec::new(),
            markers: Vec::new(),
            diff: String::new(),
            history_note: String::new(),
            code_clear: String::new(),
        }
    }
}

impl ReportData {
    /// `Inline7Control.BuildReportData` from a bus snapshot and the viewed module's timeline.
    pub fn from_snapshot(
        s: &BusSnapshot,
        timeline: Vec<DtcEvent>,
        job_tag: &str,
        when: &str,
    ) -> Self {
        Self {
            when: when.to_string(),
            job_tag: job_tag.to_string(),
            adapter: s.adapter_name.clone(),
            vin: s.vin.clone(),
            sw: s.sw.clone(),
            comp_id: s.comp_id.clone(),
            hours: s.hours.clone(),
            rpm: s.rpm,
            red: s.red,
            amber: s.amber,
            protect: s.protect,
            mil: s.mil,
            def: s.def_text.clone(),
            coolant: fmt_reading(s.coolant_c, " C", 0),
            oil: s.oil_text.clone(),
            batt: fmt_reading(s.battery_v, " V", 2),
            fuel: fmt_reading(s.fuel_lph, " L/h", 2),
            active: s.active.clone(),
            prev: s.prev.clone(),
            timeline,
            markers: Vec::new(),
            diff: String::new(),
            history_note: String::new(),
            code_clear: s.last_reset_report.clone(),
        }
    }
}

fn on_off(v: bool) -> &'static str {
    if v {
        "ON"
    } else {
        "off"
    }
}

fn or_unpublished(s: &str) -> String {
    if s.trim().is_empty() {
        "(not published)".to_string()
    } else {
        s.to_string()
    }
}

fn rpm_text(rpm: f64) -> String {
    if rpm.is_nan() {
        "—".to_string()
    } else {
        fixed(rpm, 0)
    }
}

/// Non-blank lines of a multi-line block, indented two spaces.
fn block(out: &mut Vec<String>, text: &str) {
    for line in text.replace("\r\n", "\n").split('\n') {
        if !line.trim().is_empty() {
            out.push(format!("  {}", line.trim_end()));
        }
    }
}

/// The one-page summary a tech staples to a work order, line by line.
pub fn lines(d: &ReportData) -> Vec<String> {
    let mut l = vec![
        "TECH BENCH — DIAGNOSTIC REPORT".to_string(),
        "=".repeat(64),
        format!(
            "Job:      {}",
            if d.job_tag.trim().is_empty() {
                "(not entered)"
            } else {
                &d.job_tag
            }
        ),
        format!("Date:     {}", d.when),
    ];
    if !d.adapter.is_empty() {
        l.push(format!("Adapter:  {}", d.adapter));
    }
    l.push(String::new());

    l.push("ENGINE IDENTITY".into());
    l.push(format!("  VIN / NAME:  {}", or_unpublished(&d.vin)));
    l.push(format!("  Software:    {}", or_unpublished(&d.sw)));
    l.push(format!("  Component:   {}", or_unpublished(&d.comp_id)));
    l.push(format!("  Engine hours: {}", or_unpublished(&d.hours)));
    l.push(String::new());

    l.push("READINGS AT REPORT TIME".into());
    l.push(format!("  RPM:      {}", rpm_text(d.rpm)));
    l.push(format!(
        "  Lamps:    Red Stop {}   Amber {}   Protect {}   MIL {}",
        on_off(d.red),
        on_off(d.amber),
        on_off(d.protect),
        on_off(d.mil)
    ));
    l.push(format!("  DEF:      {}", d.def));
    l.push(format!("  Coolant:  {}     Oil: {}", d.coolant, d.oil));
    l.push(format!("  Battery:  {}     Fuel rate: {}", d.batt, d.fuel));
    l.push(String::new());

    for (title, list) in [
        ("ACTIVE FAULTS (DM1)", &d.active),
        ("PREVIOUSLY ACTIVE (DM2)", &d.prev),
    ] {
        l.push(title.into());
        if list.is_empty() {
            l.push("  (none)".into());
        }
        l.extend(list.iter().map(|t| format!("  {t}")));
        l.push(String::new());
    }

    if !d.timeline.is_empty() {
        l.push("FAULT TIMELINE THIS HOOKUP".into());
        for (n, e) in d.timeline.iter().enumerate() {
            l.push(format!("  {e}"));
            if n + 1 >= 20 && d.timeline.len() > 20 {
                l.push(format!("  … {} more", d.timeline.len() - 20));
                break;
            }
        }
        l.push(String::new());
    }

    if !d.markers.is_empty() {
        l.push("MARKERS".into());
        l.extend(d.markers.iter().map(|m| format!("  {m}")));
        l.push(String::new());
    }

    if !d.code_clear.trim().is_empty() {
        l.push("LAST CODE CLEAR".into());
        block(&mut l, &d.code_clear);
        l.push(String::new());
    }

    if !d.diff.trim().is_empty() && !d.diff.contains("Need Snapshot") {
        l.push("BEFORE / AFTER".into());
        block(&mut l, &d.diff);
        l.push(String::new());
    }

    if !d.history_note.trim().is_empty() {
        l.push("UNIT HISTORY".into());
        block(&mut l, &d.history_note);
        l.push(String::new());
    }

    l.push("-".repeat(64));
    l.push("Red Stop and Amber are DM1 lamp bits, not separate codes. They go out when the".into());
    l.push("faults that set them are gone. No DEF/SCR or engine-protection function was".into());
    l.push("disabled to produce this report.".into());
    l.push(String::new());
    l.push("Tech: ______________________    Signature: ______________________".into());
    l
}

/// The report as text, CRLF after every line (`JobReport.Text`).
pub fn text(d: &ReportData) -> String {
    lines(d).iter().map(|l| format!("{l}{NL}")).collect()
}

/// `SessionIo.BaseName`: the job tag with every non letter/digit as `_`, outer `_` trimmed, cut
/// to 40 characters, then `session_` and the local `yyyyMMdd_HHmmss` stamp.
pub fn base_name(job_tag: &str, stamp: &str) -> String {
    let base = format!("session_{stamp}");
    let tag: String = job_tag
        .trim()
        .chars()
        .map(|c| if c.is_alphanumeric() { c } else { '_' })
        .collect();
    let tag: String = tag.trim_matches('_').chars().take(40).collect();
    if tag.is_empty() {
        base
    } else {
        format!("{tag}_{base}")
    }
}

/// Local clock for one save: `yyyy-MM-dd HH:mm:ss`, `yyyyMMdd_HHmmss` and ISO 8601 with offset
/// (.NET round-trip "o").
#[derive(Clone, Debug, Default)]
pub struct LocalNow {
    pub local: String,
    pub stamp: String,
    pub iso: String,
}

/// The files one Save writes, next to each other under [`base_name`].
#[derive(Clone, Debug, Default, PartialEq)]
pub struct SessionFiles {
    pub base: String,
    pub body: String,
    pub readings_csv: String,
    pub dtc_csv: String,
    pub trend_csv: String,
    pub timeline_csv: String,
}

fn quote(s: &str) -> String {
    format!("\"{}\"", s.replace('"', "\"\""))
}

/// .NET `bool.ToString()`.
fn net_bool(v: bool) -> &'static str {
    if v {
        "True"
    } else {
        "False"
    }
}

/// Everything `SaveSessionCore` writes. `timeline` is the viewed module's recent events,
/// `timeline_csv` and `trend_csv` come from the monitor as they are.
#[allow(clippy::too_many_arguments)]
pub fn session_files(
    s: &BusSnapshot,
    sas: &[SaRow],
    names: &Names,
    timeline: &[DtcEvent],
    trend_csv: String,
    timeline_csv: String,
    job_tag: &str,
    now: &LocalNow,
) -> SessionFiles {
    let mut b: Vec<String> = Vec::new();
    if !job_tag.trim().is_empty() {
        b.push(format!("Job  {job_tag}"));
    }
    b.push(format!("Tech Bench session  {}", now.local));
    b.push(format!(
        "Adapter {}  device {}  {}  connected={}",
        s.adapter_name,
        s.device_id,
        s.protocol,
        net_bool(s.connected)
    ));
    b.push(format!(
        "Engine SA {}  showing SA {}",
        s.engine_sa, s.view_sa
    ));
    b.push(format!(
        "RPM {}  Red Stop {}  Amber {}  Protect {}  MIL {}",
        rpm_text(s.rpm),
        on_off(s.red),
        on_off(s.amber),
        on_off(s.protect),
        on_off(s.mil)
    ));
    b.push(format!("DEF {}", s.def_text));
    let coolant = fmt_reading(s.coolant_c, " C", 0);
    let battery = fmt_reading(s.battery_v, " V", 2);
    let fuel = fmt_reading(s.fuel_lph, " L/h", 2);
    b.push(format!(
        "Coolant {coolant}  Oil {}  Battery {battery}  Fuel {fuel}",
        s.oil_text
    ));
    b.push(format!("VIN {}  SW {}", s.vin, s.sw));
    b.push(format!("Component {}  Hours {}", s.comp_id, s.hours));
    b.push(String::new());
    for (title, list) in [("Active DTCs:", &s.active), ("Previous DTCs:", &s.prev)] {
        b.push(title.into());
        if list.is_empty() {
            b.push("  (none)".into());
        }
        b.extend(list.iter().map(|d| format!("  {d}")));
    }
    b.push(String::new());
    b.push("Fault timeline:".into());
    if timeline.is_empty() {
        b.push("  (none)".into());
    }
    b.extend(timeline.iter().map(|e| format!("  {e}")));
    b.push(String::new());
    // Markers are not offered in this app yet.
    b.push("Markers:".into());
    b.push("  (none)".into());
    if !s.last_reset_report.is_empty() {
        b.push(String::new());
        b.push("Last code clear:".into());
        block(&mut b, &s.last_reset_report);
    }
    b.push(String::new());
    b.push("Modules:".into());
    let mut sas: Vec<&SaRow> = sas.iter().collect();
    sas.sort_by_key(|r| r.sa);
    b.extend(
        sas.iter()
            .map(|r| format!("  SA {} {} x{}", r.sa, names.sa(r.sa), r.count)),
    );

    let readings = format!(
        "time,job,rpm,red,amber,protect,mil,def,coolant,oil,battery,fuel,active_count,previous_count{NL}{},{},{},{},{},{},{},{},{},{},{},{},{},{}{NL}",
        now.iso,
        quote(job_tag),
        if s.rpm.is_nan() { String::new() } else { fixed(s.rpm, 0) },
        net_bool(s.red),
        net_bool(s.amber),
        net_bool(s.protect),
        net_bool(s.mil),
        quote(&s.def_text),
        quote(&coolant),
        quote(&s.oil_text),
        quote(&battery),
        quote(&fuel),
        s.active.len(),
        s.prev.len()
    );

    let mut dtc = format!("time,job,state,spn,fmi,occurrences,name,fmi_text{NL}");
    for (state, list) in [("active", &s.active), ("previous", &s.prev)] {
        for d in list.iter() {
            dtc.push_str(&format!(
                "{},{},{state},{},{},{},{},{}{NL}",
                now.iso,
                quote(job_tag),
                d.spn,
                d.fmi,
                d.occ,
                quote(&d.name),
                quote(&d.fmi_text)
            ));
        }
    }

    SessionFiles {
        base: base_name(job_tag, &now.stamp),
        body: b.iter().map(|l| format!("{l}{NL}")).collect(),
        readings_csv: readings,
        dtc_csv: dtc,
        trend_csv,
        timeline_csv,
    }
}
