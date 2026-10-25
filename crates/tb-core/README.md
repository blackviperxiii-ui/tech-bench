# tb-core

Rust port of Tech Bench's portable logic, for the cross-platform recode (Tauri 2 + React + TS).
The C# WinForms app is still the shipping build; this crate lives alongside it.

- std only, no external crates; builds for native targets and `wasm32-unknown-unknown`.
- `j1939::decode`, `j1939::bam`, `j1939::names`: ports of `src/Core/J1939/*.cs`.
- `fmt::fixed`: `.NET Framework` style `ToString("0.0")` rounding so decoded strings match the C# app.
- Tests in `tests/` reuse the C# SelfTest inputs and literal expected values.

```
cargo test -p tb-core
cargo build -p tb-core --target wasm32-unknown-unknown
```
