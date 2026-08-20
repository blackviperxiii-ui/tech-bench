# Tech Bench

Windows shop app for air compressor techs: **knowledge-base search** plus **Cummins INLINE 7 / J1939** in one window.

Does **not** ship manuals, filter charts, or service passwords. Those stay in your local `air-compressor-kb` folder.

## What it does

- Search fault codes, manuals, rental filters/oil, equipment dims, and (if present) iFix service passwords
- Open PDFs from the USB/Doosan/IR index
- INLINE 7 tab: live RPM, DM1/DM2, code reset, bus monitor, snapshots
- Job strip (model/serial) prefixes saved sessions

## Build (32-bit — required for INLINE 7)

Windows, .NET Framework 4.x:

```bat
cd Documents\TechBench
build.bat
```

That produces `TechBench.exe`. Desktop shortcut can point at it.

## Knowledge base

Expected at `C:\Users\<you>\Documents\air-compressor-kb` (or OneDrive Documents).  
The app looks for `data\kb.json` and the rest of that tree. It never copies the KB into this repo.

## INLINE 7

Close **USB-Link 3 Explorer** and Guidanz before Connect, or the adapter stays locked.

This tool does not disable DEF/SCR or Red Stop. Those lamps follow active DTCs.

## License

Private shop tool. Keep the password DB off GitHub.
