//! Knowledge-base index and search (port of `src/Features/Kb/KbIndex.cs`).

pub mod index;
pub mod paths;
pub mod shop;
pub mod sync;
pub mod user_codes;

pub use index::{Hit, KbIndex, MAX_RESULTS};
