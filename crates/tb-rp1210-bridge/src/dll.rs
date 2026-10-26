//! A vendor RP1210 DLL loaded at runtime (port of the loader half of `Rp1210.cs`). Only a 32-bit
//! build can load one: every RP1210 driver is a 32-bit Windows DLL.

use crate::driver::Driver;
use libloading::Library;
use std::ffi::{c_char, c_void, CString};

type FConnect = unsafe extern "system" fn(*mut c_void, i16, *const c_char, i32, i32, i16) -> i16;
type FDisconnect = unsafe extern "system" fn(i16) -> i16;
type FSend = unsafe extern "system" fn(i16, *const u8, i16, i16, i16) -> i16;
type FRead = unsafe extern "system" fn(i16, *mut u8, i16, i16) -> i16;
type FCommand = unsafe extern "system" fn(i16, i16, *mut u8, i16) -> i16;
type FErrorMsg = unsafe extern "system" fn(i16, *mut c_char) -> i16;

pub struct DllDriver {
    connect: FConnect,
    disconnect: FDisconnect,
    send: FSend,
    read: FRead,
    command: Option<FCommand>,
    error_msg: Option<FErrorMsg>,
    // Last: the function pointers above live only as long as the library stays loaded.
    _lib: Library,
}

/// Look an export up by its plain name, then by the stdcall-decorated forms some builds export.
fn bind<T: Copy>(lib: &Library, name: &str, stack_bytes: u32) -> Option<T> {
    for n in [
        name.to_string(),
        format!("{name}@{stack_bytes}"),
        format!("_{name}@{stack_bytes}"),
    ] {
        // SAFETY: `T` is the RP1210 signature for this export; the symbol is copied out as a plain
        // function pointer and `DllDriver` keeps the library loaded for as long as it is used.
        if let Ok(sym) = unsafe { lib.get::<T>(n.as_bytes()) } {
            return Some(*sym);
        }
    }
    None
}

impl DllDriver {
    pub fn load(path: &str) -> Result<DllDriver, String> {
        if cfg!(not(target_pointer_width = "32")) {
            return Err("This bridge is 64-bit. RP1210 drivers are 32-bit only: build it for i686-pc-windows-msvc.".into());
        }
        if !std::path::Path::new(path).is_file() {
            return Err(format!(
                "Driver DLL {path} not found. Install the adapter's RP1210 drivers on this PC."
            ));
        }
        // SAFETY: loading a vendor RP1210 driver runs its DllMain; that is what the driver is for.
        let lib = unsafe { Library::new(path) }.map_err(|e| {
            format!("Windows could not load {path} ({e}). Its own dependencies may be missing: reinstall the adapter drivers.")
        })?;
        let missing = || {
            format!("{path} loaded but is missing the RP1210 entry points. It may not be an RP1210 driver.")
        };
        let connect = bind::<FConnect>(&lib, "RP1210_ClientConnect", 24).ok_or_else(missing)?;
        let disconnect =
            bind::<FDisconnect>(&lib, "RP1210_ClientDisconnect", 4).ok_or_else(missing)?;
        let send = bind::<FSend>(&lib, "RP1210_SendMessage", 20).ok_or_else(missing)?;
        let read = bind::<FRead>(&lib, "RP1210_ReadMessage", 16).ok_or_else(missing)?;
        Ok(DllDriver {
            connect,
            disconnect,
            send,
            read,
            command: bind::<FCommand>(&lib, "RP1210_SendCommand", 16),
            error_msg: bind::<FErrorMsg>(&lib, "RP1210_GetErrorMsg", 8),
            _lib: lib,
        })
    }
}

impl Driver for DllDriver {
    fn connect(&mut self, device: i16, protocol: &str) -> i16 {
        let Ok(p) = CString::new(protocol) else {
            return 134;
        };
        // SAFETY: RP1210_ClientConnect with a null window, a NUL-terminated protocol string and
        // default queue sizes, as RP1210C specifies.
        unsafe { (self.connect)(std::ptr::null_mut(), device, p.as_ptr(), 0, 0, 0) }
    }

    fn disconnect(&mut self, client: i16) {
        // SAFETY: plain value call.
        unsafe {
            (self.disconnect)(client);
        }
    }

    fn send(&mut self, client: i16, msg: &[u8]) -> i16 {
        // SAFETY: the driver reads exactly `msg.len()` bytes; blocking send, no notification.
        unsafe { (self.send)(client, msg.as_ptr(), msg.len() as i16, 0, 1) }
    }

    fn read(&mut self, client: i16, buf: &mut [u8]) -> i16 {
        // SAFETY: the driver writes at most `buf.len()` bytes; non-blocking read.
        unsafe {
            (self.read)(
                client,
                buf.as_mut_ptr(),
                buf.len().min(i16::MAX as usize) as i16,
                0,
            )
        }
    }

    fn command(&mut self, cmd: i16, client: i16, data: &mut [u8]) -> Option<i16> {
        let f = self.command?;
        // SAFETY: the driver reads / writes at most `data.len()` bytes of the command buffer.
        Some(unsafe { f(cmd, client, data.as_mut_ptr(), data.len() as i16) })
    }

    fn error_text(&mut self, code: i16) -> Option<String> {
        let f = self.error_msg?;
        // RP1210C: the message buffer is at least 80 bytes; give it plenty.
        let mut buf = vec![0u8; 256];
        // SAFETY: the driver writes a NUL-terminated string into the buffer.
        unsafe { f(code, buf.as_mut_ptr().cast()) };
        let end = buf.iter().position(|&b| b == 0).unwrap_or(buf.len());
        let text = String::from_utf8_lossy(&buf[..end]).trim().to_string();
        (!text.is_empty()).then_some(text)
    }
}
