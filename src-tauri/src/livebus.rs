//! Live J1939 over the 32-bit RP1210 bridge (`tb-rp1210-bridge`), which the Windows bundle ships
//! next to the app as a sidecar. The app never touches the adapter itself: it sends the bridge
//! the few named commands below, and the bridge checks every frame against the tb-core send guard.
//!
//! `TECHBENCH_BRIDGE` points at another bridge exe and `TECHBENCH_BRIDGE_FAKE=1` runs it on the
//! built-in fake adapter (scripted ECMs, no hardware); CI smoke uses both. Without them, live bus
//! is offered only on Windows, the one platform with RP1210 drivers.

use std::path::PathBuf;
use std::sync::Mutex;

use serde::Serialize;
use serde_json::Value;
use tauri::State;
use tb_rp1210_bridge::client::{BridgeClient, CALL_TIMEOUT, CLEAR_TIMEOUT};

pub struct Bridge(pub Mutex<Option<BridgeClient>>);

/// What the UI may ask the bridge. Nothing here sends a raw frame; the bridge has no such command.
const ALLOWED: &[&str] = &[
    "hello",
    "adapters",
    "connect",
    "disconnect",
    "snapshot",
    "busLists",
    "requestCodes",
    "pingIdentity",
    "clear",
    "safety",
    "tsc1",
    "dm13",
    "autoRefresh",
    "autoReconnect",
    "clearBusStats",
    "clearHistory",
    "trend",
    "timeline",
    "log",
];

const BRIDGE_NAME: &str = "tb-rp1210-bridge";

fn fake() -> bool {
    std::env::var("TECHBENCH_BRIDGE_FAKE").is_ok_and(|v| v == "1")
}

/// TECHBENCH_BRIDGE, else the sidecar the bundle puts beside the app exe.
fn bridge_exe() -> Option<PathBuf> {
    if let Some(p) = std::env::var_os("TECHBENCH_BRIDGE").map(PathBuf::from) {
        return p.is_file().then_some(p);
    }
    let dir = std::env::current_exe().ok()?.parent()?.to_path_buf();
    let p = dir.join(format!("{BRIDGE_NAME}{}", std::env::consts::EXE_SUFFIX));
    p.is_file().then_some(p)
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct BusCaps {
    available: bool,
    fake: bool,
    /// Why live bus is off, in the words the INLINE 7 page shows.
    reason: String,
}

fn caps() -> BusCaps {
    let fake = fake();
    if !cfg!(windows) && !fake {
        return BusCaps {
            available: false,
            fake,
            reason: "Live bus needs the Windows build: the INLINE 7 adapter only has 32-bit \
                     Windows RP1210 drivers."
                .into(),
        };
    }
    match bridge_exe() {
        Some(_) => BusCaps {
            available: true,
            fake,
            reason: String::new(),
        },
        None => BusCaps {
            available: false,
            fake,
            reason: format!(
                "Live bus is off: {BRIDGE_NAME} was not found next to Tech Bench. Reinstall to \
                 put it back."
            ),
        },
    }
}

#[tauri::command]
pub fn bus_caps() -> BusCaps {
    caps()
}

/// One bridge request. Starts the bridge on first use, and again if it stopped.
#[tauri::command(async)]
pub fn bus_call(
    cmd: String,
    args: Option<Value>,
    bridge: State<'_, Bridge>,
) -> Result<Value, String> {
    if !ALLOWED.contains(&cmd.as_str()) {
        return Err(format!("Not a bus command: {cmd}"));
    }
    let c = caps();
    if !c.available {
        return Err(c.reason);
    }
    let mut slot = bridge.0.lock().unwrap_or_else(|e| e.into_inner());
    if slot.as_mut().is_some_and(|b| !b.alive()) {
        *slot = None;
    }
    if slot.is_none() {
        let exe = bridge_exe().ok_or_else(|| caps().reason)?;
        *slot = Some(BridgeClient::spawn(&exe, c.fake)?);
    }
    let timeout = if cmd == "clear" {
        CLEAR_TIMEOUT
    } else {
        CALL_TIMEOUT
    };
    let b = slot.as_mut().expect("bridge started above");
    let r = b.call(&cmd, args.unwrap_or(Value::Null), timeout);
    if !b.alive() {
        *slot = None;
    }
    r
}

/// App exit: release any TSC1 hold, disconnect and stop the bridge.
pub fn shut_down(bridge: &Bridge) {
    if let Some(b) = bridge.0.lock().unwrap_or_else(|e| e.into_inner()).take() {
        b.quit();
    }
}
