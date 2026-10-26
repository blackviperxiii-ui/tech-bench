# Tech Bench AGENTS.md

Short, living notes for agents working this repo. Continual Learning may refresh this file; keep it free of secrets and point detail at the playbook.

## Pointing
- Always-on gates: `CLAUDE.md` and `.cursor/rules/tech-bench.mdc` (identical short bodies).
- Detail: `docs/agent-playbook.md`.
- Cloud-agent brief template: `/workspace/plans/templates/cloud-agent-brief.md`.

## Stack
- C# WinForms, .NET Framework 4, **x86 only** (RP1210).
- Test/build: `test.bat` / `build.bat` on Windows; CI job `installer` is the gate (Bugbot off account-wide).
- Never commit secrets, `.env*`, tokens, keys/certs, or legacy settings files (`id-settings*.json`).

## Merge / release
- PRs go into `beta`; agents squash-merge their own PR as soon as CI is green, no review hold.
- Promoting `beta` → `main`, tagging, and releasing only on the maintainer's explicit word.
- Never use `dyl-ready-pr` merge or babysit. No field tests.


## No personal info (hard rule)

Never put a real name, personal email, phone, address, employer, or real home-folder path in any file, commit message, PR, or issue. Anywhere a name is needed, write "user". Paths use `/home/user`. Commits use the GitHub no-reply email. `scripts/pii-guard/pii_guard.py` runs in CI (`pii-guard` check) and blocks violations; run it locally before pushing.
