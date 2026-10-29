#!/usr/bin/env bash
# Build the iOS app for the simulator (unsigned; TECHBENCH_SMOKE=1 bakes the smoke in),
# install it on a booted iPhone simulator and wait for the smoke line in its Documents folder.
set -euo pipefail

# Xcode's "Build Rust Code" phase only works under a running `tauri ios build`, so build
# through the CLI; a simulator target gives an .app, no archive or signing.
npx tauri ios build --debug --target aarch64-sim --ci
app=$(find src-tauri/gen/apple/build -maxdepth 4 -name '*.app' -path '*sim*' | head -n 1)
if [ -z "$app" ]; then
  echo "No simulator .app found. Build folder:"
  find src-tauri/gen/apple/build -maxdepth 4 | head -n 40
  exit 1
fi
echo "App: $app"
udid=$(xcrun simctl list devices available -j |
  python3 -c 'import json,sys; d=json.load(sys.stdin)["devices"]; print(next(x["udid"] for k in sorted(d, reverse=True) if "iOS" in k for x in d[k] if x["name"].startswith("iPhone")))')
echo "Simulator: $udid"
xcrun simctl boot "$udid" || true
xcrun simctl bootstatus "$udid" -b
xcrun simctl install "$udid" "$app"

# --smoke as a launch argument as well, in case the baked-in flag did not reach the Rust build.
xcrun simctl launch "$udid" com.techbench.app --smoke
data=$(xcrun simctl get_app_container "$udid" com.techbench.app data)
out="$data/Documents/techbench-smoke.txt"
echo "Smoke file: $out"
for _ in $(seq 1 90); do
  if grep -q "SMOKE PASS\|SMOKE FAIL" "$out" 2>/dev/null; then
    break
  fi
  sleep 2
done
cat "$out" 2>/dev/null || true
if ! grep -q "SMOKE PASS" "$out" 2>/dev/null; then
  echo "No SMOKE PASS. Data container:"
  find "$data" -maxdepth 3 | head -n 30 || true
  echo "Running app processes:"
  xcrun simctl spawn "$udid" launchctl list | grep -i tech || true
  echo "System log for the app:"
  xcrun simctl spawn "$udid" log show --last 5m --style compact \
    --predicate 'process CONTAINS[c] "tech" OR processImagePath CONTAINS[c] "tech"' | tail -n 60 || true
  exit 1
fi
