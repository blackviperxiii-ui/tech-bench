#!/usr/bin/env bash
# Build the generated Xcode project for the iOS simulator (unsigned; TECHBENCH_SMOKE=1 bakes the
# smoke in), install it on a booted iPhone simulator and wait for the smoke line on its console.
set -euo pipefail

proj=$(ls -d src-tauri/gen/apple/*.xcodeproj | head -n 1)
scheme=$(xcodebuild -list -project "$proj" | awk '/Schemes:/{f=1;next} f&&/_iOS/{gsub(/^ +/,"");print;exit}')
echo "Project: $proj  Scheme: $scheme"
xcodebuild -project "$proj" -scheme "$scheme" -configuration debug \
  -sdk iphonesimulator -arch arm64 -derivedDataPath build/ios \
  CODE_SIGNING_ALLOWED=NO build | tail -n 20

app=$(find build/ios/Build/Products -name '*.app' -maxdepth 2 | head -n 1)
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
