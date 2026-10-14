# Changelog

## Unreleased

- Rearch stage 1: `SettingsWriteGate` blocks settings/adapter-prefs writes during startup load; shell and INLINE 7 skip persist while restoring saved page/adapter/job; SelfTest covers startup-no-write hash checks.
- Nightly medium 2026-10-14: shop-ops clarity after #26–#35 — About/update dialogs name the active feed (`update-url.txt` / `update-feed-path.txt` / built-in); invalid shop share path no longer looks like a missing private GitHub feed; IdGateway matches updater HTTP policy (no auto-redirect off-host, TLS `|=`); adapter prefs match case-insensitively; Bus snapshot skips PGN/SA lists off the Bus page; docs/cheat sheet catch up to shipped prefs/feed/F1/crash log/Clear codes menu.
