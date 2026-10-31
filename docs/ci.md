# CI

All build and test jobs live in `.github/workflows/ci.yml`. Three small workflows sit beside it: `pii-guard.yml` (the personal-info guard), `windows-installer.yml` (the C# Inno installer, kept separate because `SelfTest.cs` reads that file) and `claude.yml` (owner-triggered assistant, not CI).

## What runs when

The `changes` job in `ci.yml` reads the event and the changed paths, then each job gates on its outputs with a job-level `if:`.

| Event | Jobs that can run |
|---|---|
| Pull request into `beta` or `main` | `format`, `linux`, `windows`, plus `pii-guard`. Heavy builds never run on pull requests. |
| Push to `beta` | The PR set, plus the Linux heavy builds whose paths changed: `desktop (ubuntu-22.04)`, `android (API 34 emulator smoke)`, `web (vite build + Playwright)`, `wasm-pack test --node`. |
| Push to `main` | All of the above, plus the macOS and Windows legs: `desktop (macos-latest)`, `ios (simulator build and smoke)`, `tb-core (windows-latest)`, `tb-core (macos-latest)`, `bridge i686 + mock DLL (windows-latest)`, `desktop (windows-latest)`, `update from previous version (Windows)`, and `installer` from `windows-installer.yml`. |
| Nightly (`schedule`) and manual (`workflow_dispatch`) | Everything, without a paths filter. Nightly runs only on `main`, because GitHub schedules runs on the default branch only. |

On a push, a job runs only when its paths changed since the branch's previous commit. `format` and `pii-guard` always run.

The 32-bit bridge sidecar is built once per run, in `bridge i686 + mock DLL (windows-latest)`. `desktop (windows-latest)` and the updater download it as an artifact.

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
