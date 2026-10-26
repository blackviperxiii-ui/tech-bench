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
- Agents stop at green + Ready. Squash-merge, tag, and release only on the maintainer's explicit word.
- No `dyl-ready-pr` merge or babysit. No auto-merge.

- **vstack:** Always load and follow the vstack skills in .cursor/skills (start with viper-mode); never use pstack.

## No personal info (hard rule)

This is open-source work meant for anyone to use. Keep every file, commit, PR and issue generic.

- Never include a real name, nickname used as a person, personal email, phone, address, employer, city, machine name, or real home-folder path. Where a name is needed, write "user". Paths use `/home/user`.
- Never mention the maintainer's other projects, personal bots or assistants, task boards, or personal workflow (Notion cards, Slack pings, approval habits). Examples use neutral names like `my-app`.
- Commits use the GitHub no-reply email. Squash merges use a clean subject and body with no `Co-authored-by` lines.
- `scripts/pii-guard/pii_guard.py` runs in CI as the required `pii-guard` check; run it locally before pushing.
