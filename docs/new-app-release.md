# Releasing the new app (unsigned)

This page covers the Tauri app in `app/` and `src-tauri/`. The WinForms app keeps its own path in `docs/agent-playbook.md` (Inno Setup, `release.bat`, the `installer` check). Both ship side by side. This page does not replace the WinForms installer.

Code signing is out of scope for now. Every package below is unsigned, so each platform warns on first install. The "Installing unsigned builds" section says what the tech sees.

Releasing is the maintainer's call, as for WinForms. An agent builds and checks packages, but never tags or publishes a release on its own.

## Version

One version, in three files that must agree:

| File | Field |
|---|---|
| `src-tauri/tauri.conf.json` | `"version"` (installers, the update check, the UI build through `__APP_VERSION__`) |
| `src-tauri/Cargo.toml` | `version` (the binary) |
| `package.json` | `"version"` |

Bump all three in the same PR. Two checks catch a miss:

- `crates/tb-core/tests/app_version.rs` fails when the three files disagree. It runs in the `linux` check on every pull request.
- The desktop and mobile `--smoke` runs start their line with `version X.Y.Z`. They fail with `version mismatch: app …, binary …, UI …` when the installed app, its binary and its UI were built from different versions.

## Build the packages locally

Run these from the repo root, after `npm ci`. Each command writes to `target/release/bundle/` (or `target/<target>/release/bundle/`). File names start with the product name, for example `Tech Bench_0.1.0_amd64.deb`.

| Platform | Command | Output |
|---|---|---|
| Windows (x64 shell, 32-bit bridge) | Build the bridge first: `cargo build -p tb-rp1210-bridge --release --target i686-pc-windows-msvc`, then copy `target/i686-pc-windows-msvc/release/tb-rp1210-bridge.exe` to `src-tauri/binaries/tb-rp1210-bridge-x86_64-pc-windows-msvc.exe`. Then `npx tauri build --bundles msi,nsis`. | `bundle/msi/*.msi`, `bundle/nsis/*-setup.exe` |
| Linux | `npx tauri build --bundles deb,rpm,appimage` | `bundle/deb/*.deb`, `bundle/rpm/*.rpm`, `bundle/appimage/*.AppImage` |
| macOS | `npx tauri build --bundles app,dmg --target universal-apple-darwin` | `target/universal-apple-darwin/release/bundle/dmg/*.dmg` |
| Android | `npx tauri android build --apk` (debug: add `--debug`) | `src-tauri/gen/android/app/build/outputs/apk/` |

The Windows bridge must stay 32-bit, because RP1210 drivers are 32-bit. `tools/assert-pe-i386.ps1 -Native <exe>` checks it.

iOS builds only for the simulator today. A device build needs an Apple Developer account, which is part of signing.

## Where CI already builds them

You don't need a local build to get the packages. The heavy CI legs build them and keep them as artifacts for 7 days (`docs/ci.md`).

| Artifact | Built on | Contents |
|---|---|---|
| `techbench-Linux` | push to `beta` or `main`, nightly | `.deb`, `.rpm`, `.AppImage` |
| `techbench-Windows` | push to `main`, nightly | `.msi`, NSIS `-setup.exe`, with the 32-bit bridge sidecar |
| `techbench-macOS` | nightly, manual | universal `.dmg` |
| `techbench-android-debug-apk` | push to `beta` or `main`, nightly | debug x86_64 APK (emulator) |

Each desktop leg runs `--smoke` on the package it built before it uploads. On Windows, the smoke must reach the 32-bit bridge (`pointerBits 32`).

## Release steps (on the maintainer's word only)

1. Bump the version in the three files (see "Version") in a PR into `beta`, and merge it on green.
2. Promote `beta` to `main` with the maintainer's OK. Wait for the `main` CI run to go green.
3. Download the `techbench-Windows` and `techbench-Linux` artifacts from that run (and `techbench-macOS` from a nightly or manual run of the same commit, if wanted).
4. Attach them to the release:

   ```sh
   # In the folder holding the downloaded artifacts:
   gh release create vX.Y.Z --draft --title "Tech Bench vX.Y.Z" --notes-file notes.md \
     *.msi *-setup.exe *.deb *.rpm *.AppImage
   ```

5. The maintainer reviews the draft and publishes it.

Self-update stays off. A build turns it on only by naming a feed and a signing key under `plugins.updater` in its Tauri config, and neither exists yet. Without that section, Settings says "Updates are not set up in this build. Install new versions with the shop Setup." The WinForms `Updater.DefaultManifestUrl` is unchanged and unrelated.

## Installing unsigned builds

| Platform | What the tech sees | How to go on |
|---|---|---|
| Windows | SmartScreen: "Windows protected your PC" | **More info**, then **Run anyway**. The MSI or setup installs per machine as usual. |
| macOS | "Tech Bench can't be opened because Apple cannot check it for malicious software" | In Finder, right-click the app, then **Open**, then **Open**. Only needed the first time. |
| Linux | No warning | `sudo apt install "./Tech Bench_X.Y.Z_amd64.deb"` (package `tech-bench`), `sudo rpm -i` the `.rpm`, or `chmod +x` the AppImage and run it. |
| Android | "For your security, your phone is not allowed to install unknown apps from this source" | Allow installs from the file manager or browser that opened the APK. |

## Not covered yet

- Code signing on every platform (Windows certificate or Azure Trusted Signing, Apple Developer ID, Android release keystore). These need the maintainer's own accounts and secrets.
- A workflow job that collects the artifacts into a draft release. It changes `.github/`, so it waits for the maintainer's OK.
- iOS device builds and store uploads.
