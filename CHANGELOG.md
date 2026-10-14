# Changelog

## Unreleased

- Rearch stage 6c: move `SyncForm.cs` to `src/Features/Sync/` (path-only).
- Rearch stage 6b: move `CrashForm.cs` to `src/Features/Crash/` (path-only; SelfTest/linux-compile-check inline path updated).
- Rearch stage 6a: begin `src/` feature folders — move `CheatSheetForm.cs` to `src/Features/CheatSheet/` (path-only; list + SourceListTests accept `src\` entries).
- Rearch stage 5: `tools/gen-rsp.bat` / `gen-rsp.sh` build `sources/*.rsp` from `*.list` + `tests/*.cs` globs; `build.bat` / `test.bat` / linux-compile-check call the generator before csc/mcs (x86 and resource flags unchanged).
- Rearch stage 4: `[SelfTestCase]` attribute runner — `SelfTest.Main` discovers cases by Order; decode cases move to `tests/SelfTest.Decode.cs`; feature entry points annotated.
- Rearch stage 3: `SettingsStore` owns the load guard and atomic settings/adapter-prefs/job-strip writes (temp→replace→.bak); Shell / INLINE 7 / WorkOrderControl route through it.
- Rearch stage 2: `tools/linux-compile-check.sh` compiles SelfTest + TechBench with Mono `mcs` so agents catch CS errors without Windows (not a substitute for `test.bat` / CI).
- Rearch stage 1: `SettingsWriteGate` blocks settings/adapter-prefs writes during startup load; shell and INLINE 7 skip persist while restoring saved page/adapter/job; SelfTest covers startup-no-write hash checks.
- Nightly medium 2026-10-14: shop-ops clarity after #26–#35 — About/update dialogs name the active feed (`update-url.txt` / `update-feed-path.txt` / built-in); invalid shop share path no longer looks like a missing private GitHub feed; IdGateway matches updater HTTP policy (no auto-redirect off-host, TLS `|=`); adapter prefs match case-insensitively; Bus snapshot skips PGN/SA lists off the Bus page; docs/cheat sheet catch up to shipped prefs/feed/F1/crash log/Clear codes menu.
