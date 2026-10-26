//! The send guard: the one place that decides whether a J1939 frame may leave this tool.
//!
//! Tech Bench reads faults and clears them the J1939-73 way; it never resets DEF/SCR, never
//! writes calibration or parameters, and never runs an ECM service routine. The WinForms app keeps
//! that true by simply not having the code. The recode adds this allow-list on top, and the RP1210
//! bridge runs every frame through it right before `RP1210_SendMessage`, so a bug further up (or a
//! hand-made request on the bridge's stdin) cannot put anything else on the bus.
//!
//! Allowed, and nothing else:
//! - Request (PGN 59904) for DM1, DM2, DM3, DM11, the DEF tank PGN, and the identity PGNs
//!   (VIN, software, component ID, engine hours). These are reads, plus the two standard clears.
//! - TSC1 release (control mode 0) always; TSC1 speed control only while the Safety toggle allows
//!   a hold. No other TSC1 control mode.
//! - DM13 stop / start broadcast, global.
//! - This tool's own address claim (PGN 60928 with the tool NAME).

use super::clear::*;
use std::fmt;

/// PGNs a Request may ask for.
pub const READ_REQUESTS: [i32; 9] = [
    DM1, DM2, DM3, DM11, DEF_TANK, 0xFEEC, // VIN
    0xFEDA, // software ID
    0xFEEB, // component ID
    0xFEE5, // engine hours
];

/// What the caller is allowed to do right now.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct SendPolicy {
    /// The UI Safety toggle: a TSC1 hold above 0 RPM is refused while false.
    pub allow_tsc_hold: bool,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Refusal(pub String);

impl fmt::Display for Refusal {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.0)
    }
}

/// Plain name for a PGN the guard refuses, for the log line.
fn refused_name(pgn: i32) -> &'static str {
    match pgn {
        0xD900 => "DM14 memory access (calibration / parameter write)",
        0xD800 => "DM15 memory access response",
        0xD700 => "DM16 binary data transfer (calibration / parameter write)",
        0xE300 => "DM7 test command",
        0xEF00 => "proprietary A (OEM service routine)",
        0xFED8 => "commanded address",
        p if (0xFF00..=0xFFFF).contains(&p) => "proprietary B (OEM service routine)",
        _ => "not on the send list",
    }
}

fn refuse(why: String) -> Result<(), Refusal> {
    Err(Refusal(format!("refused: {why}")))
}

/// Decide whether `tx` may be sent. The frame must also match its own RP1210 bytes, so the check
/// covers exactly what goes to the driver.
pub fn check(tx: &J1939Tx, policy: SendPolicy) -> Result<(), Refusal> {
    if tx.rp1210 != rp1210_message(tx.pgn, tx.dest, &tx.data, tx.priority) {
        return refuse("frame bytes do not match the frame".to_string());
    }
    let pgn = tx.pgn & 0x3FFFF;
    match pgn {
        REQUEST => {
            if tx.data.len() != 3 {
                return refuse("a Request carries exactly 3 bytes".to_string());
            }
            let asked = tx.requested_pgn();
            if READ_REQUESTS.contains(&asked) {
                Ok(())
            } else {
                refuse(format!(
                    "Request for PGN 0x{asked:04X} ({}); Tech Bench only requests DM1/DM2/DM3/DM11, DEF tank and identity",
                    refused_name(asked)
                ))
            }
        }
        TSC1 => {
            if tx.data.len() != 8 {
                return refuse("TSC1 carries exactly 8 bytes".to_string());
            }
            match tx.data[0] {
                0x00 => Ok(()),
                0x01 if policy.allow_tsc_hold => Ok(()),
                0x01 => refuse("TSC1 hold while the Safety toggle is off".to_string()),
                m => refuse(format!(
                    "TSC1 control mode {m}; only release and speed control are sent"
                )),
            }
        }
        DM13 => {
            if tx.dest == BROADCAST && tx.data.len() == 8 && matches!(tx.data[0], 0x01 | 0x04) {
                Ok(())
            } else {
                refuse("DM13 other than global stop / start broadcast".to_string())
            }
        }
        ADDRESS_CLAIM => {
            if tx.dest == BROADCAST && tx.data == TOOL_NAME {
                Ok(())
            } else {
                refuse("address claim with a NAME that is not this tool's".to_string())
            }
        }
        p => refuse(format!("PGN 0x{p:04X} ({})", refused_name(p))),
    }
}
