# CI

All build and test jobs live in `.github/workflows/ci.yml`. Three small workflows sit beside it: `pii-guard.yml` (the personal-info guard), `windows-installer.yml` (the C# Inno installer, kept separate because `SelfTest.cs` reads that file) and `claude.yml` (owner-triggered assistant, not CI).

## Run-on and skip-on

The `changes` job in `ci.yml` reads the event and the changed paths, then each job gates on its outputs with a job-level `if:`. On a push, a path-gated job runs only when its paths changed since the branch's previous commit.

| Event | Runs | Skips | Cancelled by a newer run? |
|---|---|---|---|
| Pull request into `beta` or `main` | `format`, `linux` (Linux build + test), `windows` (C# `test.bat`), `pii-guard` | `linux`/`windows` when their paths didn't change (they still report as passed). Every heavy build. No macOS. | Yes, a newer push to the same PR |
| Push to `beta` | The PR set, plus the Linux heavy builds whose paths changed: `desktop (ubuntu-22.04)`, `android (API 34 emulator smoke)`, `web (vite build + Playwright)`, `wasm-pack test --node` | All Windows and macOS legs except `windows` | Yes, a newer push to `beta` |
| Push to `main` (promotion) | All of the above, plus the Windows legs whose paths changed: `bridge i686 + mock DLL (windows-latest)`, `desktop (windows-latest)`, `update from previous version (Windows)`, `tb-core (windows-latest)`, and `installer` (`windows-installer.yml`) | macOS legs. No screenshots. | No |
| Tag `v*` | Nothing. No release workflow: the release is attached by hand from the green `main` build's `installer` artifact. | Everything | n/a |
| Nightly (`schedule`, 08:05 UTC, default branch only) | Everything with no paths filter, plus the macOS legs: `desktop (macos-latest)`, `ios (simulator build and smoke)`, `tb-core (macos-latest)` | Every job but `format` when an earlier push, nightly or manual `ci.yml` run on the same commit passed with no job skipped. `installer` when it already passed on the same commit. If the lookup fails, the nightly runs in full. | No |
| Manual (`workflow_dispatch`) | Everything, like the nightly, with no skip | Nothing | No |
| Merge queue | The PR set, with no paths filter | Heavy builds | No |
| Issue label or `@claude` comment by the owner | `claude.yml` | Everyone else's events | No |

The 32-bit bridge sidecar is built once per run, in `bridge i686 + mock DLL (windows-latest)`. `desktop (windows-latest)` and the updater download it as an artifact.

## Rules for workflow changes

Update this file in the same PR as any workflow change.

- Every job sets `timeout-minutes`, a few times its usual run time.
- Every `actions/upload-artifact` step sets `retention-days: 7` or less. The bridge hand-off keeps 1 day. The `installer` artifact keeps 7 days, so attach it to the GitHub release within a week of the `main` build, or re-run the build.
- Heavy builds never run on pull requests.
- macOS runs only on nightly and manual runs.
- Path gating is a job-level `if:` on the `changes` outputs, never a workflow-level path filter on a workflow that holds a required check.
- A nightly does not repeat a commit that already passed.

## Required checks

The repository ruleset for `main` and `beta` sets these. Read the live set with `gh api repos/{owner}/{repo}/rules/branches/beta`.

| Branch | Required checks |
|---|---|
| `beta` | `format`, `linux`, `windows`, `pii-guard` |
| `main` | `format`, `linux`, `windows`, `pii-guard`. Until `ci.yml` is on `main`, `pii-guard` only. |

Each of these checks reports on every pull request into `beta` and `main`:

- `pii-guard` runs on every pull request, with no filter.
- `format` runs on every `ci.yml` run, with no condition.
- `linux` and `windows` skip with a job-level `if:` when their paths did not change. GitHub reports a job skipped by `if:` as a passing check, so a docs-only pull request still shows all four checks green.

Rules that keep this true:

- Never put an `on.<event>.paths` filter, or a workflow-level `if`, on a workflow that holds a required check. A workflow that does not start leaves its checks at "Expected — waiting for status" forever. Gate jobs with `if:` on the `changes` outputs instead.
- Never rename `format`, `linux`, `windows` or `pii-guard` without updating the ruleset in the same change.
- Don't require a matrix job (its check name includes the matrix value), and don't require a job that runs only on push, such as `installer` or any heavy build. Pull requests never run those jobs, so a required one blocks every pull request.
- `main` gets the `ci.yml` checks only after `ci.yml` is on `main`. Until then, a pull request from a branch cut from `main` has no `ci.yml` to run.
