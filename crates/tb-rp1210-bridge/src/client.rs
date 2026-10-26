//! The app's end of the bridge: starts `tb-rp1210-bridge` as a child process and sends it one
//! JSON request at a time. The app has no other way onto the bus, so every frame still goes
//! through the bridge's guard.

use serde_json::{json, Value};
use std::io::{BufRead, BufReader, Write};
use std::path::Path;
use std::process::{Child, ChildStdin, Command, Stdio};
use std::sync::mpsc::{self, Receiver, RecvTimeoutError};
use std::time::Duration;

/// Normal requests answer within a few milliseconds.
pub const CALL_TIMEOUT: Duration = Duration::from_secs(10);
/// A code clear waits for each module's ACK and re-reads the codes; it blocks the bridge until then.
pub const CLEAR_TIMEOUT: Duration = Duration::from_secs(90);

pub const STOPPED: &str = "The adapter bridge stopped.";

pub struct BridgeClient {
    child: Child,
    stdin: ChildStdin,
    lines: Receiver<String>,
    next_id: u64,
}

impl BridgeClient {
    /// Starts the bridge; `fake` runs it on the built-in fake adapter (no hardware).
    pub fn spawn(exe: &Path, fake: bool) -> Result<Self, String> {
        let mut cmd = Command::new(exe);
        if fake {
            cmd.arg("--fake");
        }
        cmd.stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::null());
        #[cfg(windows)]
        {
            use std::os::windows::process::CommandExt;
            // CREATE_NO_WINDOW: the app is a GUI program, so no console pops up for the bridge.
            cmd.creation_flags(0x0800_0000);
        }
        let mut child = cmd
            .spawn()
            .map_err(|e| format!("Could not start the adapter bridge {}: {e}", exe.display()))?;
        let stdin = child.stdin.take().ok_or(STOPPED)?;
        let stdout = child.stdout.take().ok_or(STOPPED)?;
        let (tx, lines) = mpsc::channel();
        std::thread::spawn(move || {
            for line in BufReader::new(stdout).lines() {
                let Ok(line) = line else { break };
                if tx.send(line).is_err() {
                    break;
                }
            }
        });
        Ok(Self {
            child,
            stdin,
            lines,
            next_id: 0,
        })
    }

    /// Sends `{"cmd": cmd, ...args}` and returns the reply's `data`, or its `error`.
    pub fn call(&mut self, cmd: &str, args: Value, timeout: Duration) -> Result<Value, String> {
        self.next_id += 1;
        let id = self.next_id;
        let mut req = match args {
            Value::Object(m) => Value::Object(m),
            Value::Null => json!({}),
            _ => return Err("bridge arguments must be an object".into()),
        };
        req["cmd"] = json!(cmd);
        req["id"] = json!(id);
        writeln!(self.stdin, "{req}")
            .and_then(|_| self.stdin.flush())
            .map_err(|_| STOPPED.to_string())?;
        loop {
            let line = match self.lines.recv_timeout(timeout) {
                Ok(l) => l,
                Err(RecvTimeoutError::Timeout) => {
                    return Err(format!(
                        "The adapter bridge did not answer \"{cmd}\" within {}s.",
                        timeout.as_secs()
                    ))
                }
                Err(RecvTimeoutError::Disconnected) => return Err(STOPPED.into()),
            };
            let Ok(reply) = serde_json::from_str::<Value>(&line) else {
                continue;
            };
            // A reply to an earlier request that timed out arrives late; skip it.
            if reply["id"] != json!(id) {
                continue;
            }
            return if reply["ok"] == json!(true) {
                Ok(reply["data"].clone())
            } else {
                Err(reply["error"]
                    .as_str()
                    .unwrap_or("bridge error")
                    .to_string())
            };
        }
    }

    /// True while the bridge process is still running.
    pub fn alive(&mut self) -> bool {
        matches!(self.child.try_wait(), Ok(None))
    }

    /// Asks the bridge to release TSC1, disconnect and exit; kills it if it does not.
    pub fn quit(mut self) {
        self.shut_down();
    }

    fn shut_down(&mut self) {
        if self.alive() {
            let _ = self.call("quit", Value::Null, Duration::from_secs(2));
            for _ in 0..20 {
                if !self.alive() {
                    return;
                }
                std::thread::sleep(Duration::from_millis(50));
            }
            let _ = self.child.kill();
        }
        let _ = self.child.wait();
    }
}

impl Drop for BridgeClient {
    fn drop(&mut self) {
        self.shut_down();
    }
}
