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
- PRs go into `beta`; agents squash-merge their own PR as soon as CI is green, no review hold (Jeremy, 2026-10-24).
- Promoting `beta` → `main`, tagging, and releasing only on Jeremy's explicit word.
- Never use `dyl-ready-pr` merge or babysit. No field tests.

- **pstack:** Always load and follow the pstack skills in .cursor/skills (start with poteto-mode); never use vstack.
