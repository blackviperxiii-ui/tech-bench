# Tech Bench

Windows shop app for air compressor techs: **knowledge-base search** plus **Cummins INLINE 7 / J1939** in one window.

Current stamp: **1.1.0** (Help → About). Does **not** ship manuals, filter charts, or service passwords. Those stay in your local `air-compressor-kb` folder.

## What it does

- Search fault codes, manuals, rental filters/oil, equipment dims, and (if present) iFix service passwords
- Open PDFs from the USB/Doosan/IR index
- INLINE 7 tab: live RPM, DM1/DM2, code reset, bus monitor, snapshots
- **Trend** strip chart of RPM / coolant / oil / battery / fuel rate, with the lamp-on periods shaded
- **Timeline** of every code that came and went, with the RPM at the moment it latched
- **Unit history** — what this model/serial has shown in past saved sessions
- **Report** — printable one-page summary for a work order
- Job strip (model/serial) prefixes saved sessions and scopes the history
- Remembers last model/serial and window size in `%LocalAppData%\TechBench\settings.json`
- Help → Check for updates: public `latest.json` + SHA-256 of `TechBench.exe`, swapped by a tiny `.cmd` after the window closes (never mid-session, never a GitHub token)
- **Shop sync** — two-way share of tech notes, user codes, and shop files through the knowledge-base folder or a USB/network folder you pick in Shop → Sync

A missing knowledge base does not block launch — the search status line says the index is empty.

## Build (32-bit — required for INLINE 7)

Windows, .NET Framework 4.x:

```bat
cd Documents\TechBench
build.bat
```

That produces `TechBench.exe`. Desktop shortcut can point at it.

`/platform:x86` is not optional: RP1210 adapter drivers are 32-bit only. The Adapters tab tells you if the running process is wrong.

### Release (hash + latest.json)

```bat
release.bat
```

Writes `TechBench.exe` and `latest.json` (`version`, `sha256`, `url`). Upload **both** files to a **public** HTTPS location. Shop PCs download them anonymously.

This GitHub repo is private, so `github.com/.../releases/...` URLs will 404 without a login. Host the two files somewhere GET works without auth (public dist repo, object storage, etc.). On a shop PC you can override the feed with `update-url.txt` next to the exe (one URL, `#` comments allowed).

The updater never sends credentials. If an INLINE 7 session is live, install is refused until you disconnect; a verified `TechBench.exe.new` applies on the next cold start.

## Test

```bat
test.bat
```

Offline checks: J1939 decoding, BAM reassembly, RP1210 adapter discovery, trend log, fault timeline, unit history, KB load/search, user-code round-trip, snapshot diff, report text, settings, updater, two-way shop sync. No adapter and no knowledge base needed — it builds its own sample data in `%TEMP%`.

## Knowledge base

Expected at `Documents\air-compressor-kb` (OneDrive Documents also works).
The app looks for `data\kb.json` and the rest of that tree. It never copies the KB into this repo.

If it lives somewhere else, point at it either way:

- `kb-path.txt` next to `TechBench.exe`, one path per line (`#` comments allowed)
- a `TECHBENCH_KB` environment variable

A malformed file in `data\` only costs that section — the status line under the search box names the file that failed. Launch is not blocked if the folder is missing.

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

Adapters are discovered from `RP121032.INI`, so any installed RP1210 vendor DLL shows up in the **Adapter** box and on the **Adapters** tab — not just the Cummins INLINE 7. Device IDs and baud rates come from the vendor's own INI rather than being guessed.

All adapter traffic runs on a background thread, so a code reset no longer freezes the window.

The **Module** box picks whose DM1/DM2 the code lists show. On a portable compressor the controller (usually SA 48) has its own faults, separate from the engine's.

This tool does not disable DEF/SCR or Red Stop. Those lamps follow active DTCs.

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
| Enter (search box) | search now |
| Down (search box) | move into the results |

## License

Private shop tool. Keep the password DB off GitHub.
