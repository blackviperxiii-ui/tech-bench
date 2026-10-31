# Tech Bench agent playbook

The detail behind the always-on list in `CLAUDE.md` and `.cursor/rules/tech-bench.mdc`. Those files stay one line per topic; this file is not loaded every turn, so read it when you start a task. If a rule here and an always-on line disagree, stop and ask.

## 1. Repo state
- The repo is **public by the maintainer's choice**. Don't add exposure: no mirrors, forks, gists, public feeds or pasted internals elsewhere. Never add secrets.
- Visibility changes and history rewrites are the maintainer's call only.
- The password DB (`kb/data/passwords/ifix-passwords.json`) is an open owner decision. Don't edit, move, untrack or rewrite history for it. `build.bat` and `test.bat` still embed it.

## 2. Branch protection on `main`
- One repository ruleset targets `refs/heads/main` and `refs/heads/beta`: changes only through a PR (0 required approvals, no code-owner, last-push or thread-resolution requirement), the required status checks listed in `docs/ci.md`, and no force-push or deletion. The bypass list is empty, admins included.
- A direct push to `main` fails with `GH013: Repository rule violations`. That is expected; never push to `main`. Promote via PR only when the maintainer approves.
- Check the live rules with `gh api repos/blackviperxiii-ui/tech-bench/rules/branches/main`. Changing the ruleset is the maintainer's call.
- Merge methods are not restricted by the ruleset. The rule is squash and merge. Into `beta`, agents squash-merge on green; into `main` only on the maintainer's word.

## 3. Fresh base
```
git fetch origin
git rev-parse origin/beta          # record this SHA; it goes in the PR body and your report
git switch -c cursor/<topic> origin/beta
```
- Before touching an existing branch: `git fetch origin` and `git merge-base --is-ancestor origin/beta HEAD`. If that fails, bring the branch current with `beta` first (merge `origin/beta` in; don't force-push shared branches).
- Re-check `origin/beta` right before you squash-merge into `beta`. Re-check `origin/main` only when user asks for a promote/tag/release. A PR into a protected branch that is behind shows `BEHIND`/`BLOCKED` until it's updated.
- Never plan, review or screenshot from a stale checkout or from old images in `docs/`.

## 4. Merging and releases
- Open every PR into `beta`. Squash-merge it yourself (`gh pr merge <n> --squash`) as soon as the required checks (`docs/ci.md`) are green on its head, with no review hold. Re-check that the branch is current with `beta` first.
- Only promoting `beta` → `main`, tagging, and releasing need the maintainer's explicit word for that version.
- When the maintainer approves a promote/release: after the promote merge, wait for `main` CI (`installer`) to go green, download that run's artifact (`TechBench-Setup-<ver>.exe`, `TechBench.exe`, `latest.json`), and attach it with `gh release create v<ver> ... --target main`. The API creates the tag. Installs are manual through shop Setup (`docs/shop-rollout.md`).
- There's no public feed and no dist repo. Don't create or upload to `tech-bench-dist`. Never use or ask for `DIST_REPO_TOKEN` or any PAT.
- No field tests. Never run or schedule field tests on real engines or the shop floor; prove with CI / `test.bat` and document skips.

## 5. PR shape
- Branch names: `cursor/...` or `claude/...`. Commit subjects are plain sentences, e.g. `Version: v1.2.9 Updater hardening` or `v1.2.9: fix CS0136 ...`.
- Versioned PRs: title `Version: vX.Y.Z <summary>`, and a one-page spec at `docs/specs/vX.Y.Z.md` whose first line is `Version: vX.Y.Z`.
- PR body sections: `Spec version: vX.Y.Z` (or say why there's none, e.g. docs-only), starting `beta` SHA, What changed, ACs with evidence, What I ran and saw, Verify commands, `Skipped / not verified`, Non-goals. Docs-only PRs prefer no lockstep version bump.
- Lockstep version bump, all 4 spots together:
  1. `src/Core/Version/AppVersion.cs`: `Number` plus `AssemblyVersion`, `AssemblyFileVersion` (`X.Y.Z.0`) and `AssemblyInformationalVersion` (`X.Y.Z`).
  2. `app.manifest`: `<assemblyIdentity version="X.Y.Z.0" ...>`.
  3. `tests/SelfTest.Feed.cs`: `Eq("stamped version", AppVersion.Number, "X.Y.Z")`.
  4. `README.md`: the `Current stamp: **X.Y.Z**` line.
  A lockstep version-bump script (if you keep one) should apply and verify all 4.
- New `.cs` files go in `sources/core.list` or `sources/app.list` (membership: root name or `src\...` path). `tools/gen-rsp.bat` (and `tools/gen-rsp.sh` on Linux) write `sources/*.rsp`; `tests/*.cs` are picked up by glob into `selftest.rsp`. SelfTest fails on drift. Do not hand-edit generated `.rsp` files. Stage 6 moves feature code into `src/` one folder per PR; safety files last and separately.
- No `.github/` changes (workflow, settings) without the maintainer's OK.

## 6. Tests and CI
- CI (`.github/workflows/windows-installer.yml`, job `installer`, windows-latest, on push to `main`, nightly and manual runs; PRs and beta pushes run `test.bat` in `ci.yml`'s `windows` job): `test.bat`, then `release.bat`, then `tools\assert-pe-i386.ps1` on `TechBench.exe` and `-Native` on the Setup exe, then `tools\install-smoke.ps1 -Setup dist\TechBench-Setup-<ver>.exe`.
- Locally from the repo root: `test.bat` (SelfTest plus LayoutAudit; both must end clean). Build only: `build.bat`. Needs Windows and the .NET Framework 4 `csc.exe`. On Linux agent boxes, run `tools/linux-compile-check.sh` (Mono `mcs`; `sudo apt-get install -y mono-devel` once) before opening a PR to catch CS errors early — it is not a substitute for `test.bat`. Windows CI green on the PR head is still required.
- New tests call the code the way the app does and assert a literal expected value. Before keeping one, ask: would it still pass if the code under test returned `default`/`null` or a stub? If yes, rewrite it. Don't add tests that only restate a constant, check that a mock was called, or assert their own fixture. No test beats a bad test, and no single-test PRs.
- Never delete, skip or loosen an existing test. If one looks tautological or wrong, report it in the findings log. Disclose any test change in the PR.
- Before a refactor, the touched behavior must be covered by passing tests. If it isn't, add behavior tests first, in the same PR.

## 7. Proof of work
- CI green is the floor, not the proof. The PR body names the real run path you exercised and what you saw: the command and its output, or before/after screenshots for UI. Self-reports and "it compiles" don't count.
- Every PR body and report has a `Skipped / not verified` line, even if it says "none". Label claims as measured, inferred or guess when it isn't obvious.
- No status pings without a side effect or a decision. "Nothing to report" is a valid report.

## 8. Findings
- Non-blocking findings (smells, small bugs or flaky tests outside the task) go to a log with `file:line` and a one-line why. On the box: `/workspace/tb-specs/findings.md`. Without the box: a `Findings` section in your report for the coordinator to log.
- Don't open a PR per finding. Related findings get fixed together in one batched PR when it's scheduled.
- If the same correction comes up twice, propose a structural fix (a test, a script, a type) in your report instead of another rule line.

## 9. Agent briefs and sessions
- Brief template: **intent** (1-3 sentences), **data shape** (types, files, wire formats involved), **scope and non-goals**, **evidence needed** to call it done, **file pointers** (specs, prior PRs, reports) instead of pasted content.
- Name the exact failing test or error string and paste at most 20 relevant log lines. Point to files; don't paste them.
- One fresh session per task or phase, with continuity passed as pointers. Pick the model and effort at launch and keep it. If a session has compacted more than once, relaunch with a consolidated brief. Judge progress by commits, pushes and check changes, not status messages.
- Keep the 2-agent cap; no agent sprays. Mechanical steps that repeat go into a script.

## 10. CI failures and stubborn bugs
- Read the failing step's log and classify the failure before rerunning. A failure in code you didn't touch usually means a stale base; check with `git merge-base --is-ancestor origin/beta HEAD`.
- At most one rerun for a suspected flake. An identical second failure is real.
- If a bug survives one fix attempt, stop guessing: reproduce it, add logging or a failing test that shows it, then fix.
- Bugbot is off account-wide. If a bot review comment still appears, triage it on its merits; don't churn code to quiet it.

## 11. UI changes
- Include before/after screenshots taken from a fresh build of the current branch.
- Get a design review (Critiquito) and address or note its ranked fixes before calling the PR green. Run `LayoutAudit` via `test.bat`.

## 12. Secrets
- Never commit `.env*`, tokens, keys/certs (`*.pfx`, `*.p12`, `*.pem`, `*.key`), or legacy settings files (`id-settings*.json`). Users may still have one in `%LocalAppData%\TechBench`; the app no longer reads its secret fields. Tests use fake values only.
- If you see a real secret in the tree or in history, stop and report it masked so it can be rotated. Don't rewrite history yourself.

## 13. Safety rails (don't weaken)
- J1939/INLINE 7: the code clear sends DM11/DM3 requests only, then reads DM1/DM2/FE56 back. No DEF/SCR reset or disable, no lamp override, no calibration or parameter writes, no proprietary or UDS routines, until user decides otherwise.
- Keep the existing guards: code clears sit behind their confirm dialogs, and the TSC1 speed holds sit behind the Safety toggle. The existing TSC1, DM13 and Ping ECM controls stay as they are; don't widen them.
- Any new ECU action needs the maintainer's OK and a confirm dialog. Irreversible engine commands need a human check on a real engine; CI can't prove them.
- Updater (v1.2.9, `docs/specs/v1.2.9.md`): 64 KiB manifest cap and 32 MiB payload cap enforced while streaming; manual redirects, at most 5, same host only, never https to http; payload on the manifest's host and port; SHA-256 checked before staging; `.part` cleanup; no credentials or token ever sent; nothing applied while an INLINE 7 session is live. `Updater.DefaultManifestUrl` is unchanged.
- No installs, new dependencies or NuGet packages without asking. No spending, and no code signing.


## 14. Branch cleanup
- After a PR **merges or closes**, delete the head branch you created (`gh pr view <n> --json headRefName -q .headRefName`, then `git push origin --delete <branch>` if it still exists). Prefer `gh api -X DELETE repos/blackviperxiii-ui/tech-bench/git/refs/heads/<branch>` when the local tracking branch is gone.
- Delete scratch and test branches as soon as they are unused (no open PR).
- Never delete `main`. Never delete `beta`. Don't mass-delete someone else's open-PR branches.
- Repo setting `delete_branch_on_merge` is **true** (GitHub auto-delete head branches on). Still delete your own head if it remains after a close. Confirm with `gh api repos/blackviperxiii-ui/tech-bench --jq .delete_branch_on_merge`.

## 15. Tooling choice (Grok Build, Cursor, Claude)
- Pick **Grok Build CLI**, **Cursor** (cloud agent / IDE / Project), or **Claude** per job, whichever fits scope, machine access, and token budget. Document the choice in the brief.
- Any coding agent (Claude, Cursor, or others) may pick up work; none is the only builder.
- The required checks in `docs/ci.md` are the merge gate (`windows` runs `test.bat` on every PR). Bugbot is off account-wide. Local Linux/Mono is only a hint.

## 16. Semgrep, dyl-review, Continual Learning
- **Semgrep:** before a **code** PR counts Ready, run Semgrep on the touched area (or the repo). Fix findings or justify them under `Skipped / not verified` / Notes. Docs-only PRs may mark Semgrep N/A, but still keep this gate in the playbook.
- **`dyl-review`:** quick by default for PR review drafts; deep only when asked. Never posts to the PR by itself.
- **Continual Learning:** keep this repo's `AGENTS.md` current via the continual-learning / `agents-memory-updater` flow. No secrets in `AGENTS.md`.
- **Banned:** do **not** use `dyl-ready-pr` **merge** or **babysit** steps. Agents squash-merge into `beta` on green required checks; only beta→main / tag / release needs user. No merge queues that bypass the required checks.


