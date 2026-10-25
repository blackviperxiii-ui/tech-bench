//! Tech Bench portable core (Phase 1 of the cross-platform recode).
//!
//! A std-only Rust port of the C# app's pure logic, so the same code can back a Tauri desktop or
//! mobile shell and a wasm web build. The C# WinForms app stays the shipping build; its SelfTest
//! literal expected values are ported under `tests/` and must keep matching.

pub mod fmt;
pub mod j1939;
pub mod search;
