//! J1939-73 code clear frames (DM11 / DM3) and the DM1/DM2 refresh that follows (port of
//! `src/Safety/J1939Clear.cs`). Shop machines have faults on the engine and on the compressor
//! controller (SA 48); a broadcast-only clear is not enough for that second module.
//!
//! DM11 and DM3 are not sent as data. J1939-73 clears by a Request (PGN 59904) for the DM11 or
//! DM3 PGN. A module asked directly answers with an Acknowledgment (PGN 59392): ACK when the
//! codes are erased, NACK / access denied when it will not. A global request gets no answer.

use super::bam::J1939Frame;
use super::decode::normalize_pgn;

pub const DM1: i32 = 0xFECA;
pub const DM2: i32 = 0xFECB;
pub const DM3: i32 = 0xFECC;
pub const DM11: i32 = 0xFED3;
pub const REQUEST: i32 = 0xEA00;
pub const ACK: i32 = 0xE800;
/// PGN 65110 AT1T1I: DEF level 1761, temp 3031, low-level 5245, inducement 5246.
pub const DEF_TANK: i32 = 0xFE56;
pub const TSC1: i32 = 0x0000;
pub const DM13: i32 = 0xDF00;
pub const ADDRESS_CLAIM: i32 = 0xEE00;

pub const ENGINE_SA: u8 = 0;
pub const COMPRESSOR_SA: u8 = 48;
pub const BROADCAST: u8 = 255;
pub const NULL_SA: u8 = 254;
pub const TOOL_SA: u8 = 0xF9;
pub const PRIORITY: u8 = 6;

/// Acknowledgment control byte (J1939-21).
pub const ACK_POSITIVE: i32 = 0;
pub const ACK_NEGATIVE: i32 = 1;
pub const ACK_DENIED: i32 = 2;
pub const ACK_BUSY: i32 = 3;

/// J1939 NAME, LSB first: function 129 (off-board diagnostic-service tool), industry group 0,
/// arbitrary-address capable.
pub const TOOL_NAME: [u8; 8] = [0x00, 0x00, 0x00, 0x00, 0x00, 0x81, 0x00, 0x80];

/// Unicast destination: a real module SA, or `fallback` when unknown / global.
pub fn unicast_dest(sa: i32, fallback: u8) -> u8 {
    if !(0..=253).contains(&sa) {
        return fallback;
    }
    sa as u8
}

fn add_unique(dests: &mut Vec<u8>, d: u8) {
    if !dests.contains(&d) {
        dests.push(d);
    }
}

/// Engine (SA 0), the detected engine SA if different, compressor controller (SA 48), every module
/// that has sent DM1/DM2 on this hookup, then global (255) last.
pub fn destinations(engine_sa: i32, module_sas: &[i32]) -> Vec<u8> {
    let mut dests = Vec::new();
    add_unique(&mut dests, ENGINE_SA);
    add_unique(&mut dests, unicast_dest(engine_sa, ENGINE_SA));
    add_unique(&mut dests, COMPRESSOR_SA);
    for &sa in module_sas {
        if !(0..=253).contains(&sa) || sa == i32::from(TOOL_SA) {
            continue;
        }
        add_unique(&mut dests, sa as u8);
    }
    add_unique(&mut dests, BROADCAST);
    dests
}

/// The directed (non-global) part of a destination list.
pub fn directed(dests: &[u8]) -> Vec<u8> {
    let mut list = Vec::new();
    for &d in dests {
        if d != BROADCAST && d != NULL_SA && d != TOOL_SA {
            add_unique(&mut list, d);
        }
    }
    list
}

pub fn request_payload(pgn: i32) -> Vec<u8> {
    vec![
        (pgn & 0xFF) as u8,
        ((pgn >> 8) & 0xFF) as u8,
        ((pgn >> 16) & 0xFF) as u8,
    ]
}

/// RP1210 J1939 send buffer: PGN (3) + priority + tool SA + dest + data.
pub fn rp1210_message(pgn: i32, dest: u8, data: &[u8], priority: u8) -> Vec<u8> {
    let mut m = Vec::with_capacity(6 + data.len());
    m.push((pgn & 0xFF) as u8);
    m.push(((pgn >> 8) & 0xFF) as u8);
    m.push(((pgn >> 16) & 0xFF) as u8);
    m.push(priority & 0x07);
    m.push(TOOL_SA);
    m.push(dest);
    m.extend_from_slice(data);
    m
}

/// One constructed J1939 send.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct J1939Tx {
    pub pgn: i32,
    pub dest: u8,
    pub priority: u8,
    pub data: Vec<u8>,
    pub rp1210: Vec<u8>,
}

impl J1939Tx {
    /// The PGN a Request asks for, or -1 when this is not a Request.
    pub fn requested_pgn(&self) -> i32 {
        if self.pgn != REQUEST || self.data.len() < 3 {
            return -1;
        }
        i32::from(self.data[0]) | (i32::from(self.data[1]) << 8) | (i32::from(self.data[2]) << 16)
    }
}

pub fn tx(pgn: i32, dest: u8, data: &[u8], priority: u8) -> J1939Tx {
    J1939Tx {
        pgn,
        dest,
        priority,
        data: data.to_vec(),
        rp1210: rp1210_message(pgn, dest, data, priority),
    }
}

pub fn request_tx(requested_pgn: i32, dest: u8) -> J1939Tx {
    tx(REQUEST, dest, &request_payload(requested_pgn), PRIORITY)
}

/// A Request for DM11 or DM3 to each destination, in order.
pub fn clear_request_frames(clear_pgn: i32, dests: &[u8]) -> Vec<J1939Tx> {
    dests.iter().map(|&d| request_tx(clear_pgn, d)).collect()
}

/// Request the DEF/SCR tank PGN from each destination.
pub fn aftertreatment_read_frames(dests: &[u8]) -> Vec<J1939Tx> {
    dests.iter().map(|&d| request_tx(DEF_TANK, d)).collect()
}

/// Request DM1 (active) and DM2 (previously active) from each destination.
pub fn refresh_dm_frames(dests: &[u8]) -> Vec<J1939Tx> {
    dests
        .iter()
        .flat_map(|&d| [request_tx(DM1, d), request_tx(DM2, d)])
        .collect()
}

/// TSC1 destination: the detected engine SA, falling back to ECM 1 (0).
pub fn tsc1_dest(engine_sa: i32) -> u8 {
    unicast_dest(engine_sa, ENGINE_SA)
}

/// SAE J1939 TSC1 (PGN 0) speed request to the engine; rpm 0 releases control.
pub fn tsc1_tx(rpm: i32, engine_sa: i32) -> J1939Tx {
    let mut d = [0xFFu8; 8];
    if rpm <= 0 {
        d[0] = 0x00; // override disabled
    } else {
        d[0] = 0x01; // speed control
        let raw = ((f64::from(rpm) / 0.125) as i32).min(65534);
        d[1] = (raw & 0xFF) as u8;
        d[2] = ((raw >> 8) & 0xFF) as u8;
    }
    tx(TSC1, tsc1_dest(engine_sa), &d, 3)
}

/// DM13 (PGN 57088) stop / start broadcast on J1939 network 1.
pub fn dm13_tx(stop_broadcast: bool) -> J1939Tx {
    let mut d = [0xFFu8; 8];
    d[0] = if stop_broadcast { 0x04 } else { 0x01 };
    tx(DM13, BROADCAST, &d, PRIORITY)
}

/// This tool's own address claim, announced by hand when the driver cannot protect the address.
pub fn address_claim_tx() -> J1939Tx {
    tx(ADDRESS_CLAIM, BROADCAST, &TOOL_NAME, PRIORITY)
}

/// One Acknowledgment (PGN 59392) heard on the bus.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AckReply {
    pub sa: i32,
    pub da: i32,
    pub control: i32,
    pub address: i32,
    pub pgn: i32,
    pub time: i64,
}

impl AckReply {
    /// True when this answers a request from this tool: sent to the tool, or sent global with the
    /// tool's address in byte 5 (or 0xFF from modules older than J1939-21 2006).
    pub fn for_tool(&self) -> bool {
        if self.da == i32::from(TOOL_SA) {
            return true;
        }
        if self.da != i32::from(BROADCAST) {
            return false;
        }
        self.address == i32::from(TOOL_SA) || self.address == 0xFF
    }
}

/// Decode an Acknowledgment: byte 1 control, byte 5 the address being answered, bytes 6-8 the PGN.
pub fn try_parse_ack(f: &J1939Frame, now: i64) -> Option<AckReply> {
    if f.data.len() < 8 || normalize_pgn(f.pgn) != ACK {
        return None;
    }
    let d = &f.data;
    let pgn = i32::from(d[5]) | (i32::from(d[6]) << 8) | (i32::from(d[7]) << 16);
    Some(AckReply {
        sa: f.sa,
        da: f.da,
        control: i32::from(d[0]),
        address: i32::from(d[4]),
        pgn: normalize_pgn(pgn),
        time: now,
    })
}

/// DEF / SCR / DPF parameters the shop sees on T4F engines: the codes a plain clear will not hold
/// until the ECM has re-checked the aftertreatment.
pub fn is_aftertreatment_spn(spn: i32) -> bool {
    matches!(
        spn,
        1569 | 1761
            | 3031
            | 3216
            | 3226
            | 3242
            | 3246
            | 3251
            | 3361
            | 3363
            | 3364
            | 3515
            | 3516
            | 3719
            | 3936
            | 4094
            | 4096
            | 4331
            | 4334
            | 4364
            | 4765
            | 4766
            | 5245
            | 5246
            | 5392
            | 5394
    )
}

pub fn count(frames: &[J1939Tx], pgn: i32, dest: u8, data: &[u8]) -> usize {
    frames
        .iter()
        .filter(|t| t.pgn == pgn && t.dest == dest && t.data == data)
        .count()
}

pub fn count_request(frames: &[J1939Tx], requested_pgn: i32, dest: u8) -> usize {
    count(frames, REQUEST, dest, &request_payload(requested_pgn))
}

/// How many frames carry this PGN as data (not as a Request), to any destination.
pub fn count_pgn(frames: &[J1939Tx], pgn: i32) -> usize {
    frames.iter().filter(|t| t.pgn == pgn).count()
}
