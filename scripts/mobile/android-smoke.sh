#!/usr/bin/env bash
# Install the smoke APK (built with TECHBENCH_SMOKE=1) on the running emulator, start it, and
# wait for the smoke line it writes to logcat under the TechBench tag. Exit 0 only on PASS.
set -euo pipefail

apk=$(find src-tauri/gen/android/app/build/outputs/apk -name '*.apk' | head -n 1)
echo "APK: $apk"
adb install -r "$apk"
adb logcat -c
adb shell am start -W -n com.techbench.app/.MainActivity

out=""
for _ in $(seq 1 90); do
  out=$(adb logcat -d -s TechBench:I || true)
  if grep -q "SMOKE PASS\|SMOKE FAIL" <<<"$out"; then
    break
  fi
  sleep 2
done
echo "$out"
if ! grep -q "SMOKE PASS" <<<"$out"; then
  echo "No SMOKE PASS. Last app log lines:"
  adb logcat -d | grep -i "techbench\|tauri\|chromium\|AndroidRuntime" | tail -n 20 || true
  exit 1
fi
