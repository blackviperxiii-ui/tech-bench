//! Tech Bench RP1210 bridge. RP1210 adapter drivers are 32-bit Windows DLLs, so the bus lives in
//! this small 32-bit sidecar and the app (any bitness, any OS build) talks to it over stdio:
//! one JSON request per line in, one JSON response per line out. See `main.rs` for the protocol.
//!
//! The bridge offers no way to send an arbitrary frame. Every frame it does send passes
//! `tb_core::j1939::guard` right before `RP1210_SendMessage`.

pub mod bus;
#[cfg(windows)]
pub mod dll;
pub mod driver;
pub mod fake;
pub mod rp1210;
