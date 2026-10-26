//! Installed RP1210 adapters as their own INIs describe them (port of
//! `src/Features/Adapters/Rp1210Api.cs`). Pure file reading: loading the vendor DLL is the
//! 32-bit bridge's job, so this runs (and is tested) on every OS.

use crate::ini::Ini;
use std::fmt;
use std::path::{Path, PathBuf};

#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Rp1210Device {
    pub id: i32,
    pub name: String,
    pub description: String,
}

impl fmt::Display for Rp1210Device {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        let label = if self.description.is_empty() {
            &self.name
        } else {
            &self.description
        };
        if label.is_empty() {
            write!(f, "device {}", self.id)
        } else {
            write!(f, "{} — {}", self.id, label)
        }
    }
}

/// One installed RP1210 vendor DLL.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct Rp1210Api {
    /// e.g. CMNSI7: also the DLL and INI base name.
    pub id: String,
    /// Vendor-supplied display name.
    pub name: String,
    pub dll_path: String,
    pub devices: Vec<Rp1210Device>,
    pub j1939_protocols: Vec<String>,
    pub supports_iso15765: bool,
}

impl fmt::Display for Rp1210Api {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        if self.name.is_empty() {
            f.write_str(&self.id)
        } else {
            write!(f, "{}  ({})", self.name, self.id)
        }
    }
}

/// The Windows directory, where RP121032.INI and the vendor INIs live.
pub fn windows_dir() -> PathBuf {
    for var in ["WINDIR", "SystemRoot"] {
        if let Ok(d) = std::env::var(var) {
            if !d.is_empty() {
                return PathBuf::from(d);
            }
        }
    }
    PathBuf::from(r"C:\Windows")
}

/// A 32-bit process on 64-bit Windows gets SysWOW64 redirected from System32, so System32 is the
/// right probe; SysWOW64 is listed for the odd installer that writes there directly.
pub fn dll_search_dirs(win: &Path) -> Vec<PathBuf> {
    vec![
        win.join("System32"),
        win.join("SysWOW64"),
        win.to_path_buf(),
    ]
}

pub fn find_dll(win: &Path, api_id: &str) -> String {
    for dir in dll_search_dirs(win) {
        let p = dir.join(format!("{api_id}.DLL"));
        if p.is_file() {
            return p.to_string_lossy().into_owned();
        }
    }
    String::new()
}

fn add_proto(api: &mut Rp1210Api, proto: String) {
    if !api.j1939_protocols.contains(&proto) {
        api.j1939_protocols.push(proto);
    }
}

impl Rp1210Api {
    pub fn from_ini(api_id: &str, ini: &Ini, dll_path: &str) -> Rp1210Api {
        let mut api = Rp1210Api {
            id: api_id.to_string(),
            dll_path: dll_path.to_string(),
            name: ini.get(Some("VendorInformation"), Some("Name")),
            ..Default::default()
        };
        for dev_id in ini.get_int_list("VendorInformation", "Devices") {
            let sec = format!("DeviceInformation{dev_id}");
            api.devices.push(Rp1210Device {
                id: ini.get_int(&sec, "DeviceID", dev_id),
                name: ini.get(Some(&sec), Some("DeviceName")),
                description: ini.get(Some(&sec), Some("DeviceDescription")),
            });
        }
        for proto_id in ini.get_int_list("VendorInformation", "Protocols") {
            let sec = format!("ProtocolInformation{proto_id}");
            let s = ini.get(Some(&sec), Some("ProtocolString"));
            if s.is_empty() {
                continue;
            }
            let upper = s.to_uppercase();
            if upper.contains("ISO15765") {
                api.supports_iso15765 = true;
            }
            if !upper.contains("J1939") {
                continue;
            }
            // One connect string per advertised speed; 250 first, which is what an industrial
            // engine runs.
            let speeds = ini.get_list(&sec, "ProtocolSpeed");
            if speeds.is_empty() {
                add_proto(&mut api, s);
                continue;
            }
            if speeds.iter().any(|sp| sp == "250") {
                add_proto(&mut api, format!("{s}:Baud=250"));
            }
            for sp in speeds.iter().filter(|sp| *sp != "250") {
                add_proto(&mut api, format!("{s}:Baud={sp}"));
            }
            add_proto(&mut api, s);
        }
        if api.j1939_protocols.is_empty() {
            add_proto(&mut api, "J1939:Baud=250".into());
            add_proto(&mut api, "J1939".into());
        }
        api
    }

    pub fn from_vendor_ini(win: &Path, api_id: &str) -> Option<Rp1210Api> {
        let ini = Ini::load(&win.join(format!("{api_id}.INI")))?;
        Some(Self::from_ini(api_id, &ini, &find_dll(win, api_id)))
    }

    /// Installed adapters from `<win>\RP121032.INI`.
    pub fn installed(win: &Path) -> Vec<Rp1210Api> {
        let Some(master) = Ini::load(&win.join("RP121032.INI")) else {
            return Vec::new();
        };
        master
            .get_list("RP1210Support", "APIImplementations")
            .iter()
            .filter_map(|id| Self::from_vendor_ini(win, id))
            .collect()
    }

    pub fn device_ids(&self) -> Vec<i32> {
        let mut ids = Vec::new();
        for d in &self.devices {
            if !ids.contains(&d.id) {
                ids.push(d.id);
            }
        }
        // Nothing declared: fall back to the IDs this shop's INLINE 7 has answered on.
        if ids.is_empty() {
            ids = vec![1, 2, 111];
        }
        ids
    }

    pub fn looks_like_inline7(&self) -> bool {
        let hay = format!("{} {}", self.id, self.name).to_uppercase();
        ["CIL7", "INLINE", "CMNSI", "CUMMINS"]
            .iter()
            .any(|k| hay.contains(k))
    }

    /// Cummins INLINE 7 first: that is what this bench runs.
    pub fn preferred(apis: &[Rp1210Api]) -> Option<&Rp1210Api> {
        apis.iter()
            .find(|a| a.looks_like_inline7() && !a.dll_path.is_empty())
            .or_else(|| apis.iter().find(|a| !a.dll_path.is_empty()))
            .or_else(|| apis.first())
    }

    /// Single-DLL fallback for a PC whose RP121032.INI is missing but still has the Cummins DLL.
    pub fn legacy_inline7(win: &Path) -> Rp1210Api {
        Rp1210Api {
            id: "CIL7R32".into(),
            name: "Cummins INLINE 7 (no INI found)".into(),
            dll_path: find_dll(win, "CIL7R32"),
            devices: Vec::new(),
            j1939_protocols: vec!["J1939:Baud=250".into(), "J1939".into()],
            supports_iso15765: true,
        }
    }

    /// What this PC has, INLINE 7 first, then by name.
    pub fn adapters(win: &Path) -> Vec<Rp1210Api> {
        let mut apis = Self::installed(win);
        if apis.is_empty() {
            let legacy = Self::legacy_inline7(win);
            if !legacy.dll_path.is_empty() {
                apis.push(legacy);
            }
        }
        apis.sort_by(|a, b| {
            let ra = !a.looks_like_inline7();
            let rb = !b.looks_like_inline7();
            ra.cmp(&rb).then_with(|| {
                a.to_string()
                    .to_uppercase()
                    .cmp(&b.to_string().to_uppercase())
            })
        });
        apis
    }

    pub fn describe(&self) -> String {
        let devices = if self.devices.is_empty() {
            "(none declared)".to_string()
        } else {
            self.devices
                .iter()
                .map(ToString::to_string)
                .collect::<Vec<_>>()
                .join(", ")
        };
        format!(
            "{}\r\nDLL: {}\r\nDevices: {}\r\nJ1939: {}\r\nISO15765: {}\r\n",
            self,
            if self.dll_path.is_empty() {
                "(not found)"
            } else {
                &self.dll_path
            },
            devices,
            self.j1939_protocols.join(", "),
            if self.supports_iso15765 {
                "yes"
            } else {
                "not advertised"
            }
        )
    }
}
