//! Work orders: the WO-keyed job packet and its media path guard.

pub mod model;

pub use model::{resolve_media_path, WorkOrder, WorkOrderClock};
