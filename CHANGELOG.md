# Changelog

## Unreleased

- v1.2.27: switch agent tooling back to pstack: vendored `.cursor/skills/` now pstack 0.15.23 (`837067b`), `.cursor/rules/vstack.mdc` -> `pstack.mdc`, AGENTS.md/CLAUDE.md/tech-bench.mdc say always pstack, never vstack. v1.2.26 is reserved for the IntelliDealer card (PR #74), so this takes the next free patch. Lockstep version bump.

- v1.2.25: SelfTest for job persistence: customer and last WO survive a restart (trimmed, null-safe), missing or corrupt settings.json falls back to defaults and recovers on the next save. No production change. Lockstep version bump.

- v1.2.24: SelfTest.Feed pins the exact about wording (`an anonymous manifest (built-in URL`) and diffs linux-compile-check `-resource:` against build.bat `/resource:`. Lockstep version bump.

- v1.2.23: SelfTests for TrendSample/TrendLog and DtcEvent/DtcTimeline (`tests/SelfTest.Trend.cs`). No production change. Lockstep version bump.

- v1.2.22: IdGateway refuses 307/308 redirects after non-GET (POST payload no longer wiped to GET). SelfTest covers POST+307. Lockstep version bump.

- v1.2.21: BusSnapshot.IncludesBusLists — Refresh gates Bus PGN/SA Repopulate on the snapshot flag (not a second 800 ms clock read). Lockstep version bump.

- v1.2.20: Nightly adaptive 2026-10-15 — gateway HTTP hygiene (gzip, keepalive, loopback proxy, 429 floor, body cap, CombineUrl port match); WO AddPhoto/PullAssigned status honesty; media open confined under packet/media; ContainsSecret patterns widened; playbook lockstep pointer to SelfTest.Feed.

- v1.2.19: sync vendored vstack skills in `.cursor/skills/` to vstack `6f2fbd3` (viper-mode, setup-vstack). Lockstep version bump.

- Docs: correct v1.2.13 (no lockstep stamp); playbook lockstep paths → `src/Core/Version/AppVersion.cs` and `tests/SelfTest.Feed.cs`.

- Restore SelfTest About assert for anonymous manifest URL (require both "anonymous manifest" and "built-in URL").

- Stop committing generated `sources/*.rsp` (gitignore + always regenerate); align `gen-rsp.bat` / `gen-rsp.sh` shared header and test-file sort so drift checks can fail for real.

- SelfTestCase runner asserts the exact discovered case count (was `>= 20`), so a dropped `[SelfTestCase]` fails the run.

- v1.2.16: SelfTests for SessionIo.BaseName (blank/symbol tags, sanitize, trim, 40-char cut) and Names.SaLabel / OverrideCount. Test-only plus lockstep version bump.

- v1.2.15: SelfTests for Ini (Parse, HasSection, Get, GetInt, GetList, GetIntList). Test-only plus lockstep version bump.

- v1.2.14: SelfTests for WorkOrder (Key, JobTag, ListLabel, clock line) and UserCode/UserCodes (IsUsable, KeyOf, PathFor). Test-only plus lockstep version bump.

- v1.2.13: always-apply `.cursor/rules/vstack.mdc` plus AGENTS.md/CLAUDE.md line: always use vstack skills (viper-mode first), never pstack. Agent-tooling only — stamp stayed 1.2.12 (no lockstep bump).

- v1.2.12: vendor vstack skills (`242d4f9`) under `.cursor/skills/` for Cursor Cloud agents; CodeBot keeps them in sync. Lockstep version bump.

- Rearch stage 6m: move Safety sources (`CodeClear`, `J1939Clear`, `Updater`, `SafeOpen`, `IdGateway`, `IdSettings`, `ShopSync`) to `src/Safety/` (path-only; needs-human-review; SelfTest.Feed path updated).
- Rearch stage 6l: move `Inline7Control.cs` to `src/Features/Inline7/` (path-only; clear-code UI flagged; SelfTest.Persist path updated).
- Rearch stage 6k: move Shell sources (`Program`, `ShellForm`, `UiLayout`, `Session`, `IdSettingsForm`) to `src/Shell/` (path-only; SelfTest path reads updated). Last non-safety stage 6 folder move.
- Rearch stage 6j: move non-clear J1939 sources to `src/Core/J1939/` (path-only; `J1939Clear.cs` stays at root for Safety).
- Rearch stage 6i: move `AppVersion.cs` to `src/Core/Version/` (path-only; release/installer/smoke/SelfTest paths updated).
- Rearch stage 6h: move Settings sources to `src/Core/Settings/` (path-only; SelfTest.Persist path updated).
- Rearch stage 6g: move Adapters sources to `src/Features/Adapters/` (path-only).
- Rearch stage 6f: move Kb sources to `src/Features/Kb/` (path-only; SelfTest.Open path updated).
- Rearch stage 6e: move WorkOrders sources to `src/Features/WorkOrders/` (path-only; SelfTest path reads updated).
- Rearch stage 6d: move Search feature sources to `src/Features/Search/` (path-only; SelfTest.Open path updated).
- Rearch stage 6c: move `SyncForm.cs` to `src/Features/Sync/` (path-only).
- Rearch stage 6b: move `CrashForm.cs` to `src/Features/Crash/` (path-only; SelfTest/linux-compile-check inline path updated).
- Rearch stage 6a: begin `src/` feature folders — move `CheatSheetForm.cs` to `src/Features/CheatSheet/` (path-only; list + SourceListTests accept `src\` entries).
- Rearch stage 5: `tools/gen-rsp.bat` / `gen-rsp.sh` build `sources/*.rsp` from `*.list` + `tests/*.cs` globs; `build.bat` / `test.bat` / linux-compile-check call the generator before csc/mcs (x86 and resource flags unchanged).
- Rearch stage 4: `[SelfTestCase]` attribute runner — `SelfTest.Main` discovers cases by Order; decode cases move to `tests/SelfTest.Decode.cs`; feature entry points annotated.
- Rearch stage 3: `SettingsStore` owns the load guard and atomic settings/adapter-prefs/job-strip writes (temp→replace→.bak); Shell / INLINE 7 / WorkOrderControl route through it.
- Rearch stage 2: `tools/linux-compile-check.sh` compiles SelfTest + TechBench with Mono `mcs` so agents catch CS errors without Windows (not a substitute for `test.bat` / CI).
- Rearch stage 1: `SettingsWriteGate` blocks settings/adapter-prefs writes during startup load; shell and INLINE 7 skip persist while restoring saved page/adapter/job; SelfTest covers startup-no-write hash checks.
- Nightly medium 2026-10-14: shop-ops clarity after #26–#35 — About/update dialogs name the active feed (`update-url.txt` / `update-feed-path.txt` / built-in); invalid shop share path no longer looks like a missing private GitHub feed; IdGateway matches updater HTTP policy (no auto-redirect off-host, TLS `|=`); adapter prefs match case-insensitively; Bus snapshot skips PGN/SA lists off the Bus page; docs/cheat sheet catch up to shipped prefs/feed/F1/crash log/Clear codes menu.
