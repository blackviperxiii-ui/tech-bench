//! The bus worker (port of `BusWorker.cs`): owns the adapter, keeps reading frames into the
//! monitor, refreshes DM1/DM2/DEF every 2 s, holds TSC1 only while the Safety toggle allows it,
//! reconnects a silent adapter, and runs code clears. The stdio loop in `main.rs` calls
//! [`Bus::cycle`] every few milliseconds and [`Bus::handle`] for each request.

use crate::driver::{Driver, Loader};
use crate::rp1210::Rp1210;
use serde_json::{json, Value};
use std::collections::VecDeque;
use std::time::{Duration, SystemTime, UNIX_EPOCH};
use tb_core::fmt::fixed;
use tb_core::j1939::clear::{
    destinations, dm13_tx, refresh_dm_frames, request_tx, tsc1_dest, tsc1_tx, J1939Tx,
    COMPRESSOR_SA, DEF_TANK,
};
use tb_core::j1939::code_clear::{ClearKind, ClearLink, CodeClearRun};
use tb_core::j1939::guard::SendPolicy;
use tb_core::j1939::monitor::{fmt_reading, BusMonitor, BusSnapshot};
use tb_core::j1939::Dtc;
use tb_core::rp1210::Rp1210Api;

pub const CYCLE_MS: u64 = 5;
const TSC_HEARTBEAT_MS: i64 = 40;
const AUTO_REFRESH_MS: i64 = 2000;
const RECONNECT_EVERY_MS: i64 = 5000;
const SILENT_RECONNECT_MS: i64 = 6000;
const STALE_AFTER_MS: i64 = 3000;
const MAX_LOG: usize = 500;

pub fn now_ms() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as i64)
        .unwrap_or(0)
}

/// Everything but the adapter and the monitor, so a code clear can borrow the three apart.
struct Live {
    want_connected: bool,
    auto_reconnect: bool,
    auto_refresh: bool,
    allow_tsc_hold: bool,
    tsc_rpm: i32,
    view_sa: i32,
    busy: bool,
    busy_what: String,
    api: Option<Rp1210Api>,
    last_reset_report: String,
    reset_seq: u32,
    last_req: i64,
    last_tsc: i64,
    last_reconnect: i64,
    last_good: i64,
    fps_window: i64,
    frames_this_window: u32,
    fps: u32,
    log: VecDeque<String>,
}

impl Live {
    fn log(&mut self, s: impl Into<String>) {
        if self.log.len() >= MAX_LOG {
            self.log.pop_front();
        }
        self.log.push_back(s.into());
    }

    fn policy(&self) -> SendPolicy {
        SendPolicy {
            allow_tsc_hold: self.allow_tsc_hold,
        }
    }
}

pub struct Bus {
    rp: Rp1210,
    mon: BusMonitor,
    live: Live,
}

/// TSC1 heartbeat, frame reads, frames/s and the trend sample. Also runs while a code clear waits.
fn service_bus(rp: &mut Rp1210, mon: &mut BusMonitor, live: &mut Live, now: i64) {
    if live.tsc_rpm > 0 && !live.allow_tsc_hold {
        live.tsc_rpm = 0;
        rp.send_j1939(&tsc1_tx(0, mon.engine_sa()), live.policy());
        live.log("TSC1 released — Safety toggle is off");
    }
    if live.tsc_rpm > 0 && now - live.last_tsc >= TSC_HEARTBEAT_MS {
        live.last_tsc = now;
        rp.send_j1939(&tsc1_tx(live.tsc_rpm, mon.engine_sa()), live.policy());
    }
    let mut read = 0;
    while read < 500 {
        let Some(f) = rp.read() else { break };
        read += 1;
        live.frames_this_window += 1;
        live.last_good = now;
        mon.feed(&f, now);
    }
    if live.fps_window == 0 {
        live.fps_window = now;
    }
    if now - live.fps_window >= 1000 {
        live.fps = live.frames_this_window;
        live.frames_this_window = 0;
        live.fps_window = now;
    }
    mon.sample_trend(now);
}

/// The live adapter as a code clear sees it.
struct WorkerLink<'a> {
    rp: &'a mut Rp1210,
    live: &'a mut Live,
}

impl ClearLink for WorkerLink<'_> {
    fn now(&self) -> i64 {
        now_ms()
    }

    fn send(&mut self, tx: &J1939Tx) -> bool {
        let policy = self.live.policy();
        self.rp.send_j1939(tx, policy)
    }

    fn pump(
        &mut self,
        mon: &mut BusMonitor,
        ms: i64,
        done: Option<&dyn Fn(&BusMonitor) -> bool>,
    ) -> bool {
        let end = now_ms() + ms;
        loop {
            if !self.live.want_connected || !self.rp.is_connected() {
                return false;
            }
            service_bus(self.rp, mon, self.live, now_ms());
            if done.is_some_and(|d| d(mon)) {
                return true;
            }
            if now_ms() >= end {
                return true;
            }
            std::thread::sleep(Duration::from_millis(CYCLE_MS));
        }
    }

    fn progress(&mut self, what: &str) {
        self.live.busy_what = what.to_string();
    }

    fn last_error(&self) -> String {
        self.rp.last_error.clone()
    }
}

fn num(v: f64) -> Value {
    if v.is_nan() {
        Value::Null
    } else {
        json!(v)
    }
}

fn dtcs(list: &[Dtc]) -> Value {
    Value::Array(
        list.iter()
            .map(|d| {
                json!({"spn": d.spn, "fmi": d.fmi, "occ": d.occ, "name": d.name, "fmiText": d.fmi_text,
                        "text": d.to_string()})
            })
            .collect(),
    )
}

pub fn snapshot_json(s: &BusSnapshot) -> Value {
    json!({
        "connected": s.connected, "busy": s.busy, "busyWhat": s.busy_what,
        "adapterName": s.adapter_name, "deviceId": s.device_id, "protocol": s.protocol,
        "lastError": s.last_error, "fps": s.fps, "quietMs": s.quiet_ms, "stale": s.stale,
        "tscRpm": s.tsc_rpm, "engineSa": s.engine_sa, "viewSa": s.view_sa,
        "rpm": num(s.rpm), "coolantC": num(s.coolant_c), "oilKpa": num(s.oil_kpa),
        "batteryV": num(s.battery_v), "fuelLph": num(s.fuel_lph),
        "defText": s.def_text, "aftText": s.aft_text, "severityRaw": s.severity_raw,
        "lowLampRaw": s.low_lamp_raw, "oilText": s.oil_text,
        "vin": s.vin, "sw": s.sw, "compId": s.comp_id, "hours": s.hours,
        "red": s.red, "amber": s.amber, "protect": s.protect, "mil": s.mil,
        "hasSpn5246": s.has_spn_5246, "hasTankFmi9": s.has_tank_fmi9, "has1569": s.has_1569,
        "lastResetReport": s.last_reset_report, "resetSeq": s.reset_seq, "defSa": s.def_sa,
        "active": dtcs(&s.active), "prev": dtcs(&s.prev), "faultModules": s.fault_modules,
    })
}

fn on_off(v: bool) -> &'static str {
    if v {
        "ON"
    } else {
        "off"
    }
}

/// The INLINE 7 HUD lines, worded as `Inline7Control.Refresh` writes them.
pub fn hud_json(s: &BusSnapshot, want_connected: bool) -> Value {
    let status = if s.busy {
        format!("Working: {}…", s.busy_what)
    } else if s.connected {
        let stale = if s.stale {
            format!(
                "  —  NO FRAMES for {}s, readings below are stale",
                fixed(s.quiet_ms as f64 / 1000.0, 0)
            )
        } else {
            String::new()
        };
        format!(
            "Connected  {}  device {}  {}  {} frames/s{stale}",
            s.adapter_name, s.device_id, s.protocol, s.fps
        )
    } else if want_connected {
        format!("Connecting… {}", s.last_error)
    } else {
        "Disconnected — close Guidanz / J1939 tool / USB-Link Explorer before Connect.".into()
    };
    let rpm = if s.rpm.is_nan() {
        "RPM: —".to_string()
    } else {
        format!(
            "RPM: {}{}",
            fixed(s.rpm, 0),
            if s.stale { " (stale)" } else { "" }
        )
    };
    let mut lamps = format!("Red Stop: {}   Amber: {}", on_off(s.red), on_off(s.amber));
    if s.protect {
        lamps.push_str("   Protect: ON");
    }
    if s.mil {
        lamps.push_str("   MIL: ON");
    }
    let tsc = if s.tsc_rpm > 0 {
        format!("    TSC1 holding {} RPM", s.tsc_rpm)
    } else {
        "    TSC1 released".to_string()
    };
    let mon = format!(
        "Coolant: {}    Oil: {}    Battery: {}    Fuel rate: {}{tsc}",
        fmt_reading(s.coolant_c, " C", 0),
        s.oil_text,
        fmt_reading(s.battery_v, " V", 2),
        fmt_reading(s.fuel_lph, " L/h", 2)
    );
    json!({"status": status, "rpm": rpm, "lamps": lamps, "def": format!("DEF: {}", s.def_text),
           "mon": mon})
}

impl Bus {
    /// A bus over one fixed driver (the fake adapter, or a test double).
    pub fn new(driver: Box<dyn Driver>) -> Self {
        Self::over(Rp1210::new(driver))
    }

    /// A bus that loads each adapter's vendor DLL on connect.
    pub fn with_loader(loader: Loader) -> Self {
        Self::over(Rp1210::with_loader(loader))
    }

    fn over(rp: Rp1210) -> Self {
        Self {
            rp,
            mon: BusMonitor::new(),
            live: Live {
                want_connected: false,
                auto_reconnect: true,
                auto_refresh: true,
                allow_tsc_hold: false,
                tsc_rpm: 0,
                view_sa: -1,
                busy: false,
                busy_what: String::new(),
                api: None,
                last_reset_report: String::new(),
                reset_seq: 0,
                last_req: 0,
                last_tsc: 0,
                last_reconnect: 0,
                last_good: 0,
                fps_window: 0,
                frames_this_window: 0,
                fps: 0,
                log: VecDeque::new(),
            },
        }
    }

    pub fn rp(&self) -> &Rp1210 {
        &self.rp
    }

    pub fn rp_mut(&mut self) -> &mut Rp1210 {
        &mut self.rp
    }

    pub fn monitor(&mut self) -> &mut BusMonitor {
        &mut self.mon
    }

    pub fn request_connect(&mut self, api: Rp1210Api) {
        self.live.api = Some(api);
        self.live.want_connected = true;
        self.live.last_reconnect = 0;
    }

    pub fn request_disconnect(&mut self) {
        self.live.want_connected = false;
    }

    pub fn drain_log(&mut self) -> Vec<String> {
        self.live.log.drain(..).collect()
    }

    /// One pass of the worker loop.
    pub fn cycle(&mut self) {
        let now = now_ms();
        if !self.live.want_connected {
            if self.rp.is_connected() {
                if self.live.tsc_rpm > 0 {
                    self.rp
                        .send_j1939(&tsc1_tx(0, self.mon.engine_sa()), self.live.policy());
                    self.live.tsc_rpm = 0;
                }
                self.rp.disconnect();
                self.mon.clear_live();
                self.live.log("Disconnected");
            }
            return;
        }
        if !self.rp.is_connected() {
            self.try_connect(now);
            return;
        }
        if self.live.auto_refresh && now - self.live.last_req >= AUTO_REFRESH_MS {
            self.request_codes(false);
        }
        service_bus(&mut self.rp, &mut self.mon, &mut self.live, now);
        // An adapter that stops answering stays "connected", so silence is the real signal.
        if self.live.auto_reconnect
            && self.live.last_good != 0
            && now - self.live.last_good > SILENT_RECONNECT_MS
            && now - self.live.last_reconnect > RECONNECT_EVERY_MS
        {
            self.live.last_reconnect = now;
            self.live.log("Auto-reconnect…");
            self.rp.disconnect();
            self.mon.clear_live();
        }
    }

    fn try_connect(&mut self, now: i64) {
        if self.live.last_reconnect != 0 && now - self.live.last_reconnect < RECONNECT_EVERY_MS {
            return;
        }
        self.live.last_reconnect = now;
        let Some(api) = self.live.api.clone() else {
            self.live.log("Connect failed: No RP1210 adapter selected.");
            self.live.want_connected = false;
            return;
        };
        self.live.busy = true;
        self.live.busy_what = "connecting".into();
        let ok = self.rp.connect(&api);
        self.live.busy = false;
        self.live.busy_what.clear();
        if !ok {
            let why = self.rp.last_error.clone();
            self.live.log(format!("Connect failed: {why}"));
            if !self.live.auto_reconnect {
                self.live.want_connected = false;
            }
            return;
        }
        self.live.last_good = now;
        self.live.fps_window = now;
        self.live.frames_this_window = 0;
        self.mon.clear_live();
        let (dev, proto) = (self.rp.device_id, self.rp.protocol.clone());
        self.live.log(format!("Connected  device {dev}  {proto}"));
        if !self.rp.claim_status.is_empty() {
            let claim = self.rp.claim_status.clone();
            self.live.log(claim);
        }
        self.request_codes(true);
    }

    fn send(&mut self, tx: &J1939Tx) -> bool {
        self.rp.send_j1939(tx, self.live.policy())
    }

    fn request_codes(&mut self, log: bool) {
        if !self.rp.is_connected() {
            return;
        }
        let engine_sa = self.mon.engine_sa();
        let def_sa = self.mon.def_sa;
        for tx in refresh_dm_frames(&destinations(engine_sa, &[])) {
            self.send(&tx);
        }
        let engine = tsc1_dest(engine_sa);
        self.send(&request_tx(DEF_TANK, engine));
        // Some machines send FE56 from the tank header or an aftertreatment module, not the ECM.
        if (0..=253).contains(&def_sa) && def_sa != i32::from(engine) {
            self.send(&request_tx(DEF_TANK, def_sa as u8));
        }
        self.live.last_req = now_ms();
        if log {
            self.live.log(format!(
                "Requested DM1 / DM2 / DEF tank (engine + compressor SA {COMPRESSOR_SA})"
            ));
        }
    }

    fn ping_identity(&mut self) {
        self.mon.vin.clear();
        self.mon.sw.clear();
        self.mon.comp_id.clear();
        self.mon.hours_text.clear();
        let sa = tsc1_dest(self.mon.engine_sa());
        let mut asks = vec![
            (0xFEEC, sa),
            (0xFEEC, 255),
            (0xFEDA, sa),
            (0xFEDA, 255),
            (0xFEEB, sa),
            (0xFEE5, sa),
            (0xFECA, sa),
        ];
        if sa != 0 {
            asks.push((0xFEEC, 0));
            asks.push((0xFEDA, 0));
        }
        for (pgn, dest) in asks {
            self.send(&request_tx(pgn, dest));
        }
        self.live
            .log("Pinged ECM for VIN / software / component / hours / DM1");
    }

    fn set_tsc1(&mut self, rpm: i32) {
        let mut next = rpm.max(0);
        if next > 0 && !self.live.allow_tsc_hold {
            next = 0;
            self.live.log("TSC1 refused — Safety toggle is off");
        }
        self.live.tsc_rpm = next;
        if self.rp.is_connected() {
            self.send(&tsc1_tx(next, self.mon.engine_sa()));
            self.live.log(if next <= 0 {
                "TSC1 release — ECM has its own throttle back".to_string()
            } else {
                format!("TSC1 request {next} RPM (heartbeat while held)")
            });
        }
    }

    /// DM11/DM3 clear; the stdio loop stays blocked while it runs, as the C# bus thread did.
    fn run_clear(&mut self, kind: ClearKind) -> String {
        let what = if kind == ClearKind::All {
            "Reset all codes"
        } else {
            "Clear previous"
        };
        if !self.rp.is_connected() {
            self.live.last_reset_report =
                format!("{what} FAILED: adapter not connected — nothing was sent.\r\n");
            self.live.reset_seq += 1;
            return self.live.last_reset_report.clone();
        }
        self.live.busy = true;
        self.live.busy_what = if kind == ClearKind::All {
            "resetting codes".into()
        } else {
            "clearing previous codes".into()
        };
        let mut run = CodeClearRun::new(kind);
        let report = {
            let mut link = WorkerLink {
                rp: &mut self.rp,
                live: &mut self.live,
            };
            run.execute(&mut self.mon, &mut link)
        };
        self.live.busy = false;
        self.live.busy_what.clear();
        self.live.last_reset_report = report.clone();
        self.live.reset_seq += 1;
        self.live.log(format!(
            "Code clear — {}\r\n{}",
            run.headline(),
            report.trim_end()
        ));
        // The clear just re-read DM1/DM2/FE56; the next auto-refresh can wait its turn.
        self.live.last_req = now_ms();
        report
    }

    pub fn snapshot(&mut self) -> BusSnapshot {
        let now = now_ms();
        let mut s = self.mon.snapshot(self.live.view_sa);
        s.connected = self.rp.is_connected();
        s.busy = self.live.busy;
        s.busy_what = self.live.busy_what.clone();
        s.device_id = i32::from(self.rp.device_id);
        s.protocol = self.rp.protocol.clone();
        s.last_error = self.rp.last_error.clone();
        s.adapter_name = self
            .rp
            .api
            .as_ref()
            .map(|a| a.to_string())
            .unwrap_or_default();
        s.fps = self.live.fps;
        s.tsc_rpm = self.live.tsc_rpm;
        s.quiet_ms = if self.live.last_good == 0 {
            0
        } else {
            now - self.live.last_good
        };
        s.stale = s.connected && self.live.last_good != 0 && s.quiet_ms > STALE_AFTER_MS;
        s.last_reset_report = self.live.last_reset_report.clone();
        s.reset_seq = self.live.reset_seq;
        if !s.connected {
            s.blank_disconnected_readouts();
        }
        s
    }

    /// One JSON request. `adapters` is what this PC has (or the fake adapter in fake mode).
    pub fn handle(&mut self, req: &Value, adapters: &[Rp1210Api]) -> Result<Value, String> {
        let cmd = req.get("cmd").and_then(Value::as_str).unwrap_or("");
        let int = |k: &str| req.get(k).and_then(Value::as_i64).map(|v| v as i32);
        let flag = |k: &str| req.get(k).and_then(Value::as_bool);
        match cmd {
            "hello" => Ok(json!({
                "bridge": env!("CARGO_PKG_VERSION"),
                "pointerBits": usize::BITS,
            })),
            "adapters" => Ok(Value::Array(
                adapters
                    .iter()
                    .map(|a| {
                        json!({"id": a.id, "name": a.to_string(), "dll": a.dll_path,
                               "inline7": a.looks_like_inline7(), "describe": a.describe()})
                    })
                    .collect(),
            )),
            "connect" => {
                let id = req.get("adapter").and_then(Value::as_str).unwrap_or("");
                let api = if id.is_empty() {
                    Rp1210Api::preferred(adapters).cloned()
                } else {
                    adapters
                        .iter()
                        .find(|a| a.id.eq_ignore_ascii_case(id))
                        .cloned()
                };
                let api = api.ok_or_else(|| format!("No RP1210 adapter {id} on this PC."))?;
                self.request_connect(api);
                Ok(json!(true))
            }
            "disconnect" => {
                self.request_disconnect();
                Ok(json!(true))
            }
            "snapshot" => {
                if let Some(v) = int("viewSa") {
                    self.live.view_sa = v;
                }
                let s = self.snapshot();
                let mut v = snapshot_json(&s);
                v["hud"] = hud_json(&s, self.live.want_connected);
                Ok(v)
            }
            "busLists" => Ok(json!({
                "pgns": self.mon.pgn_rows().iter().map(|r| json!({
                    "pgn": r.pgn, "sa": r.sa, "count": r.count, "lastLen": r.last_len,
                    "name": self.mon.names.pgn(r.pgn)})).collect::<Vec<_>>(),
                "sas": self.mon.sa_rows().iter().map(|r| json!({
                    "sa": r.sa, "count": r.count, "claimed": r.claimed, "nameHex": r.name_hex,
                    "label": self.mon.names.sa_label(r.sa)})).collect::<Vec<_>>(),
            })),
            "requestCodes" => {
                self.request_codes(true);
                Ok(json!(true))
            }
            "pingIdentity" => {
                if !self.rp.is_connected() {
                    return Err("adapter not connected".into());
                }
                self.ping_identity();
                Ok(json!(true))
            }
            "clear" => {
                let kind = match req.get("kind").and_then(Value::as_str) {
                    Some("all") => ClearKind::All,
                    Some("previous") => ClearKind::Previous,
                    _ => return Err("clear needs kind \"all\" or \"previous\"".into()),
                };
                Ok(json!(self.run_clear(kind)))
            }
            "safety" => {
                self.live.allow_tsc_hold = flag("allowHold").unwrap_or(false);
                if !self.live.allow_tsc_hold && self.live.tsc_rpm > 0 && self.rp.is_connected() {
                    self.live.tsc_rpm = 0;
                    self.send(&tsc1_tx(0, self.mon.engine_sa()));
                    self.live.log("TSC1 released — Safety toggle is off");
                }
                Ok(json!(self.live.allow_tsc_hold))
            }
            "tsc1" => {
                self.set_tsc1(int("rpm").unwrap_or(0));
                Ok(json!(self.live.tsc_rpm))
            }
            "dm13" => {
                let stop = flag("stop").unwrap_or(false);
                let sent = self.send(&dm13_tx(stop));
                self.live.log(if stop {
                    "DM13 Stop Broadcast sent"
                } else {
                    "DM13 Start Broadcast sent"
                });
                Ok(json!(sent))
            }
            "engineSa" => {
                self.mon.set_engine_sa_setting(
                    int("sa").unwrap_or(tb_core::j1939::monitor::AUTO_ENGINE_SA),
                );
                Ok(json!(self.mon.engine_sa()))
            }
            "autoRefresh" => {
                self.live.auto_refresh = flag("on").unwrap_or(true);
                Ok(json!(self.live.auto_refresh))
            }
            "autoReconnect" => {
                self.live.auto_reconnect = flag("on").unwrap_or(true);
                Ok(json!(self.live.auto_reconnect))
            }
            "clearBusStats" => {
                self.mon.clear_bus_stats();
                self.live.log("Bus stats cleared");
                Ok(json!(true))
            }
            "clearHistory" => {
                self.mon.clear_history();
                self.live.log("Trend and timeline cleared");
                Ok(json!(true))
            }
            "trend" => Ok(json!({
                "csv": tb_core::j1939::trend::TrendLog::csv(&self.mon.trend().copy()),
                "count": self.mon.trend().len(),
            })),
            "timeline" => {
                let sa = int("sa").unwrap_or(-1);
                let view = if sa < 0 { self.mon.engine_sa() } else { sa };
                let job = req.get("job").and_then(Value::as_str).unwrap_or("");
                let tl = &self.mon.module(view).timeline;
                Ok(json!({
                    "lines": tl.recent().iter().map(ToString::to_string).collect::<Vec<_>>(),
                    "csv": tl.csv(job),
                }))
            }
            "log" => Ok(json!(self.drain_log())),
            other => Err(format!("unknown command \"{other}\"")),
        }
    }
}
