# Tech Bench

[![Windows installer](https://github.com/blackviperxiii-ui/tech-bench/actions/workflows/windows-installer.yml/badge.svg?branch=main)](https://github.com/blackviperxiii-ui/tech-bench/actions/workflows/windows-installer.yml)
[![pii-guard](https://github.com/blackviperxiii-ui/tech-bench/actions/workflows/pii-guard.yml/badge.svg?branch=main)](https://github.com/blackviperxiii-ui/tech-bench/actions/workflows/pii-guard.yml)

Windows WinForms shop tool for air-compressor knowledge-base search and Cummins INLINE 7 / SAE J1939 diagnostics.

## Overview

Tech Bench gives air-compressor technicians offline service reference lookup and live RP1210 / SAE J1939 diagnostics in a single 32-bit Windows desktop window. It runs per-user (`asInvoker`) without administrator rights, cloud sign-in, or an active network connection.

Current stamp: **1.2.22** (Help → About).

The bundled field database ships both inside the installer (`air-compressor-kb`) and embedded in `TechBench.exe`, covering fault codes, iFix service access, the USB manual index, rental filter/oil charts, and equipment dimensions. Indexed PDF manuals stay at their original paths on the shop PC, and a missing knowledge base never blocks launch.

## Table of Contents

- [Overview](#overview)
- [Features](#features)
- [Getting Started](#getting-started)
- [Usage](#usage)
- [Configuration](#configuration)
- [Architecture](#architecture)
- [Documentation](#documentation)
- [Contributing](#contributing)
- [License](#license)

## Features

- **Knowledge-base search**: Search 155 fault codes, 2,147 USB/Doosan/IR manual index entries, 63 rental filter/oil rows, 37 equipment dimension rows, and (if present) 1,424 iFix service-access entries (`kb\data\INDEX.json`), and open indexed PDFs safely via `SafeOpen`.
- **INLINE 7 & RP1210 diagnostics**: Live RPM, per-module DM1/DM2 fault lists (engine SA 0, compressor controller SA 48, DEF/aftertreatment modules), PGN `FE56` DEF level/temp/SCR inducement readout, bus monitor, and session snapshots on a background worker thread.
- **Trend, Timeline, and Unit History**: Live strip chart of RPM, coolant temperature, oil pressure, battery voltage, and fuel rate with lamp-on periods shaded; chronological fault timeline with latched RPM; and fleet history aggregated across saved `_dtcs.csv` sessions by model/serial.
- **Confirmed J1939 code clearing**: Direct J1939-73 Request (PGN 59904) for DM11 (active) and DM3 (previously active) per module, waiting for ACK/NACK and re-reading DM1, DM2, and PGN `FE56` to report what cleared and what returned.
- **Work orders & IntelliDealer**: Attach customer/model/serial metadata, tech notes, photos, INLINE 7 snapshots, and printable one-page job reports to a work order; share packets across benches; and post log on / log off / sign off / multimedia to the IntelliDealer Azure API Gateway when dealer credentials are configured.
- **Two-way shop sync**: Share user-added fault codes, notes, and files across shop PCs via a shared folder, USB drive, or network share (`Shop → Sync`) with explicit conflict resolution (**Keep mine**, **Keep theirs**, **Keep both**).
- **Verified per-user updates**: SHA-256 verified `latest.json` swap in `%LocalAppData%\Programs\TechBench` applied after exit via a shop share (`update-feed-path.txt`) or manifest URL (`update-url.txt`), never mid-session and never using a GitHub token.

## Getting Started

### Prerequisites

- **Shop PC (running the installer)**:
  - Windows 7 SP1 or later
  - 32-bit .NET Framework 4.x runtime (4.5 or later Full `Release` registry key checked by Setup)
  - Vendor 32-bit RP1210 adapter drivers (Cummins, Noregon, etc.) for live bus traffic; Tech Bench does not bundle vendor DLLs
- **Building from source**:
  - Windows with the in-box `.NET Framework v4.0.30319` C# compiler (`%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe`) and PowerShell
  - Inno Setup 6 (`ISCC.exe`) for building the installer ([installer/build.bat](installer/build.bat) fetches a local copy into gitignored `tools\innosetup` via [installer/fetch-iscc.ps1](installer/fetch-iscc.ps1) if not already installed)

### Install on a Shop PC

Download `TechBench-Setup-<version>.exe` from the private GitHub release `v<version>` or from the `Windows installer` GitHub Actions artifact, and run it on the shop PC. No administrator account or GitHub login is required. See [docs/shop-rollout.md](docs/shop-rollout.md) for upgrade and rollback steps.

- Installs `TechBench.exe`, icons, and `air-compressor-kb` to `%LocalAppData%\Programs\TechBench`
- Creates Start Menu shortcuts (including **Uninstall Tech Bench**) and an optional Desktop shortcut (preserved across upgrades)
- Launch from the Start Menu or Desktop shortcut rather than copying `TechBench.exe` onto the Desktop, so upgrades and in-place updates target the active copy

### Build from Source

`/platform:x86` is mandatory because RP1210 adapter drivers are 32-bit only (the **Adapters** tab warns if the process bitness is wrong).

```bat
git clone https://github.com/blackviperxiii-ui/tech-bench.git
cd tech-bench
build.bat
```

[build.bat](build.bat) runs [tools/gen-rsp.bat](tools/gen-rsp.bat) to generate gitignored `sources\*.rsp` response files from [sources/core.list](sources/core.list) and [sources/app.list](sources/app.list), then compiles `TechBench.exe` with embedded KB JSON and cheat-sheet resources.

### Build Release Artifacts

```bat
release.bat
```

[release.bat](release.bat) compiles `TechBench.exe`, verifies the PE32 `i386` / `32BITREQUIRED` headers with [tools/assert-pe-i386.ps1](tools/assert-pe-i386.ps1), writes `latest.json` (`version`, `sha256`, `url`), and builds `dist\TechBench-Setup-<version>.exe` via [installer/techbench.iss](installer/techbench.iss). Attach `dist\TechBench-Setup-<version>.exe`, `TechBench.exe`, and `latest.json` to the private GitHub release `v<version>` so technicians can install or upgrade via the shop Setup ([docs/shop-rollout.md](docs/shop-rollout.md)).

## Usage

### Launch and Headless Smoke Check

```bat
TechBench.exe
TechBench.exe --smoke
TechBench.exe --smoke --smoke-out smoke.txt
```

`--smoke` verifies the stamped version and loads the bundled knowledge base without opening a window or touching user settings; `--smoke-out <path>` writes the one-line status report verified by [tools/install-smoke.ps1](tools/install-smoke.ps1) in CI.

### Knowledge Base, Notes, and Shop Sync

- **Add code to KB** (`Ctrl+N`) writes `data\shop\{your-name}\user-codes.json`, and the index merges every technician's folder on load (legacy `data\user-codes.json` is still read and migrated on first sync).
- **Add note** / **Add file** (`Shop` menu) stores `.txt` notes and copied PDFs/photos under your shop folder.
- **Two-way sync**: If `air-compressor-kb` already lives in a shared OneDrive folder, each PC sets its tech name in `Shop → Sync` (defaults to the Windows username). Otherwise, open `Shop → Sync → Browse`, select a shared USB drive or network folder on each bench, and click **Sync now** to synchronize `data\shop`, `data\notes`, and `data\files` in both directions without overwriting conflicting edits.

### Work Orders

Use the job strip (model, serial, customer, work order) and the **Work orders** page to manage field packets in-app:

- Attach notes, photos, INLINE 7 snapshots, and the printable one-page diagnostic **Report** to the active work order.
- **Share with shop** copies the packet to `data\shop\_shared\work-orders` and to any share folder configured in `Shop → IntelliDealer`.
- **Log on / log off** record local packet timestamps and post to the IntelliDealer Azure API Gateway only when dealer credentials are saved in `Shop → IntelliDealer`.
- **Sign off** requires a successful gateway response and refuses to fake payroll offline.

### INLINE 7 and J1939 Code Clearing

Close **USB-Link 3 Explorer**, Guidanz, and any other RP1210 tool before clicking **Connect** so the adapter is not locked. Adapters, device IDs, and baud rates are discovered automatically from `RP121032.INI`. On connect, the Log reports whether the adapter claimed tool address SA 249 (required to transmit clear requests). The **Module** selector picks whose DM1/DM2 fault list is displayed (engine SA 0 vs. compressor controller SA 48), and the status strip displays DEF level (SPN 1761), tank temp (SPN 3031), SCR inducement (SPN 5246), and the DEF low-level lamp (SPN 5245) once PGN `FE56` arrives (`no data` until received).

Under the **Clear codes** menu, **Reset all codes** and **Clear codes after repair** clear active then previously active codes, while **Clear previous** clears previously active codes only:

1. Addresses each module directly (engine SA 0 and detected engine SA, compressor controller SA 48, and any other module seen sending DM1/DM2 such as a DEF/aftertreatment controller) before broadcasting to SA 255.
2. Sends a J1939-73 Request (PGN 59904) for DM11, then DM3 (never raw DM11/DM3 data frames), retrying once if an active bus module stays silent.
3. Waits 1 second for modules to re-evaluate, re-reads DM1, DM2, and PGN `FE56`, and writes a per-module before/after report (including returned codes, lamp state, and DEF level/temp/SCR inducement) to the **Codes** tab, **Log**, saved session, and printed **Report**.

Interpreting clear results:

- **Refused / access denied**: Many ECMs require key ON with engine OFF.
- **Came straight back**: The clear succeeded, and the underlying fault is still active.
- **Did not answer**: The controller does not accept J1939 clears (some compressor controllers clear only from their front-panel keypad).
- **SCR inducement (SPN 5246)**: Not cleared by DM11/DM3; the ECM clears inducement after valid DEF level/temperature/quality readings and a key cycle, or via OEM service routines. Tech Bench never disables DEF/SCR or Red Stop lamps.

### Keyboard Shortcuts

| Key | Action |
|---|---|
| F1 | shop cheat sheet (Help → Keyboard / shop cheat sheet) |
| Ctrl+F or F3 | Jump to the search box |
| Enter (search box) | Run search immediately |
| Down (search box) | Move focus into search results |
| Ctrl+N | Add a fault code to the knowledge base |
| Shop → Sync | Select sync folder, run two-way sync, resolve conflicts |
| Shop → IntelliDealer | Configure Azure API Gateway credentials for live WO / clock |

### Testing

Run the offline self-test and WinForms layout audit on Windows (no RP1210 adapter or live DMS required):

```bat
test.bat
```

[test.bat](test.bat) compiles and runs `SelfTest.exe` ([SelfTest.cs](SelfTest.cs) and [tests/](tests/)) followed by `LayoutAudit.exe` ([tools/LayoutAudit.cs](tools/LayoutAudit.cs)), covering J1939 decoding, BAM reassembly, simulated multi-ECM DM11/DM3 clears, RP1210 INI discovery, trend/timeline/history, KB search and shipped record counts, shop sync conflicts, work-order packets, updater SHA-256/redirect/origin rules, Inno Setup script checks, and response-file drift.

On Linux machines without Windows `csc.exe`, run the Mono C# 5 compile check (`mono-devel`) to catch syntax or source-list errors before opening a pull request (does not replace `test.bat` or the Windows CI `installer` job):

```bash
tools/linux-compile-check.sh
```

Run the personal-info guard locally before pushing:

```bash
python3 scripts/pii-guard/pii_guard.py origin/main
```

## Configuration

### Knowledge Base Resolution Order

Tech Bench locates `data\kb.json` in the following order (a malformed JSON file only skips that section and reports the filename in the search status bar):

1. `TECHBENCH_KB` environment variable, or paths listed in `kb-path.txt` beside `TechBench.exe` (one path per line, `#` comments allowed)
2. `air-compressor-kb` beside `TechBench.exe` (installed by Setup)
3. Legacy `Documents\air-compressor-kb` (including OneDrive `Documents`) when the install copy is absent
4. Embedded resource copy extracted to `%LocalAppData%\TechBench\air-compressor-kb`

### Optional Sidecar and Override Files

| File | Location | Purpose |
|---|---|---|
| `update-url.txt` | Beside `TechBench.exe` | Override manifest URL (`latest.json`) for `Help → Check for updates` (`#` comments allowed; built-in default URL is unpublished) |
| `update-feed-path.txt` | Beside `TechBench.exe` | Point `Help → Check for updates` at a UNC or drive folder containing `latest.json` and `TechBench.exe` (SHA-256 verified before offer) |
| `kb-path.txt` | Beside `TechBench.exe` | Custom knowledge-base root path(s), checked after `TECHBENCH_KB` |
| `sync-path.txt` | Beside `TechBench.exe` | Default two-way shop sync folder without opening the `Shop → Sync` dialog |
| `id-work-orders.json` / `.csv` | Beside `TechBench.exe` or in `data\` | File-backed assigned work-order list |
| `id-api.json` | Beside `TechBench.exe` | Optional IntelliDealer API operation path overrides (no secrets) |
| `data\j1939-names.json` | Inside active KB folder | Extend or override built-in SPN, PGN, and source-address labels without rebuilding |

`Help → About` displays which update feed source is active (default unpublished URL `https://github.com/blackviperxiii-ui/tech-bench-dist/releases/latest/download/latest.json`, `update-url.txt`, or `update-feed-path.txt`). Update installation is refused while an INLINE 7 session is live; a verified `TechBench.exe.new` swaps into the install folder after quit or on the next cold start.

Example `data\j1939-names.json` (keys accept decimal or `0x` hex):

```json
{
  "spn": { "1761": "DEF tank level", "4001": "Shop-added SPN" },
  "pgn": { "0xFE56": "DEF tank 1" },
  "sa":  { "48": "Compressor controller" }
}
```

### Local Data and Session Paths

- **User state (`%LocalAppData%\TechBench\`)**:
  - `settings.json`: Last model, serial, customer, work order, and window dimensions
  - `adapter-prefs.json`: Last RP1210 adapter ID and active shell / INLINE 7 tab
  - `id-settings.json`: IntelliDealer gateway configuration (subscription key and OAuth secret encrypted with Windows DPAPI)
  - `sync.json`, `sync-state-*.json`, and `work-orders\`: Sync state and local work-order packets
- **Saved sessions (`Desktop\TechBench-sessions`, falling back to `Documents` then `%LocalAppData%\TechBench-sessions`)**:
  - `<job>_session_<stamp>.txt` plus `_readings.csv`, `_dtcs.csv` (read by **Unit history**), `_trend.csv`, and `_timeline.csv`
  - `shot_<stamp>.png` screenshots and `techbench-errors.log` non-fatal error log
- **Crash log**:
  - `techbench-crash.log` beside `TechBench.exe` records unhandled fatal exceptions captured by `CrashForm`

## Architecture

- [src/Core/](src/Core/): J1939 PGN/SPN decoding and BAM transport reassembly ([src/Core/J1939/](src/Core/J1939/)), atomic settings persistence ([src/Core/Settings/](src/Core/Settings/)), and version/smoke metadata ([src/Core/Version/AppVersion.cs](src/Core/Version/AppVersion.cs))
- [src/Features/](src/Features/): RP1210 DLL binding, background bus worker, trend/timeline/history, and job reports ([src/Features/Adapters/](src/Features/Adapters/)); INLINE 7 tab ([src/Features/Inline7/](src/Features/Inline7/)); KB index and user codes ([src/Features/Kb/](src/Features/Kb/)); search UI ([src/Features/Search/](src/Features/Search/)); two-way sync dialog ([src/Features/Sync/](src/Features/Sync/)); work orders and printing ([src/Features/WorkOrders/](src/Features/WorkOrders/)); cheat sheet and crash dialog
- [src/Safety/](src/Safety/): J1939 DM11/DM3 clear orchestration, hardened `Updater` (`latest.json` caps, origin/redirect rules, SHA-256 staging), `SafeOpen` file-launch guard, `ShopSync` engine, and `IdGateway` / DPAPI `IdSettings`
- [src/Shell/](src/Shell/): WinForms entry point ([src/Shell/Program.cs](src/Shell/Program.cs)), main shell window ([src/Shell/ShellForm.cs](src/Shell/ShellForm.cs)), layout helpers, and session export
- [sources/](sources/): Tracked [sources/core.list](sources/core.list) and [sources/app.list](sources/app.list) manifests compiled into generated `.rsp` files by [tools/gen-rsp.bat](tools/gen-rsp.bat) and [tools/gen-rsp.sh](tools/gen-rsp.sh)
- [kb/data/](kb/data/): Bundled JSON field database and [kb/data/INDEX.json](kb/data/INDEX.json) metadata
- [tests/](tests/) and [SelfTest.cs](SelfTest.cs): Attribute-discovered `[SelfTestCase]` suite paired with [tools/LayoutAudit.cs](tools/LayoutAudit.cs)
- [installer/](installer/): Per-user Inno Setup 6 installer script ([installer/techbench.iss](installer/techbench.iss)) and build helpers

## Documentation

- [docs/shop-rollout.md](docs/shop-rollout.md): Shop PC installation, upgrades, feed overrides, data locations, and rollback guide
- [CHANGELOG.md](CHANGELOG.md): Unreleased and historical release notes
- [docs/specs/](docs/specs/): Versioned engineering specifications (`v1.2.7` through `v1.2.22`)
- [docs/agent-playbook.md](docs/agent-playbook.md): Repository workflow, lockstep version stamps, CI gates, and ECU/updater safety rules

## Contributing

1. Fetch `origin` and branch from the latest `main` (`main` is protected and gated by the `installer` and `pii-guard` status checks).
2. Register any new `.cs` files in [sources/core.list](sources/core.list) or [sources/app.list](sources/app.list), or add test files under [tests/](tests/). Do not commit or hand-edit generated `sources/*.rsp` files.
3. Follow the safety, testing, and 4-spot lockstep version-bump rules in [docs/agent-playbook.md](docs/agent-playbook.md). Never commit `.env*`, tokens, keys/certs, `id-settings*.json`, or personal information.
4. Run `test.bat` on Windows (or `tools/linux-compile-check.sh` on Linux) and `python3 scripts/pii-guard/pii_guard.py origin/main` before opening a pull request.

## License

No `LICENSE` file or open-source SPDX license is published in this repository. Private shop tool; all rights reserved. Keep the password DB off external mirrors and never commit credentials or dealer keys.
