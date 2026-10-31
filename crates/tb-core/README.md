# tb-core

Rust port of Tech Bench's portable logic, for the cross-platform recode (Tauri 2 + React + TS).
The C# WinForms app is still the shipping build; this crate lives alongside it.

- std only at runtime (the one dev-dependency, `wasm-bindgen-test`, is wasm32 test-only); builds for native targets and `wasm32-unknown-unknown`.
- `j1939::decode`, `j1939::bam`, `j1939::names`: ports of `src/Core/J1939/*.cs`.
- `fmt::fixed`: `.NET Framework` style `ToString("0.0")` rounding so decoded strings match the C# app.
- Tests in `tests/` reuse the C# SelfTest inputs and literal expected values. The files that
  don't touch the filesystem (decode, bam, names, search, ini) also run on wasm32 under Node.
- CI (`.github/workflows/ci.yml`): `format` and `linux` run fmt, clippy and tests on every PR
  and push; `wasm` runs `wasm-pack test --node` on pushes; `tb-core` runs clippy and tests on
  Windows and macOS on main pushes, nightly and manual runs.

```
cargo test -p tb-core
cargo build -p tb-core --target wasm32-unknown-unknown
wasm-pack test --node crates/tb-core
```
