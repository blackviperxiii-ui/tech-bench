//! SPN / PGN / source-address labels (port of `src/Core/J1939/Names.cs`).
//!
//! The built-in tables are the shop defaults; the knowledge base can extend or override any of
//! them (data\j1939-names.json in the WinForms app). The C# class keeps the overrides in static
//! fields; here they live in a [`Names`] value the caller owns, so tests and threads don't share state.

use std::collections::HashMap;

#[derive(Debug, Clone, Default)]
pub struct Names {
    spn: Option<HashMap<i32, String>>,
    pgn: Option<HashMap<i32, String>>,
    sa: Option<HashMap<i32, String>>,
}

fn lookup(map: &Option<HashMap<i32, String>>, key: i32) -> Option<&str> {
    match map.as_ref()?.get(&key) {
        Some(v) if !v.is_empty() => Some(v.as_str()),
        _ => None,
    }
}

impl Names {
    /// Built-in tables only (the C# `Names.Reset()` state).
    pub fn new() -> Self {
        Self::default()
    }

    /// Replace all three override tables (the C# `Names.Load`).
    pub fn load(
        &mut self,
        spn: Option<HashMap<i32, String>>,
        pgn: Option<HashMap<i32, String>>,
        sa: Option<HashMap<i32, String>>,
    ) {
        self.spn = spn;
        self.pgn = pgn;
        self.sa = sa;
    }

    pub fn reset(&mut self) {
        *self = Self::default();
    }

    pub fn override_count(&self) -> usize {
        [&self.spn, &self.pgn, &self.sa]
            .iter()
            .map(|m| m.as_ref().map_or(0, HashMap::len))
            .sum()
    }

    pub fn spn(&self, spn: i32) -> String {
        lookup(&self.spn, spn)
            .unwrap_or_else(|| built_in_spn(spn))
            .to_string()
    }

    pub fn sa(&self, sa: i32) -> String {
        match lookup(&self.sa, sa) {
            Some(v) => v.to_string(),
            None => built_in_sa(sa),
        }
    }

    /// "SA 0 ECM 1", or just "SA 61" when the address has no name.
    pub fn sa_label(&self, sa: i32) -> String {
        let bare = format!("SA {sa}");
        let name = self.sa(sa);
        if name.is_empty() || name == bare {
            bare
        } else {
            format!("{bare} {name}")
        }
    }

    pub fn pgn(&self, pgn: i32) -> String {
        lookup(&self.pgn, pgn)
            .unwrap_or_else(|| built_in_pgn(pgn))
            .to_string()
    }
}

fn built_in_sa(sa: i32) -> String {
    match sa {
        0 => "ECM 1",
        3 => "Trans / retarder",
        11 => "Brakes",
        15 => "Retarder",
        23 => "Instrument cluster",
        33 => "Body controller",
        37 => "Cab controller",
        47 => "Ignition",
        48 => "Compressor controller",
        49 => "Cab display",
        59 => "Beede / trans display",
        128 => "Grayhill keypad",
        249 => "This tool / off-board diag",
        253 => "OEM reserved",
        255 => "Broadcast",
        _ => return format!("SA {sa}"),
    }
    .to_string()
}

fn built_in_pgn(pgn: i32) -> &'static str {
    match pgn {
        0x0000 => "TSC1 speed/torque",
        0xE800 => "ACK / NACK",
        0xEA00 => "Request",
        0xEB00 => "TP.DT",
        0xEC00 => "TP.CM",
        0xEE00 => "Address claim",
        0xF001 => "ERC1 retarder",
        0xF003 => "EEC2 load/pedal",
        0xF004 => "EEC1 RPM",
        0xF00A => "EEC3",
        0xFE56 => "DEF tank 1",
        0xFECA => "DM1 active DTCs",
        0xFECB => "DM2 previous DTCs",
        0xFECC => "DM3 clear previous",
        0xFED3 => "DM11 clear active",
        0xFEDA => "Software ID",
        0xFEE5 => "Engine hours",
        0xFEEB => "Component ID",
        0xFEEC => "VIN",
        0xFEED => "Reference weight",
        0xFEEE => "Temps (coolant)",
        0xFEEF => "Oil / fuel pressure",
        0xFEF1 => "CCVS speed",
        0xFEF2 => "Fuel rate",
        0xFEF6 => "Inlet air",
        0xFEF7 => "Electrical / battery",
        0xFEFC => "Fuel level",
        0xDF00 => "DM13 stop/start bcast",
        _ => "",
    }
}

fn built_in_spn(spn: i32) -> &'static str {
    match spn {
        91 => "Accelerator pedal",
        94 => "Fuel delivery pressure",
        97 => "Water in fuel",
        98 => "Oil level",
        100 => "Oil pressure",
        102 => "Boost / MAP",
        105 => "Intake manifold temp",
        110 => "Coolant temp",
        157 => "Fuel rail pressure",
        168 => "Battery voltage",
        190 => "Engine speed",
        611 => "System diagnostic",
        629 => "Controller",
        639 => "J1939 network",
        651 => "Injector cylinder 1",
        677 => "Starter motor relay",
        723 => "Camshaft speed/position",
        1079 => "Sensor supply 1",
        1080 => "Sensor supply 2",
        1569 => "Engine protection / DEF empty derate",
        1761 => "DEF tank level",
        2791 => "EGR valve",
        3031 => "DEF tank temp",
        3216 => "AT1 intake NOx",
        3226 => "AT1 outlet NOx",
        3361 => "DEF dosing unit",
        3363 => "DEF tank heater",
        3364 => "DEF tank quality",
        3515 => "DEF line heater",
        3516 => "DEF tank temp",
        4331 => "DEF pressure",
        4334 => "DEF doser pressure",
        5245 => "SCR inducement time",
        5246 => "SCR operator inducement severity",
        5392 => "DEF pump",
        5394 => "DEF pump state",
        _ => "",
    }
}
