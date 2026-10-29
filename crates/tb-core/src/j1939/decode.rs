//! J1939 payload decoders (port of `src/Core/J1939/J1939Decode.cs`). Strings match the C# app
//! byte for byte, so the ported SelfTest literals hold.

use super::names::Names;
use crate::fmt::fixed;
use std::fmt;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Dtc {
    pub spn: i32,
    pub fmi: i32,
    pub occ: i32,
    pub name: String,
    pub fmi_text: String,
}

impl fmt::Display for Dtc {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        let n = if self.name.is_empty() {
            String::new()
        } else {
            format!("  {}", self.name)
        };
        write!(
            f,
            "SPN {}  FMI {}  occ {}  {}{}",
            self.spn, self.fmi, self.occ, self.fmi_text, n
        )
    }
}

/// DM1/DM2 byte 0 carries four lamps: MIL 7-6, red stop 5-4, amber 3-2, protect 1-0.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct Lamps {
    pub red: bool,
    pub amber: bool,
    pub protect: bool,
    pub mil: bool,
}

pub fn u16_le(d: &[u8], i: usize) -> u16 {
    if i + 1 >= d.len() {
        return 0xFFFF;
    }
    u16::from(d[i]) | (u16::from(d[i + 1]) << 8)
}

pub fn u32_le(d: &[u8], i: usize) -> u32 {
    if i + 3 >= d.len() {
        return 0xFFFF_FFFF;
    }
    u32::from_le_bytes([d[i], d[i + 1], d[i + 2], d[i + 3]])
}

/// PDU1 PGNs (PF below 0xF0) carry the destination in their low byte. Some RP1210 drivers
/// hand that back inside the PGN (0xE8F9 for an ACK to this tool), others zero it. Zero it
/// here so Request, ACK, and TP.CM/TP.DT always match one value.
pub fn normalize_pgn(pgn: i32) -> i32 {
    let mut pgn = pgn & 0x3FFFF;
    if ((pgn >> 8) & 0xFF) < 0xF0 {
        pgn &= 0x3FF00;
    }
    pgn
}

pub fn fmi_name(fmi: i32) -> String {
    match fmi {
        0 => "above normal (most severe)",
        1 => "below normal (most severe)",
        2 => "data erratic",
        3 => "voltage high",
        4 => "voltage low",
        5 => "current low",
        6 => "current high",
        7 => "not responding",
        8 => "abnormal frequency",
        9 => "abnormal update rate (not talking)",
        10 => "abnormal rate of change",
        11 => "root cause unknown",
        12 => "bad intelligent device",
        13 => "out of calibration",
        14 => "special instruction",
        15 => "above normal (least severe)",
        16 => "above normal (moderate)",
        17 => "below normal (least severe)",
        18 => "below normal (moderate)",
        19 => "received network data in error",
        31 => "condition exists",
        _ => return format!("FMI {fmi}"),
    }
    .to_string()
}

/// Parse a DM1/DM2 payload: returns the lamps and appends new DTCs to `list`, skipping
/// empty slots and SPN/FMI pairs already in the list.
pub fn parse_dm(d: &[u8], list: &mut Vec<Dtc>, names: &Names) -> Lamps {
    let mut lamps = Lamps::default();
    if d.len() < 2 {
        return lamps;
    }
    let b0 = d[0];
    lamps.protect = (b0 & 3) == 1;
    lamps.amber = ((b0 >> 2) & 3) == 1;
    lamps.red = ((b0 >> 4) & 3) == 1;
    lamps.mil = ((b0 >> 6) & 3) == 1;
    let mut i = 2;
    while i + 3 < d.len() {
        if !(d[i] == 0xFF && d[i + 1] == 0xFF) {
            let spn =
                i32::from(d[i]) | (i32::from(d[i + 1]) << 8) | ((i32::from(d[i + 2]) & 0xE0) << 11);
            let fmi = i32::from(d[i + 2] & 0x1F);
            let occ = i32::from(d[i + 3] & 0x7F);
            let dup = list.iter().any(|x| x.spn == spn && x.fmi == fmi);
            if spn != 0 && spn < 0x7FFFF && !dup {
                list.push(Dtc {
                    spn,
                    fmi,
                    occ,
                    name: names.spn(spn),
                    fmi_text: fmi_name(fmi),
                });
            }
        }
        i += 4;
    }
    lamps
}

/// EEC1 engine speed, 0.125 rpm/bit in bytes 4-5; -1 when missing or not available.
pub fn rpm(d: &[u8]) -> f64 {
    if d.len() < 5 {
        return -1.0;
    }
    let raw = u16_le(d, 3);
    if raw == 0xFFFF {
        return -1.0;
    }
    f64::from(raw) * 0.125
}

pub fn def_text(d: &[u8]) -> String {
    if d.len() < 2 {
        return "n/a".to_string();
    }
    if d[0] >= 0xFB {
        return "n/a (not talking)".to_string();
    }
    let temp = if d[1] >= 0xFB {
        "n/a".to_string()
    } else {
        format!("{} C", i32::from(d[1]) - 40)
    };
    format!("{}%  temp {}", fixed(f64::from(d[0]) * 0.4, 1), temp)
}

/// DEF/SCR fields from PGN 65110. Each line stays "no data" until that byte arrives.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AftState {
    pub level: String,
    pub temp: String,
    pub severity: String,
    pub low_lamp: String,
    pub low_lamp_raw: i32,
    pub severity_raw: i32,
}

impl AftState {
    pub const NO_DATA: &'static str = "no data";

    pub fn attention_band(severity_raw: i32, low_lamp_raw: i32) -> i32 {
        if severity_raw == 5 || severity_raw == 4 || low_lamp_raw == 4 {
            return 2;
        }
        if severity_raw == 1 || severity_raw == 2 || severity_raw == 3 || low_lamp_raw == 1 {
            return 1;
        }
        0
    }

    pub fn text(&self) -> String {
        format!(
            "DEF level SPN 1761: {}\r\nDEF tank temp SPN 3031: {}\r\nSCR inducement SPN 5246: {}\r\nDEF low-level SPN 5245: {}",
            self.level, self.temp, self.severity, self.low_lamp
        )
    }
}

impl Default for AftState {
    fn default() -> Self {
        Self {
            level: Self::NO_DATA.to_string(),
            temp: Self::NO_DATA.to_string(),
            severity: Self::NO_DATA.to_string(),
            low_lamp: Self::NO_DATA.to_string(),
            low_lamp_raw: -1,
            severity_raw: -1,
        }
    }
}

fn slot_byte(d: &[u8], index: usize, format: impl Fn(i32) -> String) -> String {
    match d.get(index) {
        None => AftState::NO_DATA.to_string(),
        Some(&b) if b >= 0xFB => "not available".to_string(),
        Some(&b) => format(i32::from(b)),
    }
}

/// PGN 65110 (0xFE56) Aftertreatment 1 DEF Tank 1, the layout industrial ECMs
/// still broadcast: byte 1 SPN 1761 at 0.4 %/bit, byte 2 SPN 3031 at 1 °C offset -40,
/// byte 5 bits 6–8 SPN 5245, byte 6 bits 6–8 SPN 5246. Missing bytes stay "no data".
pub fn parse_aftertreatment(d: Option<&[u8]>) -> AftState {
    let mut a = AftState::default();
    let d = match d {
        Some(d) if !d.is_empty() => d,
        _ => return a,
    };
    a.level = slot_byte(d, 0, |raw| format!("{}%", fixed(f64::from(raw) * 0.4, 1)));
    if d.len() < 2 {
        return a;
    }
    a.temp = slot_byte(d, 1, |raw| format!("{} C", raw - 40));
    if d.len() < 5 {
        return a;
    }
    a.low_lamp_raw = sae_bits(d[4], 6, 3);
    a.low_lamp = low_level_5245(a.low_lamp_raw);
    if d.len() < 6 {
        return a;
    }
    a.severity_raw = sae_bits(d[5], 6, 3);
    a.severity = severity_5246(a.severity_raw);
    a
}

/// SAE bit 1 is the least significant bit.
pub fn sae_bits(b: u8, bit1: i32, width: i32) -> i32 {
    let bit1 = bit1.max(1);
    let width = width.max(1);
    let mask = (1 << width) - 1;
    (i32::from(b) >> (bit1 - 1)) & mask
}

pub fn low_level_5245(v: i32) -> String {
    match v {
        0 => "off — DEF level adequate",
        1 => "on solid — DEF low",
        4 => "fast blink — DEF lower",
        7 => "not available",
        _ => return format!("reserved ({v})"),
    }
    .to_string()
}

pub fn severity_5246(v: i32) -> String {
    match v {
        0 => "not active",
        1 => "level 1 — DEF warning",
        2 => "level 2 — second warning",
        3 => "level 3 — low-level inducement",
        4 => "level 4 — severe pre-trigger",
        5 => "level 5 — final inducement",
        6 => "ECM reports a temporary override",
        7 => "not available",
        _ => return format!("reserved ({v})"),
    }
    .to_string()
}

pub fn temp_c(b: u8) -> String {
    if b >= 0xFB {
        return "n/a".to_string();
    }
    format!("{} C", i32::from(b) - 40)
}

pub fn pressure_kpa(b: u8, scale: i32) -> String {
    if b >= 0xFB {
        return "n/a".to_string();
    }
    let k = i32::from(b) * scale;
    format!("{} kPa ({} psi)", k, fixed(f64::from(k) * 0.145, 1))
}

pub fn battery_v(d: &[u8]) -> String {
    if d.len() < 6 {
        return "n/a".to_string();
    }
    let raw = u16_le(d, 4);
    if raw >= 0xFB00 {
        return "n/a".to_string();
    }
    format!("{} V", fixed(f64::from(raw) * 0.05, 2))
}

pub fn fuel_rate(d: &[u8]) -> String {
    if d.len() < 2 {
        return "n/a".to_string();
    }
    let raw = u16_le(d, 0);
    if raw >= 0xFB00 {
        return "n/a".to_string();
    }
    format!("{} L/h", fixed(f64::from(raw) * 0.05, 2))
}

/// PGN 0xFEE5 total engine hours, 0.05 h/bit in bytes 1-4. `None` when missing or not available.
pub fn hours(d: &[u8]) -> Option<String> {
    if d.len() < 4 {
        return None;
    }
    let raw = u32_le(d, 0);
    if raw >= 0xFAFF_FFFF {
        return None;
    }
    Some(format!("{} h", fixed(f64::from(raw) * 0.05, 1)))
}

/// J1939 ASCII strings are '*'-delimited. VIN is one field so the star is noise; component ID
/// packs make/model/serial/unit and needs the fields kept apart (`keep_fields`).
pub fn ascii(d: &[u8], keep_fields: bool) -> String {
    let mut sb = String::with_capacity(d.len() + 8);
    for &b in d {
        if b == b'*' {
            if keep_fields && !sb.is_empty() {
                sb.push_str("  ·  ");
            }
            continue;
        }
        if (32..127).contains(&b) {
            sb.push(char::from(b));
        }
    }
    sb.trim().trim_matches('·').trim().to_string()
}
