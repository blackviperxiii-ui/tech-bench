#!/usr/bin/env bash
# Build the iOS app for the simulator (unsigned; TECHBENCH_SMOKE=1 bakes the smoke in),
# install it on a booted iPhone simulator and wait for the smoke line on its console.
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

log=$(mktemp)
xcrun simctl launch --console-pty "$udid" com.techbench.app >"$log" 2>&1 &
for _ in $(seq 1 90); do
  if grep -q "SMOKE PASS\|SMOKE FAIL" "$log"; then
    break
  fi
  sleep 2
done
grep "SMOKE" "$log" || tail -n 20 "$log"
grep -q "SMOKE PASS" "$log"
