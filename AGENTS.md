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

## Require vstack
- Cursor: import `blackviperxiii-ui/vstack` as a team marketplace; set it to Required and enable Auto Refresh.
- Claude Code: `.claude/settings.json` adds the vstack marketplace and turns on `vstack@vstack`, so it loads once you trust the folder. If `/plugin` doesn't list it, run `/plugin marketplace add blackviperxiii-ui/vstack` and `/plugin install vstack@vstack`.
- Other agents (Codex, Copilot, Jules): run `npx skills add blackviperxiii-ui/vstack -g -y` before you start, so the skills install for your user and nothing lands in the repo.
- Do not copy vstack skills or rules into this repository.

How to use vstack here:
- Start nontrivial work with `viper-mode`. It routes to `how` and `why` before edits, `architect` for new modules, `blast-radius` before shared code, `tdd` for bugs and `interrogate` for reviews.
- Issues and board cards: run `follow-through` from pickup to PR (start comment, blocked note, done with proof).
- PR bodies and docs: `technical-writing`.
- `app/` TypeScript: `typescript-best-practices`. Before a beta to main promotion: `qa-walkthrough`.
- No install? Read a skill straight from `https://github.com/blackviperxiii-ui/vstack/blob/main/skills/<name>/SKILL.md`.

## No personal info (hard rule)

This is open-source work meant for anyone to use. Keep every file, commit, PR and issue generic.

- Never include a real name, nickname used as a person, personal email, phone, address, employer, city, machine name, or real home-folder path. Where a name is needed, write "user". Paths use `/home/user`.
- Never mention the maintainer's other projects, personal bots or assistants, task boards, or personal workflow (Notion cards, Slack pings, approval habits). Examples use neutral names like `my-app`.
- Commits use the GitHub no-reply email. Squash merges use a clean subject and body with no `Co-authored-by` lines.
- `scripts/pii-guard/pii_guard.py` runs in CI as the required `pii-guard` check; run it locally before pushing.

## Command Centre board guide (GitHub access only)

Work is planned on the Notion board "Grok Bot Command Centre". Cards with
Status = Ready sync to GitHub issues on blackviperxiii-ui/tech-bench (Project = Tech Bench; the issue sync is being rolled out here after GrokHub).
You don't need Notion access: the issue is your copy of the card.

### Finding your work
1. Open issues with your label: claude, copilot or jules.
2. Do High priority first, then lowest Order (both are shown on the card, linked from the issue).
3. Each issue links its Notion card and copies its Evidence / Fix / Acceptance.
4. Don't start a new issue while you already have 2 open PRs.

### Board terms used in issues
- Status: Idea, Ready, Doing, Waiting, Needs you, Done, Notes.
- Assigned: Claude, Copilot or Jules (matches your label).
- Priority: High, Medium, Low, Eh... Whenever.
- Order: pickup sequence. Lower goes first.
- Result: the card's outcome line. Picked up / Done at: start and finish dates.

### Working an issue
- Start: comment on the issue saying you've started (the card moves to Doing).
- PR body must include:
  - `Closes #N` (the issue number)
  - the Notion card URL from the issue
  - a one-line summary of the change
- On merge to beta, the board's coding agent sets the card to Done, fills in
  Done at, and puts the PR link in Result. Don't do this yourself.
- Blocked: comment on the issue with what blocked you and what's needed,
  then stop. The card goes back to Ready with your note.

### Never
- Work issues labelled for another coder, or change their labels.
- Close or delete issues, or relabel your own issue to someone else.

### Repo rules
- PRs target beta only. Never main or genesis.
- Squash-merge style: one clean subject line, no Co-authored-by lines.
- No force-pushes.
- Run fmt, lint and tests locally before pushing, then push once.
- At most 2 open PRs per coder.
- Merges: once your PR into beta is green and not held, squash-merge it into
  beta yourself, with no review hold. Rebase on the beta tip first and,
  if beta moved past the PR's version, renumber it and let CI rerun. Never merge into main.
- No personal info in the repo: write 'user' for names and /home/user for paths.

Coders with Notion access: see `docs/agents/board-guide-notion.md`.
