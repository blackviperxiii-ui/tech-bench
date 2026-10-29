//! A test-only RP1210 driver DLL. It exports the RP1210 C entry points over the bridge's fake
//! adapter (scripted ECMs, no hardware), so CI can load it with the same 32-bit loader the bridge
//! uses for vendor drivers. `MOCK_*` exports let a test read back every frame the bridge sent.
//! Never shipped.

use std::ffi::{c_char, c_void, CStr};
use std::sync::OnceLock;
use tb_rp1210_bridge::driver::Driver;
use tb_rp1210_bridge::fake::FakeBus;

fn bus() -> FakeBus {
    static BUS: OnceLock<FakeBus> = OnceLock::new();
    BUS.get_or_init(FakeBus::default).clone()
}

/// # Safety
/// `protocol` must be null or a NUL-terminated string.
#[no_mangle]
pub unsafe extern "system" fn RP1210_ClientConnect(
    _hwnd: *mut c_void,
    device: i16,
    protocol: *const c_char,
    _tx_size: i32,
    _rx_size: i32,
    _is_app_packetizing: i16,
) -> i16 {
    if protocol.is_null() {
        return 134;
    }
    // SAFETY: the caller passes a NUL-terminated string (RP1210C).
    let p = unsafe { CStr::from_ptr(protocol) }.to_string_lossy();
    bus().connect(device, &p)
}

#[no_mangle]
pub extern "system" fn RP1210_ClientDisconnect(client: i16) -> i16 {
    bus().disconnect(client);
    0
}

/// # Safety
/// `msg` must point to `len` readable bytes.
#[no_mangle]
pub unsafe extern "system" fn RP1210_SendMessage(
    client: i16,
    msg: *const u8,
    len: i16,
    _notify: i16,
    _block: i16,
) -> i16 {
    if msg.is_null() || len < 0 {
        return 141;
    }
    // SAFETY: the caller passes `len` readable bytes.
    let m = unsafe { std::slice::from_raw_parts(msg, len as usize) };
    bus().send(client, m)
}

/// # Safety
/// `buf` must point to `len` writable bytes.
#[no_mangle]
pub unsafe extern "system" fn RP1210_ReadMessage(
    client: i16,
    buf: *mut u8,
    len: i16,
    _block: i16,
) -> i16 {
    if buf.is_null() || len <= 0 {
        return 0;
    }
    // SAFETY: the caller passes `len` writable bytes.
    let b = unsafe { std::slice::from_raw_parts_mut(buf, len as usize) };
    bus().read(client, b)
}

/// # Safety
/// `data` must be null or point to `len` writable bytes.
#[no_mangle]
pub unsafe extern "system" fn RP1210_SendCommand(
    cmd: i16,
    client: i16,
    data: *mut u8,
    len: i16,
) -> i16 {
    let mut empty: [u8; 0] = [];
    let d = if data.is_null() || len <= 0 {
        &mut empty[..]
    } else {
        // SAFETY: the caller passes `len` writable bytes.
        unsafe { std::slice::from_raw_parts_mut(data, len as usize) }
    };
    bus().command(cmd, client, d).unwrap_or(0)
}

/// # Safety
/// `out` must point to at least 80 writable bytes (RP1210C).
#[no_mangle]
pub unsafe extern "system" fn RP1210_GetErrorMsg(code: i16, out: *mut c_char) -> i16 {
    if out.is_null() {
        return 1;
    }
    let text = bus().error_text(code).unwrap_or_default();
    let bytes = text.as_bytes();
    let n = bytes.len().min(79);
    // SAFETY: at most 80 bytes written, as RP1210C guarantees the buffer holds.
    unsafe {
        std::ptr::copy_nonoverlapping(bytes.as_ptr(), out.cast::<u8>(), n);
        *out.add(n) = 0;
    }
    0
}

/// How many messages RP1210_SendMessage has received.
#[no_mangle]
pub extern "system" fn MOCK_SentCount() -> i32 {
    bus().state().sent.len() as i32
}

/// Copy sent message `index` into `buf`; returns its length, or -1.
///
/// # Safety
/// `buf` must point to `len` writable bytes.
#[no_mangle]
pub unsafe extern "system" fn MOCK_SentMessage(index: i32, buf: *mut u8, len: i16) -> i16 {
    let b = bus();
    let s = b.state();
    let Some(m) = s.sent.get(index.max(0) as usize) else {
        return -1;
    };
    if buf.is_null() || (len as usize) < m.len() {
        return -1;
    }
    // SAFETY: `m.len()` bytes fit in the caller's `len`-byte buffer, checked above.
    unsafe { std::ptr::copy_nonoverlapping(m.as_ptr(), buf, m.len()) };
    m.len() as i16
}
