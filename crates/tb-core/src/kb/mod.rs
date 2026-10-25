//! Knowledge-base index and search (port of `src/Features/Kb/KbIndex.cs`).

pub mod index;
pub mod paths;

pub use index::{Hit, KbIndex, MAX_RESULTS};
