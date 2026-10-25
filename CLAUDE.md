# Tech Bench: house rules (always on)

C# WinForms shop tool, .NET Framework 4, **x86 only** (RP1210 drivers are 32-bit): KB search plus Cummins INLINE 7 / J1939. Owner: Viper. One line per topic here; the detail lives in `docs/agent-playbook.md`, so read it before your first change. `CLAUDE.md` and `.cursor/rules/tech-bench.mdc` carry this same list; change both together and keep them short.

- **Repo:** public by Viper's choice. No new exposure and never add secrets. Visibility and history changes are his call. The password DB is an open owner decision: don't touch it.
- **Main is protected** (ruleset): PR only, required check `installer`, branch up to date before merge, no force-push or delete. Never push to `main`.
- **Fresh base:** `git fetch origin` and start from the latest `beta`. Bring a branch current with `beta` before touching it, and re-check before merge or tag. Never plan, review or screenshot from a stale checkout. Put the starting `beta` SHA in every PR and report.
- **Merging:** open every PR into `beta` and squash-merge it yourself as soon as CI is green, with no review hold. Only promoting `beta` → `main` (and tag/release) needs Viper's explicit say. Ask only when CI can't go green or for a product decision (Viper, 2026-10-24).
- **Branch cleanup:** after a PR merges or closes, delete your own head branch (GitHub auto-delete head branches is on). Delete scratch/test branches right away. Never delete `main` or `beta`.
- **Tooling:** pick Grok Build CLI, Cursor, or Claude per job (whichever fits). Claude and Cursor Projects both pick up Ready Command Centre cards. Green CI (`installer`) is the merge gate; Bugbot is off account-wide.
- **Ready gates:** Semgrep scan before a code PR counts Ready; `dyl-review` quick by default; Continual Learning keeps `AGENTS.md` current. Never use `dyl-ready-pr` merge or babysit steps. Auto-merge into `beta` on green is fine.
- **Release:** only on his word, by the normal path in the playbook (green `main` CI artifact attached with `gh release create`).
- **Proof of work:** CI green is the floor, not the proof. Every PR names the real thing it ran and what it saw. PRs and reports include a `Skipped / not verified` line.
- **Tests:** `test.bat` locally; Windows CI `installer` is the gate. Assert real behavior against literal expected values, no tautological tests. Never delete, skip or loosen an existing test; report it instead. Before a refactor, add behavior tests for that code in the same PR.
- **No field tests:** never run or schedule field tests; prove with CI / `test.bat` and document skips instead.
- **Findings:** log them and fix them in batches, never one PR per finding.
- **Briefs:** intent, data shape, scope and non-goals, evidence needed, file pointers. Exact error plus at most 20 log lines. One fresh session per task, context passed as pointers. Use `/workspace/plans/templates/cloud-agent-brief.md`.
- **CI failures:** find the cause before rerunning; at most one rerun. If a bug survives one fix, reproduce it and add logging first.
- **UI changes:** before/after screenshots and a Critiquito design review before the PR is called green.
- **PR shape:** title `Version: vX.Y.Z ...`, spec `docs/specs/vX.Y.Z.md` whose first line is `Version: vX.Y.Z`, the PR body sections, and the 4-spot lockstep version bump (playbook).
- **Owner gates:** no `.github/` changes without Viper's OK. No installs, new dependencies, spending or code signing.
- **Secrets:** never commit `.env*`, tokens, keys/certs, or legacy settings files (`id-settings*.json`). If you see one, stop and report it masked; don't rewrite history. Never use `DIST_REPO_TOKEN` or any PAT.
- **J1939:** no DEF/SCR reset and no calibration or parameter writes. Keep the existing guards. New ECU actions need Viper's OK and a confirm dialog; irreversible engine commands need human checks.
- **Updater:** keep the v1.2.9 rules (`docs/specs/v1.2.9.md`); `Updater.DefaultManifestUrl` stays unchanged.

