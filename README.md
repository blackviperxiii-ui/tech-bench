# Tech Bench

Windows shop app for air compressor techs: **knowledge-base search** plus **Cummins INLINE 7 / J1939** in one window.

Current stamp: **1.2.6** (Help → About). The field database ships with the app: fault codes, iFix service access, the USB manual index, rental filter/oil charts, and equipment dims. PDF manuals stay on their original paths.

## What it does

- Search fault codes, manuals, rental filters/oil, equipment dims, and (if present) iFix service passwords
- Open PDFs from the USB/Doosan/IR index
- INLINE 7 page: live RPM, DM1/DM2, code reset, bus monitor, snapshots
- **Trend** strip chart of RPM / coolant / oil / battery / fuel rate, with the lamp-on periods shaded
- **Timeline** of every code that came and went, with the RPM at the moment it latched
- **Unit history** — what this model/serial has shown in past saved sessions
- **Report** — printable one-page summary for a work order
- Job strip (model/serial) prefixes saved sessions and scopes the history
- Remembers last model/serial and window size in `%LocalAppData%\TechBench\settings.json`
- Help → Check for updates: public `latest.json` + SHA-256 of `TechBench.exe`, swapped in the **install folder** by a tiny `.cmd` after the window closes (never mid-session, never a GitHub token)
- **Shop sync** — two-way share of tech notes, user codes, and shop files through the knowledge-base folder or a USB/network folder you pick in Shop → Sync
- **Work orders** — pick a WO, fill customer/model/serial, notes, photos, attach the diagnostic report, share the packet with other techs, shop log on/off. IntelliDealer Azure API Gateway log on / log off / sign off / multimedia post-back only when dealer credentials are in Shop → IntelliDealer

A missing knowledge base does not block launch — the search status line says the index is empty.

## Install (shop PC)

Run `TechBench-Setup-<version>.exe` (Windows installer CI artifact). It does **not** need administrator.

- Puts `TechBench.exe`, tab icons, and `air-compressor-kb` (the field database) in `%LocalAppData%\Programs\TechBench`
- Start Menu shortcut (Uninstall is there too)
- Desktop shortcut unless you untick it

That folder is user-writable, so **Help → Check for updates** replaces the installed exe in place. Do not copy `TechBench.exe` onto the Desktop after that — you would update the wrong file.

INLINE 7 / other RP1210 adapters still need the **vendor** driver package (Cummins, Noregon, …). Those installers are usually machine-wide and often ask for admin. Tech Bench itself does not ship those DLLs and does not ask for elevation (`asInvoker`).

## Build (32-bit — required for INLINE 7)

Windows, .NET Framework 4.x:

```bat
cd Documents\TechBench
build.bat
```

That produces `TechBench.exe`. Prefer the Setup exe above for shop PCs.

`/platform:x86` is not optional: RP1210 adapter drivers are 32-bit only. The Adapters page tells you if the running process is wrong.

### Release (hash + latest.json + installer)

```bat
release.bat
```

Writes:

- `TechBench.exe` and `latest.json` (`version`, `sha256`, `url`) for the in-app updater
- `dist\TechBench-Setup-<version>.exe` — Inno Setup per-user installer (same version stamp as Help → About)

Building the Setup exe needs the Inno Setup compiler (`ISCC.exe`). `installer\build.bat` looks in the usual install paths, then downloads a local copy into `tools\innosetup` (gitignored) if needed. Shop PCs never run that step — they only run the finished Setup exe.

Upload **TechBench.exe** and **latest.json** to the public dist repo [blackviperxiii-ui/tech-bench-dist](https://github.com/blackviperxiii-ui/tech-bench-dist). Shop PCs download them anonymously. Hand techs the Setup exe for first install (and for machines that never had a copy).

Built-in updater URLs (this private source repo is not the feed):

- `latest.json`: `https://github.com/blackviperxiii-ui/tech-bench-dist/releases/latest/download/latest.json`
- `TechBench.exe`: `https://github.com/blackviperxiii-ui/tech-bench-dist/releases/download/v{VERSION}/TechBench.exe`

CI publishes those two files after `release.bat` when repository secret `DIST_REPO_TOKEN` is set (a PAT with `contents:write` on `tech-bench-dist`). `GITHUB_TOKEN` cannot create releases on another repo; without `DIST_REPO_TOKEN` the job still builds and asserts PE i386, and you upload the two files onto the `v{VERSION}` dist release by hand. On a shop PC you can override the feed with `update-url.txt` next to the **installed** exe (one URL, `#` comments allowed).

The updater never sends credentials and the installer does not contain a GitHub token. If an INLINE 7 session is live, install is refused until you disconnect; a verified `TechBench.exe.new` applies on the next cold start, in the same folder the Setup exe used.

## Test

```bat
test.bat
```

Offline checks: J1939 decoding, BAM reassembly, RP1210 adapter discovery, trend log, fault timeline, unit history, KB load/search, the shipped field database (155 / 1,424 / 2,147 / 63 / 37), user-code round-trip, snapshot diff, report text, settings, updater, Inno Setup script (per-user, no token), two-way shop sync, work-order packets, shop share, and IntelliDealer gateway (no live DMS). No adapter needed. Sample rows are built in `%TEMP%`; the shipped database is the `kb\` folder.

## Knowledge base

A normal install already has the database. The Setup exe copies `air-compressor-kb` next to `TechBench.exe`, and the same JSON is embedded in the exe. Search reads that copy. No Google Drive or OneDrive folder is required.

Record counts from the 2026-08-11 field database (`kb\data\INDEX.json`): 155 fault codes, 1,424 service-access rows, 2,147 USB manual index entries, 63 filter/oil rows, 37 equipment rows.

The app looks for `data\kb.json` in this order:

1. `TECHBENCH_KB`, or `kb-path.txt` next to `TechBench.exe` (one path per line, `#` comments allowed)
2. `air-compressor-kb` next to the exe (what the installer writes)
3. The old `Documents\air-compressor-kb` / OneDrive Documents location, if that folder is still there and the install copy is missing
4. The copy embedded in the exe, written to `%LocalAppData%\TechBench\air-compressor-kb`

A malformed file in `data\` only costs that section — the status line under the search box names the file that failed. Launch is not blocked if the folder is missing.

Manual hits point at the original PDF paths from the shop PC that built the index. Those PDFs are not inside the installer.

### Work orders (Mobile Tech in-app)

Stay in Tech Bench instead of ID Mobile Access:

- **Work orders** page and the WO picker on the job strip
- Notes, photos, INLINE 7 shots, and the diagnostic report attach to that WO number
- **Share with shop** copies the packet to `data\shop\_shared\work-orders` (rides along if the knowledge base is already on OneDrive) and to a share folder you pick in Shop → IntelliDealer. Same `data\shop` tree the two-way sync branch uses — this does not overwrite that work.
- **Log on / log off** write a shop timestamp on the packet. They post to IntelliDealer only when Azure API Gateway credentials are saved.
- **Sign off** is refused until the gateway returns success. Tech Bench will not fake payroll.

Drop `id-work-orders.json` (or `.csv`) next to `TechBench.exe` or in the KB `data` folder for a file-backed assigned list. Credentials live in `%LocalAppData%\TechBench\id-settings.json` (subscription key and OAuth secret are DPAPI-protected). Optional `id-api.json` next to the exe may overlay operation paths — not secrets.

### Adding what you learn

**Add code to KB** (or Ctrl+N) writes `data\shop\{your-name}\user-codes.json`. The index merges every tech's folder on load. Older `data\user-codes.json` is still read (and copied into your shop folder on the first sync).

**Add note** / **Add file** (Shop menu) drop a `.txt` or a copy of a PDF/photo into that same shop folder.

### Sharing with another tech

Two-way, no GitHub token, no extra installer:

1. If `air-compressor-kb` already lives in a shared OneDrive folder, you are done. Each PC uses a tech name (Shop → Sync, defaults to the Windows user). Status bar shows sync state.
2. If each bench has its own copy, Shop → Sync → Browse and point both PCs at the same USB stick, network share, or extra folder. **Sync now** copies `data\shop`, `data\notes`, and `data\files` both ways.

Same user-code (or the same file) edited on both sides is listed as a conflict. Pick **Keep mine**, **Keep theirs**, or **Keep both** — the app never last-write-wins a note.

`sync-path.txt` next to `TechBench.exe` (one path, `#` comments allowed) sets the sync folder without opening the dialog.

### Naming SPNs without a rebuild

Optional `data\j1939-names.json` extends or overrides the built-in SPN / PGN / source-address labels:

```json
{
  "spn": { "1761": "DEF tank level", "4001": "Shop-added SPN" },
  "pgn": { "0xFE56": "DEF tank 1" },
  "sa":  { "48": "Compressor controller" }
}
```

Keys may be decimal or `0x` hex.

## INLINE 7

Close **USB-Link 3 Explorer** and Guidanz before Connect, or the adapter stays locked.

Adapters are discovered from `RP121032.INI`, so any installed RP1210 vendor DLL shows up in the **Adapter** box and on the **Adapters** page — not just the Cummins INLINE 7. Device IDs and baud rates come from the vendor's own INI rather than being guessed.

All adapter traffic runs on a background thread, so a code reset no longer freezes the window.

The **Module** box picks whose DM1/DM2 the code lists show. On a portable compressor the controller (usually SA 48) has its own faults, separate from the engine's. **Reset all codes** / **Clear previous** / **Clear codes after repair** send DM11/DM3 to the engine (SA 0 and the detected engine SA), the compressor controller (SA 48), and broadcast, then re-request DM1/DM2 and the DEF/SCR tank message (PGN FE56) from those same addresses. Broadcast-only was not enough for SA 48.

The INLINE 7 strip shows DEF level (SPN 1761), tank temp (SPN 3031), SCR inducement (SPN 5246), and the DEF low-level lamp (SPN 5245) when PGN FE56 arrives. Until then each line says **no data**.

This tool does not disable DEF/SCR or Red Stop. Those lamps follow active DTCs. There is no public SAE routine that resets DEF dosing without disabling SCR, so Tech Bench does not invent one.

## Where files go

`Desktop\TechBench-sessions` (falls back to Documents, then LocalAppData):

- `<job>_session_<stamp>.txt` — readable summary
- `..._readings.csv`, `..._dtcs.csv`, `..._trend.csv`, `..._timeline.csv`
- `shot_<stamp>.png` — screenshots
- `techbench-errors.log` — anything unexpected, worth sending along with a bug report

The `_dtcs.csv` files are what the Unit history tab reads back, so saving a session is what builds fleet history.

## Keyboard

| Key | Action |
|---|---|
| Ctrl+F or F3 | jump to the search box |
| Ctrl+N | add a code to the knowledge base |
| Shop → Sync | pick a folder, run two-way sync, resolve conflicts |
| Shop → IntelliDealer | Azure API Gateway credentials for live WO / clock |
| Enter (search box) | search now |
| Down (search box) | move into the results |

## License

Private shop tool. Keep the password DB off GitHub.
