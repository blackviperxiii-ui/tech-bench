//! Rolling 1 Hz history of the live engine readings (port of `src/Features/Adapters/Trend.cs`).
//! Times are milliseconds since the Unix epoch; `f64::NAN` means "not published".

use crate::fmt::fixed;
use crate::time::round_trip_from_ticks;

/// "Never" for a millisecond timestamp (the C# `DateTime.MinValue`).
pub const NEVER: i64 = i64::MIN;

/// ISO 8601 round-trip text of a millisecond timestamp, as C# `DateTime.ToString("o")` writes it.
pub fn iso_ms(ms: i64) -> String {
    round_trip_from_ticks((ms.max(0) as u64) * 10_000)
}

/// One second of engine readings.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct TrendSample {
    pub time: i64,
    pub rpm: f64,
    pub coolant_c: f64,
    pub oil_kpa: f64,
    pub battery_v: f64,
    pub fuel_lph: f64,
    pub red: bool,
    pub amber: bool,
    pub active_codes: usize,
}

impl Default for TrendSample {
    fn default() -> Self {
        Self {
            time: 0,
            rpm: f64::NAN,
            coolant_c: f64::NAN,
            oil_kpa: f64::NAN,
            battery_v: f64::NAN,
            fuel_lph: f64::NAN,
            red: false,
            amber: false,
            active_codes: 0,
        }
    }
}

pub fn has(v: f64) -> bool {
    !v.is_nan()
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TrendChannel {
    Rpm,
    Coolant,
    Oil,
    Battery,
    Fuel,
}

#[derive(Debug, Clone)]
pub struct TrendLog {
    buf: Vec<TrendSample>,
    cap: usize,
    start: usize,
    last_sample: i64,
}

impl Default for TrendLog {
    fn default() -> Self {
        Self::new(Self::DEFAULT_CAPACITY)
    }
}

impl TrendLog {
    /// 30 minutes at 1 Hz.
    pub const DEFAULT_CAPACITY: usize = 30 * 60;

    pub fn new(capacity: usize) -> Self {
        let cap = capacity.max(2);
        Self {
            buf: Vec::with_capacity(cap),
            cap,
            start: 0,
            last_sample: NEVER,
        }
    }

    pub fn capacity(&self) -> usize {
        self.cap
    }

    pub fn len(&self) -> usize {
        self.buf.len()
    }

    pub fn is_empty(&self) -> bool {
        self.buf.is_empty()
    }

    /// Oldest first.
    pub fn get(&self, i: usize) -> Option<&TrendSample> {
        if i >= self.buf.len() {
            return None;
        }
        self.buf.get((self.start + i) % self.buf.len())
    }

    pub fn clear(&mut self) {
        self.buf.clear();
        self.start = 0;
        self.last_sample = NEVER;
    }

    pub fn add(&mut self, s: TrendSample) {
        if self.buf.len() < self.cap {
            self.buf.push(s);
        } else {
            self.buf[self.start] = s;
            self.start = (self.start + 1) % self.cap;
        }
        self.last_sample = s.time;
    }

    /// Adds at most one sample per second; true when it took one.
    pub fn offer(&mut self, s: TrendSample) -> bool {
        if self.last_sample != NEVER && s.time - self.last_sample < 950 {
            return false;
        }
        self.add(s);
        true
    }

    pub fn copy(&self) -> Vec<TrendSample> {
        (0..self.len())
            .filter_map(|i| self.get(i).copied())
            .collect()
    }

    pub fn value(s: &TrendSample, ch: TrendChannel) -> f64 {
        match ch {
            TrendChannel::Rpm => s.rpm,
            TrendChannel::Coolant => s.coolant_c,
            TrendChannel::Oil => s.oil_kpa,
            TrendChannel::Battery => s.battery_v,
            TrendChannel::Fuel => s.fuel_lph,
        }
    }

    pub fn label(ch: TrendChannel) -> &'static str {
        match ch {
            TrendChannel::Rpm => "RPM",
            TrendChannel::Coolant => "Coolant °C",
            TrendChannel::Oil => "Oil kPa",
            TrendChannel::Battery => "Battery V",
            TrendChannel::Fuel => "Fuel L/h",
        }
    }

    /// Min/max of a channel over the samples that have a value.
    pub fn range(samples: &[TrendSample], ch: TrendChannel) -> Option<(f64, f64)> {
        let mut out: Option<(f64, f64)> = None;
        for s in samples {
            let v = Self::value(s, ch);
            if !has(v) {
                continue;
            }
            out = Some(match out {
                None => (v, v),
                Some((lo, hi)) => (lo.min(v), hi.max(v)),
            });
        }
        out
    }

    pub fn csv(samples: &[TrendSample]) -> String {
        let mut out = String::from(
            "time,rpm,coolant_c,oil_kpa,battery_v,fuel_lph,red,amber,active_codes\r\n",
        );
        for s in samples {
            let row = [
                iso_ms(s.time),
                num(s.rpm, 0),
                num(s.coolant_c, 0),
                num(s.oil_kpa, 0),
                num(s.battery_v, 2),
                num(s.fuel_lph, 2),
                if s.red { "1" } else { "0" }.to_string(),
                if s.amber { "1" } else { "0" }.to_string(),
                s.active_codes.to_string(),
            ];
            out.push_str(&row.join(","));
            out.push_str("\r\n");
        }
        out
    }
}

fn num(v: f64, places: usize) -> String {
    if has(v) {
        fixed(v, places)
    } else {
        String::new()
    }
}
