# Tech Bench agent playbook

The detail behind the always-on list in `CLAUDE.md` and `.cursor/rules/tech-bench.mdc`. Those files stay one line per topic; this file is not loaded every turn, so read it when you start a task. If a rule here and an always-on line disagree, stop and ask.

## 1. Repo state
- The repo is **public by Jeremy's choice**. Don't add exposure: no mirrors, forks, gists, public feeds or pasted internals elsewhere. Never add secrets.
- Visibility changes and history rewrites are Jeremy's call only.
- The password DB (`kb/data/passwords/ifix-passwords.json`) is an open owner decision. Don't edit, move, untrack or rewrite history for it. `build.bat` and `test.bat` still embed it.

## 2. Branch protection on `main`
- Repository ruleset **"Protect main"** targets `refs/heads/main`: changes only through a PR (0 required approvals, no code-owner, last-push or thread-resolution requirement), required status check `installer` (GitHub Actions) with the branch **up to date** before merge, and no force-push or deletion. The bypass list is empty, admins included.
- A direct push to `main` fails with `GH013: Repository rule violations`. That is expected; open a PR instead.
- Check the live rules with `gh api repos/blackviperxiii-ui/tech-bench/rules/branches/main`. Changing the ruleset is Jeremy's call.
- Merge methods are not restricted by the ruleset. Jeremy's rule is squash and merge, only when he says so.

## 3. Fresh base
```
git fetch origin
git rev-parse origin/main          # record this SHA; it goes in the PR body and your report
git switch -c cursor/<topic> origin/main
```
- Before touching an existing branch: `git fetch origin` and `git merge-base --is-ancestor origin/main HEAD`. If that fails, bring the branch current with `main` first (merge `origin/main` in; don't force-push shared branches).
- Re-check `origin/main` right before you call a PR ready, and before any merge or tag Jeremy asks for. Because the ruleset is strict, a PR behind `main` shows `BEHIND`/`BLOCKED` until it's updated.
- Never plan, review or screenshot from a stale checkout or from old images in `docs/`.

## 4. Merging and releases
- Agents get PRs **green and ready**, then stop and report. Never merge, tag or release without Jeremy's explicit word for that PR or version.
- When he says merge: squash and merge (`gh pr merge <n> --squash`), after re-checking that the branch is current and CI is green on its head.
- Release, only on his word: after the merge, wait for `main` CI (`installer`) to go green, download that run's artifact (`TechBench-Setup-<ver>.exe`, `TechBench.exe`, `latest.json`), and attach it with `gh release create v<ver> ... --target main`. The API creates the tag. Installs are manual through shop Setup (`docs/shop-rollout.md`).
- There's no public feed and no dist repo. Don't create or upload to `tech-bench-dist`. Never use or ask for `DIST_REPO_TOKEN` or any PAT.

## 5. PR shape
- Branch names: `cursor/...` or `claude/...`. Commit subjects are plain sentences, e.g. `Version: v1.2.9 Updater hardening` or `v1.2.9: fix CS0136 ...`.
- Versioned PRs: title `Version: vX.Y.Z <summary>`, and a one-page spec at `docs/specs/vX.Y.Z.md` whose first line is `Version: vX.Y.Z`.
- PR body sections: `Spec version: vX.Y.Z` (or say why there's none, e.g. docs-only), starting `main` SHA, What changed, ACs with evidence, What I ran and saw, Verify commands, `Skipped / not verified`, Non-goals.
- Lockstep version bump, all 4 spots together:
  1. `AppVersion.cs`: `Number` plus `AssemblyVersion`, `AssemblyFileVersion` (`X.Y.Z.0`) and `AssemblyInformationalVersion` (`X.Y.Z`).
  2. `app.manifest`: `<assemblyIdentity version="X.Y.Z.0" ...>`.
  3. `SelfTest.cs`: `Eq("stamped version", AppVersion.Number, "X.Y.Z")`.
  4. `README.md`: the `Current stamp: **X.Y.Z**` line.
  On Jeremy's box, `/workspace/tech-bench-tools/lockstep-version-bump.sh <repo> X.Y.Z` applies and verifies all 4; `--check` only reports them.
- New `.cs` files go in `sources/core.rsp` or `sources/app.rsp` (the only source list). SelfTest fails on drift.
- No `.github/` changes (workflow, settings) without Jeremy's OK.

## 6. Tests and CI
- CI (`.github/workflows/windows-installer.yml`, job `installer`, windows-latest, on every push): `test.bat`, then `release.bat`, then `tools\assert-pe-i386.ps1` on `TechBench.exe` and `-Native` on the Setup exe, then `tools\install-smoke.ps1 -Setup dist\TechBench-Setup-<ver>.exe`.
- Locally from the repo root: `test.bat` (SelfTest plus LayoutAudit; both must end clean). Build only: `build.bat`. Needs Windows and the .NET Framework 4 `csc.exe`. Linux/Mono runs are only a hint; Windows CI green on the PR head is required.
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
- Read the failing step's log and classify the failure before rerunning. A failure in code you didn't touch usually means a stale base; check with `git merge-base --is-ancestor origin/main HEAD`.
- At most one rerun for a suspected flake. An identical second failure is real.
- If a bug survives one fix attempt, stop guessing: reproduce it, add logging or a failing test that shows it, then fix.
- Triage bot review comments (e.g. Bugbot) on their merits; don't churn code to quiet them.

## 11. UI changes
- Include before/after screenshots taken from a fresh build of the current branch.
- Get a design review (Critiquito) and address or note its ranked fixes before calling the PR green. Run `LayoutAudit` via `test.bat`.

## 12. Secrets
- Never commit `.env*`, tokens, keys/certs (`*.pfx`, `*.p12`, `*.pem`, `*.key`), `id-settings*.json`, or the IntelliDealer subscription key or OAuth secret. Those live only in `%LocalAppData%\TechBench\id-settings.json`, DPAPI-protected. Tests use fake values such as `sub-key`.
- If you see a real secret in the tree or in history, stop and report it masked so it can be rotated. Don't rewrite history yourself.

## 13. Safety rails (don't weaken)
- J1939/INLINE 7: the code clear sends DM11/DM3 requests only, then reads DM1/DM2/FE56 back. No DEF/SCR reset or disable, no lamp override, no calibration or parameter writes, no proprietary or UDS routines, until Jeremy decides otherwise.
- Keep the existing guards: code clears sit behind their confirm dialogs, and the TSC1 speed holds sit behind the Safety toggle. The existing TSC1, DM13 and Ping ECM controls stay as they are; don't widen them.
- Any new ECU action needs Jeremy's OK and a confirm dialog. Irreversible engine commands need a human check on a real engine; CI can't prove them.
- Updater (v1.2.9, `docs/specs/v1.2.9.md`): 64 KiB manifest cap and 32 MiB payload cap enforced while streaming; manual redirects, at most 5, same host only, never https to http; payload on the manifest's host and port; SHA-256 checked before staging; `.part` cleanup; no credentials or token ever sent; nothing applied while an INLINE 7 session is live. `Updater.DefaultManifestUrl` is unchanged.
- No installs, new dependencies or NuGet packages without asking. No spending, and no code signing.


## 14. Branch cleanup
- After a PR **merges or closes**, delete the head branch you created (`gh pr view <n> --json headRefName -q .headRefName`, then `git push origin --delete <branch>` if it still exists). Prefer `gh api -X DELETE repos/blackviperxiii-ui/tech-bench/git/refs/heads/<branch>` when the local tracking branch is gone.
- Delete scratch and test branches as soon as they are unused (no open PR).
- Never delete `main`. Never delete `beta` if that branch exists. Don't mass-delete someone else's open-PR branches.
- Repo setting `delete_branch_on_merge` is currently **false**. Do **not** flip it. When Jeremy approves later: `gh api -X PATCH repos/blackviperxiii-ui/tech-bench -f delete_branch_on_merge=true`. Confirm with `gh api repos/blackviperxiii-ui/tech-bench --jq .delete_branch_on_merge`.

## 15. Tooling choice (Grok Build vs Cursor)
- Pick **Grok Build CLI** or **Cursor** (cloud agent / IDE) per job, whichever fits scope, machine access, and token budget. Document the choice in the brief.
- Cursor usage may be paused by Jeremy (e.g. out until a stated date); then use Grok Build / box work. Keep the rule general: whichever fits when both are available.
- Windows CI `installer` and Cursor Bugbot still gate every PR. Local Linux/Mono is only a hint.

## 16. Semgrep, dyl-review, Continual Learning
- **Semgrep:** before a **code** PR counts Ready, run Semgrep on the touched area (or the repo). Fix findings or justify them under `Skipped / not verified` / Notes. Docs-only PRs may mark Semgrep N/A, but still keep this gate in the playbook.
- **`dyl-review`:** quick by default for PR review drafts; deep only when asked. Never posts to the PR by itself.
- **Continual Learning:** keep this repo's `AGENTS.md` current via the continual-learning / `agents-memory-updater` flow. No secrets in `AGENTS.md`.
- **Banned:** do **not** use `dyl-ready-pr` **merge** or **babysit** steps. Nothing merges without Jeremy's say. No auto-merge, no merge queues that bypass that rule.

