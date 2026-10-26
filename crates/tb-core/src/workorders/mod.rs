//! Work orders: the WO-keyed job packet, its media path guard, and the file-backed store.

pub mod model;
pub mod store;

pub use model::{resolve_media_path, WorkOrder, WorkOrderClock};
pub use store::{ShopConflict, WorkOrderStore};
