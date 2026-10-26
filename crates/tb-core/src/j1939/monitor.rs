//! Frame routing and decoded bus state (port of `src/Features/Adapters/BusMonitor.cs`). Free of
//! threads and UI so the whole decode path runs offline in tests. Times are milliseconds since the
//! Unix epoch; [`NEVER`] stands in for the C# `DateTime.MinValue`.

use super::bam::{BamAssembler, J1939Frame};
use super::clear::{try_parse_ack, AckReply, ACK};
use super::decode::{self, normalize_pgn, AftState, Dtc};
use super::names::Names;
use super::timeline::DtcTimeline;
use super::trend::{has, TrendLog, TrendSample, NEVER};
use crate::fmt::fixed;
use std::collections::BTreeMap;

/// Everything one module on the bus has told us.
#[derive(Debug, Clone)]
pub struct ModuleFaults {
    pub sa: i32,
    pub active: Vec<Dtc>,
    pub prev: Vec<Dtc>,
    pub red: bool,
    pub amber: bool,
    pub protect: bool,
    pub mil: bool,
    pub last_dm1: i64,
    pub last_dm2: i64,
    pub timeline: DtcTimeline,
}

impl ModuleFaults {
    pub fn new(sa: i32) -> Self {
        Self {
            sa,
            active: Vec::new(),
            prev: Vec::new(),
            red: false,
            amber: false,
            protect: false,
            mil: false,
            last_dm1: NEVER,
            last_dm2: NEVER,
            timeline: DtcTimeline::default(),
        }
    }

    pub fn has_dm1(&self) -> bool {
        self.last_dm1 != NEVER
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PgnRow {
    pub pgn: i32,
    pub sa: i32,
    pub count: u64,
    pub last: i64,
    pub last_len: usize,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SaRow {
    pub sa: i32,
    pub count: u64,
    pub last: i64,
    pub name_hex: String,
    pub claimed: bool,
}

/// Follow whoever broadcasts EEC1.
pub const AUTO_ENGINE_SA: i32 = -1;
const MAX_ACKS: usize = 64;
/// A non-engine FE56 is only used after the engine has been quiet on FE56 this long.
const DEF_FALLBACK_MS: i64 = 3000;

#[derive(Debug)]
pub struct BusMonitor {
    bam: BamAssembler,
    pgns: BTreeMap<(i32, i32), PgnRow>,
    sas: BTreeMap<i32, SaRow>,
    modules: BTreeMap<i32, ModuleFaults>,
    acks: Vec<AckReply>,
    trend: TrendLog,
    engine_sa_setting: i32,
    engine_sa: i32,
    last_engine_def: i64,
    pub names: Names,

    pub rpm: f64,
    pub coolant_c: f64,
    pub oil_kpa: f64,
    pub battery_v: f64,
    pub fuel_lph: f64,
    pub def_pct: f64,
    pub def_temp_c: f64,
    pub def_silent: bool,
    pub aft: AftState,
    /// Who the DEF/SCR readout came from: the engine, or the module that sends FE56.
    pub def_sa: i32,
    pub last_def: i64,
    pub vin: String,
    pub sw: String,
    pub comp_id: String,
    pub hours_text: String,
    pub last_frame: i64,
    pub frames: u64,
}

impl Default for BusMonitor {
    fn default() -> Self {
        Self::new()
    }
}

impl BusMonitor {
    pub fn new() -> Self {
        Self {
            bam: BamAssembler::new(),
            pgns: BTreeMap::new(),
            sas: BTreeMap::new(),
            modules: BTreeMap::new(),
            acks: Vec::new(),
            trend: TrendLog::default(),
            engine_sa_setting: AUTO_ENGINE_SA,
            engine_sa: 0,
            last_engine_def: NEVER,
            names: Names::new(),
            rpm: f64::NAN,
            coolant_c: f64::NAN,
            oil_kpa: f64::NAN,
            battery_v: f64::NAN,
            fuel_lph: f64::NAN,
            def_pct: f64::NAN,
            def_temp_c: f64::NAN,
            def_silent: true,
            aft: AftState::default(),
            def_sa: -1,
            last_def: NEVER,
            vin: String::new(),
            sw: String::new(),
            comp_id: String::new(),
            hours_text: String::new(),
            last_frame: NEVER,
            frames: 0,
        }
    }

    pub fn trend(&self) -> &TrendLog {
        &self.trend
    }

    pub fn engine_sa_setting(&self) -> i32 {
        self.engine_sa_setting
    }

    /// Which SA the engine readouts come from; [`AUTO_ENGINE_SA`] follows whoever sends EEC1.
    pub fn set_engine_sa_setting(&mut self, value: i32) {
        self.engine_sa_setting = value;
        if value >= 0 {
            self.engine_sa = value;
        }
    }

    pub fn engine_sa(&self) -> i32 {
        self.engine_sa
    }

    /// The engine module, created if it has not said anything yet.
    pub fn engine(&mut self) -> &ModuleFaults {
        let sa = self.engine_sa;
        self.module(sa)
    }

    /// The module if it has said anything; unlike [`Self::module`] this never creates one.
    pub fn peek(&self, sa: i32) -> Option<&ModuleFaults> {
        self.modules.get(&sa)
    }

    pub fn module(&mut self, sa: i32) -> &mut ModuleFaults {
        self.modules
            .entry(sa)
            .or_insert_with(|| ModuleFaults::new(sa))
    }

    pub fn modules_with_faults(&self) -> Vec<i32> {
        self.modules
            .values()
            .filter(|m| m.has_dm1())
            .map(|m| m.sa)
            .collect()
    }

    /// Modules that have sent DM1 or DM2: the ones a code clear asks directly.
    pub fn diagnostic_modules(&self) -> Vec<i32> {
        self.modules
            .values()
            .filter(|m| m.has_dm1() || m.last_dm2 != NEVER)
            .map(|m| m.sa)
            .collect()
    }

    /// Has this source address sent anything at or after `since`?
    pub fn seen_since(&self, sa: i32, since: i64) -> bool {
        self.sas.get(&sa).is_some_and(|r| r.last >= since)
    }

    /// Latest Acknowledgment from `sa` for `pgn` addressed to this tool, at or after `since`.
    pub fn ack_from(&self, sa: i32, pgn: i32, since: i64) -> Option<&AckReply> {
        for a in self.acks.iter().rev() {
            if a.time < since {
                break;
            }
            if a.sa == sa && a.pgn == pgn && a.for_tool() {
                return Some(a);
            }
        }
        None
    }

    /// A module ACKed a clear but has not sent a fresh DM1/DM2 since. Some controllers only
    /// broadcast DM1 while they have a fault, so the old list would otherwise stay up forever. The
    /// next DM1/DM2 puts back anything that is really still there. Lamps stay until a fresh DM1.
    pub fn mark_cleared(&mut self, sa: i32, active: bool, previous: bool, now: i64) {
        let rpm = if sa == self.engine_sa {
            self.rpm
        } else {
            f64::NAN
        };
        let Some(m) = self.modules.get_mut(&sa) else {
            return;
        };
        if active {
            m.active.clear();
            m.timeline.update(Some(&m.active), now, rpm);
        }
        if previous {
            m.prev.clear();
        }
    }

    pub fn clear_live(&mut self) {
        self.rpm = f64::NAN;
        self.coolant_c = f64::NAN;
        self.oil_kpa = f64::NAN;
        self.battery_v = f64::NAN;
        self.fuel_lph = f64::NAN;
        self.def_pct = f64::NAN;
        self.def_temp_c = f64::NAN;
        self.def_silent = true;
        self.aft = AftState::default();
        self.def_sa = -1;
        self.last_def = NEVER;
        self.last_engine_def = NEVER;
        self.acks.clear();
        self.vin.clear();
        self.sw.clear();
        self.comp_id.clear();
        self.hours_text.clear();
        self.modules.clear();
        self.last_frame = NEVER;
        self.engine_sa = self.engine_sa_setting.max(0);
    }

    pub fn clear_bus_stats(&mut self) {
        self.pgns.clear();
        self.sas.clear();
    }

    pub fn clear_history(&mut self) {
        self.trend.clear();
        for m in self.modules.values_mut() {
            m.timeline.clear();
        }
    }

    /// Feed one raw frame. True when a DM1 or DM2 for any module was updated.
    pub fn feed(&mut self, raw: &J1939Frame, now: i64) -> bool {
        let mut raw = raw.clone();
        raw.pgn = normalize_pgn(raw.pgn);
        self.frames += 1;
        self.last_frame = now;
        self.track_bus(&raw, now);
        match self.bam.feed(&raw, now.max(0) as u64) {
            Some(f) => self.route(&f, now),
            None => false,
        }
    }

    fn route(&mut self, f: &J1939Frame, now: i64) -> bool {
        // EEC1 defines the engine when auto-detecting: whoever broadcasts RPM is it.
        if f.pgn == 0xF004 {
            if self.engine_sa_setting == AUTO_ENGINE_SA && self.engine_sa != f.sa {
                self.engine_sa = f.sa;
            }
            if f.sa == self.engine_sa {
                self.rpm = nan_if_negative(decode::rpm(&f.data));
            }
            return false;
        }
        let engine = f.sa == self.engine_sa;
        let d = &f.data;
        match f.pgn {
            0xFE56 => {
                if self.accept_def(f.sa, engine, now) {
                    self.def_silent = d.len() < 2 || d[0] >= 0xFB;
                    self.def_pct = if !d.is_empty() && d[0] < 0xFB {
                        f64::from(d[0]) * 0.4
                    } else {
                        f64::NAN
                    };
                    self.def_temp_c = if d.len() > 1 && d[1] < 0xFB {
                        f64::from(d[1]) - 40.0
                    } else {
                        f64::NAN
                    };
                    self.aft = decode::parse_aftertreatment(Some(d));
                    self.def_sa = f.sa;
                    self.last_def = now;
                }
                false
            }
            ACK => {
                if let Some(ack) = try_parse_ack(f, now) {
                    if self.acks.len() >= MAX_ACKS {
                        self.acks.remove(0);
                    }
                    self.acks.push(ack);
                }
                false
            }
            0xFEEE => {
                if engine && !d.is_empty() {
                    self.coolant_c = if d[0] < 0xFB {
                        f64::from(d[0]) - 40.0
                    } else {
                        f64::NAN
                    };
                }
                false
            }
            0xFEEF => {
                if engine && d.len() > 3 {
                    self.oil_kpa = if d[3] < 0xFB {
                        f64::from(d[3]) * 4.0
                    } else {
                        f64::NAN
                    };
                }
                false
            }
            0xFEF7 => {
                if engine && d.len() >= 6 {
                    let raw = decode::u16_le(d, 4);
                    self.battery_v = if raw < 0xFB00 {
                        f64::from(raw) * 0.05
                    } else {
                        f64::NAN
                    };
                }
                false
            }
            0xFEF2 => {
                if engine && d.len() >= 2 {
                    let raw = decode::u16_le(d, 0);
                    self.fuel_lph = if raw < 0xFB00 {
                        f64::from(raw) * 0.05
                    } else {
                        f64::NAN
                    };
                }
                false
            }
            0xFEEC | 0xFEDA | 0xFEEB => {
                if engine {
                    let v = decode::ascii(d, f.pgn == 0xFEEB);
                    if !v.is_empty() {
                        match f.pgn {
                            0xFEEC => self.vin = v,
                            0xFEDA => self.sw = v,
                            _ => self.comp_id = v,
                        }
                    }
                }
                false
            }
            0xFEE5 => {
                if engine {
                    self.hours_text = decode::hours(d).unwrap_or_default();
                }
                false
            }
            0xFECA => {
                // Every module's DM1 is kept, not just the engine's: on a portable compressor the
                // controller at SA 48 has its own faults.
                let rpm = if engine { self.rpm } else { f64::NAN };
                let names = self.names.clone();
                let m = self.module(f.sa);
                m.active.clear();
                let lamps = decode::parse_dm(d, &mut m.active, &names);
                m.red = lamps.red;
                m.amber = lamps.amber;
                m.protect = lamps.protect;
                m.mil = lamps.mil;
                m.last_dm1 = now;
                m.timeline.update(Some(&m.active), now, rpm);
                true
            }
            0xFECB => {
                let names = self.names.clone();
                let m = self.module(f.sa);
                m.prev.clear();
                decode::parse_dm(d, &mut m.prev, &names);
                m.last_dm2 = now;
                true
            }
            _ => false,
        }
    }

    /// FE56 from the engine always wins. On machines where the DEF tank header or an
    /// aftertreatment module sends FE56 itself the engine never does, so those are used too.
    fn accept_def(&mut self, sa: i32, engine: bool, now: i64) -> bool {
        if engine {
            self.last_engine_def = now;
            return true;
        }
        if self.last_engine_def != NEVER && now - self.last_engine_def < DEF_FALLBACK_MS {
            return false;
        }
        if self.def_sa >= 0
            && self.def_sa != sa
            && self.def_sa != self.engine_sa
            && self.last_def != NEVER
            && now - self.last_def < DEF_FALLBACK_MS
        {
            return false;
        }
        true
    }

    fn track_bus(&mut self, f: &J1939Frame, now: i64) {
        let pr = self.pgns.entry((f.pgn, f.sa)).or_insert_with(|| PgnRow {
            pgn: f.pgn,
            sa: f.sa,
            count: 0,
            last: NEVER,
            last_len: 0,
        });
        pr.count += 1;
        pr.last = now;
        pr.last_len = f.data.len();

        let sr = self.sas.entry(f.sa).or_insert_with(|| SaRow {
            sa: f.sa,
            count: 0,
            last: NEVER,
            name_hex: String::new(),
            claimed: false,
        });
        sr.count += 1;
        sr.last = now;
        if f.pgn == 0xEE00 {
            sr.claimed = true;
            sr.name_hex = f
                .data
                .iter()
                .map(|b| format!("{b:02X}"))
                .collect::<Vec<_>>()
                .join("-");
        }
    }

    /// Offer the current readings to the trend log; it decides whether a second has passed.
    pub fn sample_trend(&mut self, now: i64) -> bool {
        let (red, amber, n) = {
            let e = self.engine();
            (e.red, e.amber, e.active.len())
        };
        let s = TrendSample {
            time: now,
            rpm: self.rpm,
            coolant_c: self.coolant_c,
            oil_kpa: self.oil_kpa,
            battery_v: self.battery_v,
            fuel_lph: self.fuel_lph,
            red,
            amber,
            active_codes: n,
        };
        self.trend.offer(s)
    }

    /// Sorted by PGN then SA: count-ranked rows reorder under the cursor on a busy bus.
    pub fn pgn_rows(&self) -> Vec<PgnRow> {
        self.pgns.values().cloned().collect()
    }

    pub fn sa_rows(&self) -> Vec<SaRow> {
        self.sas.values().cloned().collect()
    }

    pub fn def_text(&self) -> String {
        if self.def_silent {
            return "n/a (not talking)".to_string();
        }
        let pct = if has(self.def_pct) {
            format!("{}%", fixed(self.def_pct, 1))
        } else {
            "n/a".to_string()
        };
        let t = if has(self.def_temp_c) {
            format!("{} C", fixed(self.def_temp_c, 0))
        } else {
            "n/a".to_string()
        };
        format!("{pct}  temp {t}")
    }

    pub fn aft_text(&self) -> String {
        let mut text = self.aft.text();
        if self.def_sa >= 0 && self.def_sa != self.engine_sa {
            text.push_str(&format!(
                "\r\n(FE56 from {}, not the engine)",
                self.names.sa_label(self.def_sa)
            ));
        }
        text
    }

    pub fn oil_text(&self) -> String {
        if !has(self.oil_kpa) {
            return "—".to_string();
        }
        format!(
            "{} kPa ({} psi)",
            fixed(self.oil_kpa, 0),
            fixed(self.oil_kpa * 0.145, 1)
        )
    }

    fn engine_active(&self) -> &[Dtc] {
        self.peek(self.engine_sa).map_or(&[], |m| &m.active)
    }

    pub fn has_spn(&self, spn: i32) -> bool {
        self.engine_active().iter().any(|d| d.spn == spn)
    }

    pub fn has_tank_fmi9(&self) -> bool {
        self.engine_active()
            .iter()
            .any(|d| matches!(d.spn, 1761 | 3031 | 3364) && d.fmi == 9)
    }
}

/// `value + unit` in the given number of decimals, or an em dash when not published.
pub fn fmt_reading(v: f64, unit: &str, places: usize) -> String {
    if has(v) {
        format!("{}{unit}", fixed(v, places))
    } else {
        "—".to_string()
    }
}

fn nan_if_negative(v: f64) -> f64 {
    if v < 0.0 {
        f64::NAN
    } else {
        v
    }
}

/// What the UI shows of the bus at one moment (the C# `BusSnapshot`, monitor half). The adapter
/// fields (connected, busy, fps, ...) are filled by whoever owns the adapter.
#[derive(Debug, Clone, PartialEq)]
pub struct BusSnapshot {
    pub connected: bool,
    pub busy: bool,
    pub busy_what: String,
    pub adapter_name: String,
    pub device_id: i32,
    pub protocol: String,
    pub last_error: String,
    pub fps: u32,
    pub quiet_ms: i64,
    pub stale: bool,
    pub tsc_rpm: i32,
    pub engine_sa: i32,
    pub view_sa: i32,
    pub rpm: f64,
    pub coolant_c: f64,
    pub oil_kpa: f64,
    pub battery_v: f64,
    pub fuel_lph: f64,
    pub def_text: String,
    pub aft_text: String,
    pub severity_raw: i32,
    pub low_lamp_raw: i32,
    pub oil_text: String,
    pub vin: String,
    pub sw: String,
    pub comp_id: String,
    pub hours: String,
    pub red: bool,
    pub amber: bool,
    pub protect: bool,
    pub mil: bool,
    pub has_spn_5246: bool,
    pub has_tank_fmi9: bool,
    pub has_1569: bool,
    pub last_reset_report: String,
    /// Bumps on every finished clear, so the same report text twice still shows.
    pub reset_seq: u32,
    pub def_sa: i32,
    pub active: Vec<Dtc>,
    pub prev: Vec<Dtc>,
    pub fault_modules: Vec<i32>,
}

impl Default for BusSnapshot {
    fn default() -> Self {
        Self {
            connected: false,
            busy: false,
            busy_what: String::new(),
            adapter_name: String::new(),
            device_id: 0,
            protocol: String::new(),
            last_error: String::new(),
            fps: 0,
            quiet_ms: 0,
            stale: false,
            tsc_rpm: 0,
            engine_sa: 0,
            view_sa: 0,
            rpm: f64::NAN,
            coolant_c: f64::NAN,
            oil_kpa: f64::NAN,
            battery_v: f64::NAN,
            fuel_lph: f64::NAN,
            def_text: "—".to_string(),
            aft_text: AftState::default().text(),
            severity_raw: -1,
            low_lamp_raw: -1,
            oil_text: "—".to_string(),
            vin: String::new(),
            sw: String::new(),
            comp_id: String::new(),
            hours: String::new(),
            red: false,
            amber: false,
            protect: false,
            mil: false,
            has_spn_5246: false,
            has_tank_fmi9: false,
            has_1569: false,
            last_reset_report: String::new(),
            reset_seq: 0,
            def_sa: -1,
            active: Vec::new(),
            prev: Vec::new(),
            fault_modules: Vec::new(),
        }
    }
}

impl BusSnapshot {
    /// Disconnect can race the snapshot: the adapter is already gone while the monitor still holds
    /// the last live values. Blanking keeps the HUD from looking hooked up.
    pub fn blank_disconnected_readouts(&mut self) {
        self.rpm = f64::NAN;
        self.coolant_c = f64::NAN;
        self.oil_kpa = f64::NAN;
        self.battery_v = f64::NAN;
        self.fuel_lph = f64::NAN;
        self.def_text = "—".to_string();
        self.aft_text = AftState::default().text();
        self.def_sa = -1;
        self.severity_raw = -1;
        self.low_lamp_raw = -1;
        self.oil_text = "—".to_string();
        self.vin.clear();
        self.sw.clear();
        self.comp_id.clear();
        self.hours.clear();
        self.red = false;
        self.amber = false;
        self.protect = false;
        self.mil = false;
        self.has_spn_5246 = false;
        self.has_tank_fmi9 = false;
        self.has_1569 = false;
        self.active.clear();
        self.prev.clear();
        self.fault_modules.clear();
    }
}

impl BusMonitor {
    /// The monitor half of a snapshot; `view_sa` -1 follows the engine.
    pub fn snapshot(&mut self, view_sa: i32) -> BusSnapshot {
        let view = if view_sa < 0 { self.engine_sa } else { view_sa };
        let m = self.module(view).clone();
        BusSnapshot {
            engine_sa: self.engine_sa,
            view_sa: view,
            active: m.active,
            prev: m.prev,
            red: m.red,
            amber: m.amber,
            protect: m.protect,
            mil: m.mil,
            fault_modules: self.modules_with_faults(),
            rpm: self.rpm,
            coolant_c: self.coolant_c,
            oil_kpa: self.oil_kpa,
            battery_v: self.battery_v,
            fuel_lph: self.fuel_lph,
            def_text: self.def_text(),
            aft_text: self.aft_text(),
            severity_raw: self.aft.severity_raw,
            low_lamp_raw: self.aft.low_lamp_raw,
            oil_text: self.oil_text(),
            vin: self.vin.clone(),
            sw: self.sw.clone(),
            comp_id: self.comp_id.clone(),
            hours: self.hours_text.clone(),
            has_spn_5246: self.has_spn(5246),
            has_tank_fmi9: self.has_tank_fmi9(),
            has_1569: self.has_spn(1569),
            def_sa: self.def_sa,
            ..BusSnapshot::default()
        }
    }
}
