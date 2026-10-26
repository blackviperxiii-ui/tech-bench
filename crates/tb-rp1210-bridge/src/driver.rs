//! The RP1210 calls the bridge makes, as a trait so the same client runs over a vendor DLL, the
//! mock DLL, or the in-process fake adapter. Return codes follow RP1210: 0..=127 is success
//! (a client id for connect), anything else is an error code.

pub trait Driver: Send {
    /// RP1210_ClientConnect.
    fn connect(&mut self, device: i16, protocol: &str) -> i16;
    /// RP1210_ClientDisconnect.
    fn disconnect(&mut self, client: i16);
    /// RP1210_SendMessage, blocking until sent.
    fn send(&mut self, client: i16, msg: &[u8]) -> i16;
    /// RP1210_ReadMessage, non-blocking: the byte count, 0 when nothing is waiting, or a negative error.
    fn read(&mut self, client: i16, buf: &mut [u8]) -> i16;
    /// RP1210_SendCommand; `None` when the driver has no such export.
    fn command(&mut self, cmd: i16, client: i16, data: &mut [u8]) -> Option<i16>;
    /// RP1210_GetErrorMsg; `None` when the driver has no such export or no text.
    fn error_text(&mut self, code: i16) -> Option<String>;
}

pub const CMD_FILTERS_PASS: i16 = 3;
pub const CMD_PROTECT_ADDRESS: i16 = 19;

/// Opens the driver for one adapter (the vendor DLL named in its INI).
pub type Loader =
    Box<dyn FnMut(&tb_core::rp1210::Rp1210Api) -> Result<Box<dyn Driver>, String> + Send>;

/// Stands in until an adapter's driver is loaded; refuses everything.
pub struct NoDriver;

impl Driver for NoDriver {
    fn connect(&mut self, _: i16, _: &str) -> i16 {
        129
    }
    fn disconnect(&mut self, _: i16) {}
    fn send(&mut self, _: i16, _: &[u8]) -> i16 {
        129
    }
    fn read(&mut self, _: i16, _: &mut [u8]) -> i16 {
        -129
    }
    fn command(&mut self, _: i16, _: i16, _: &mut [u8]) -> Option<i16> {
        None
    }
    fn error_text(&mut self, _: i16) -> Option<String> {
        Some("no RP1210 driver loaded".into())
    }
}
