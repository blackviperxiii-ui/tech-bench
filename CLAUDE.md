# Tech Bench: house rules for every agent chat

Private shop tool: C# WinForms, .NET Framework 4, **x86 only** (RP1210 drivers are 32-bit). KB search plus Cummins INLINE 7 / J1939. Owner: Jeremy. This file and `.cursor/rules/tech-bench.mdc` (or `CLAUDE.md`) say the same thing. Change both together.

## Git and PRs
- Branch off `main` (`cursor/...`, `claude/...`). Never push to `main`. Changes land only through a PR.
- Ask before merging. Merges are **merge commits** (no squash or rebase), only with Jeremy's OK. Keep the branch.
- No `.github/` changes (workflow, settings) without Jeremy's OK.
- PR title: `Version: vX.Y.Z <summary>`. PR body: a `Spec version: vX.Y.Z` line, ACs with evidence, verify commands, and non-goals. Add a one-page spec at `docs/specs/vX.Y.Z.md` whose first line is `Version: vX.Y.Z`.
- Commit subjects are plain sentences, for example `Version: v1.2.9 Updater hardening` or `v1.2.9: fix CS0136 ...`.
- Version bump, all in lockstep: `AppVersion.cs` (`Number` plus the 3 assembly attributes), `app.manifest` `assemblyIdentity version="X.Y.Z.0"`, the SelfTest `stamped version` check in `SelfTest.cs`, and the README `Current stamp:` line.
- New `.cs` files go in `sources/core.rsp` or `sources/app.rsp` (the only source list). SelfTest fails on drift.

## Test (needs Windows plus .NET Framework 4 `csc.exe`; CI is the gate)
- Exact CI steps (`.github/workflows/windows-installer.yml`, windows-latest, on every push): `test.bat`, then `release.bat`, then `tools\assert-pe-i386.ps1` on `TechBench.exe` and `-Native` on the Setup exe, then `tools\install-smoke.ps1 -Setup dist\TechBench-Setup-<ver>.exe`.
- Local check from the repo root: `test.bat` (SelfTest plus LayoutAudit; both must end clean). Build only: `build.bat`.
- Linux/Mono runs are only a hint. Some sections assume Windows paths. Windows CI green on the PR head is required.
- Never remove, skip, or weaken an existing test. Disclose any test change in the PR.

## Release (only on Jeremy's "full ship")
- No tag and no release without Jeremy saying "full ship" for that version.
- Normal path: merge, wait for `main` CI green, then take that run's artifact (`TechBench-Setup-<ver>.exe`, `TechBench.exe`, `latest.json`) and attach it to GitHub release `v<ver>` on blackviperxiii-ui/tech-bench. Installs are manual through shop Setup (`docs/shop-rollout.md`).
- No public feed and no public dist repo. Don't create or upload to `tech-bench-dist`. Never use or ask for `DIST_REPO_TOKEN` or any PAT.
- The repo stays private. Don't change visibility or mirror it anywhere. If `gh repo view --json visibility` does not say PRIVATE, stop and tell Jeremy.

## Secrets
- Never commit `.env*`, tokens, keys/certs (`*.pfx`, `*.p12`, `*.pem`, `*.key`), `id-settings*.json`, or the IntelliDealer subscription key or OAuth secret. Those live only in `%LocalAppData%\TechBench\id-settings.json`, DPAPI-protected. Tests use fake values such as `sub-key`.
- The password DB (`kb/data/passwords/ifix-passwords.json`) is an open owner decision. Don't edit, move, untrack, or rewrite history for it without Jeremy.
- If you see a real secret in the tree or in history, stop and report it (masked). Don't rewrite history yourself.

## Safety rails (do not weaken)
- J1939/INLINE 7: the code clear sends DM11/DM3 requests only, then reads DM1/DM2/FE56 back. No DEF/SCR reset or disable, no lamp override, no calibration or parameter writes, no proprietary or UDS routines, until Jeremy decides otherwise.
- Keep the existing guards: the code clears sit behind their confirm dialogs, and the TSC1 speed holds sit behind the Safety toggle. Any new ECU action needs Jeremy's OK and a confirm dialog. The existing TSC1, DM13, and Ping ECM controls stay as they are; don't widen them.
- Updater (v1.2.9, `docs/specs/v1.2.9.md`): 64 KiB manifest cap and 32 MiB payload cap enforced while streaming; manual redirects, at most 5, same host only, never https to http; payload on the manifest's host and port; SHA-256 checked before staging; `.part` cleanup; no credentials or token ever sent; nothing applied while an INLINE 7 session is live. `Updater.DefaultManifestUrl` is unchanged.
- No installs, new dependencies, or NuGet packages without asking. No spending, and no code signing.
