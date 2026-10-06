# Tech Bench AGENTS.md

Short, living notes for agents working this repo. Continual Learning may refresh this file; keep it free of secrets and point detail at the playbook.

## Pointing
- Always-on gates: `CLAUDE.md` and `.cursor/rules/tech-bench.mdc` (identical short bodies).
- Detail: `docs/agent-playbook.md`.
- Cloud-agent brief template: `/workspace/plans/templates/cloud-agent-brief.md`.

## Stack
- C# WinForms, .NET Framework 4, **x86 only** (RP1210).
- Test/build: `test.bat` / `build.bat` on Windows; CI job `installer` is the gate.
- Never commit secrets, `.env*`, tokens, keys/certs, or the IntelliDealer key.

## Merge / release
- Agents stop at green + Ready. Squash-merge, tag, and release only on Jeremy's explicit word.
- No `dyl-ready-pr` merge or babysit. No auto-merge.
