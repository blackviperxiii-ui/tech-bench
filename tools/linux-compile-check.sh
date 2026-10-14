#!/usr/bin/env bash
# Linux compile check for Tech Bench (rearch stage 2).
# Catches C# compile errors (CS0136, missing rsp entries, etc.) on agent boxes
# that lack Windows .NET Framework 4 csc. Not a substitute for test.bat or the
# Windows CI installer job (x86 PE, SelfTest run, LayoutAudit, Setup smoke).
#
# Uses -langversion:5 so Mono matches in-box Framework csc (C# 5). Without that,
# mcs defaults to C# 7 and can green-light nameof / ?. / interpolation that CI rejects.
#
# Needs: Mono C# compiler (mcs). On Ubuntu: sudo apt-get install -y mono-devel
# Usage (from anywhere): tools/linux-compile-check.sh
# Exit 0 = SelfTest + TechBench both compiled; non-zero = compile failure or missing mcs.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

if [[ ! -f sources/core.list ]]; then
  echo "linux-compile-check: could not find sources/core.list (repo root?)" >&2
  exit 1
fi

# Regenerate sources/*.rsp from *.list + tests/*.cs (same as build.bat / test.bat).
bash tools/gen-rsp.sh
if [[ ! -f sources/core.rsp ]]; then
  echo "linux-compile-check: gen-rsp did not write sources/core.rsp" >&2
  exit 1
fi

if ! command -v mcs >/dev/null 2>&1; then
  echo "linux-compile-check: mcs not found. Install Mono, e.g.:" >&2
  echo "  sudo apt-get install -y mono-devel" >&2
  exit 1
fi

MONO_LIB=""
for d in /usr/lib/mono/4.5 /usr/lib/mono/4.7.2-api /usr/lib/mono/4.8-api; do
  if [[ -f "$d/System.Web.Extensions.dll" && -f "$d/System.Security.dll" ]]; then
    MONO_LIB="$d"
    break
  fi
done
if [[ -z "$MONO_LIB" ]]; then
  echo "linux-compile-check: Mono 4.x reference assemblies not found under /usr/lib/mono" >&2
  exit 1
fi

TMP="$(mktemp -d "${TMPDIR:-/tmp}/tb-linux-compile.XXXXXX")"
cleanup() { rm -rf "$TMP"; }
trap cleanup EXIT

# mcs does not treat '#' rsp comments like Windows csc; also needs '/' paths.
clean_rsp() {
  local src="$1" dst="$2"
  awk '
    /^[[:space:]]*#/ { next }
    /^[[:space:]]*$/ { next }
    {
      gsub(/\\/, "/")
      print
    }
  ' "$src" > "$dst"
}

clean_rsp sources/core.rsp "$TMP/core.rsp"
clean_rsp sources/selftest.rsp "$TMP/selftest.rsp"
clean_rsp sources/app.rsp "$TMP/app.rsp"

REFS=(
  -r:System.Windows.Forms.dll
  -r:System.Drawing.dll
  -r:"$MONO_LIB/System.Web.Extensions.dll"
  -r:"$MONO_LIB/System.Security.dll"
)

KB_RES=(
  -resource:kb/data/kb.json,TechBench.BundledKb.data.kb.json
  -resource:kb/data/passwords/ifix-passwords.json,TechBench.BundledKb.data.passwords.ifix-passwords.json
  -resource:kb/data/usb-manuals.json,TechBench.BundledKb.data.usb-manuals.json
  -resource:kb/data/rental-portable-filters-oil.json,TechBench.BundledKb.data.rental-portable-filters-oil.json
  -resource:kb/data/rental-equipment-info.json,TechBench.BundledKb.data.rental-equipment-info.json
)

# Match Windows .NET Framework 4 csc language level (C# 5).
# Do not name this LANG — that overwrites the shell locale variable.
CS_LANG=(-langversion:5)

echo "== linux-compile-check: SelfTest (core + selftest) =="
mcs -nologo "${CS_LANG[@]}" -out:"$TMP/SelfTest.exe" \
  "${REFS[@]}" \
  "${KB_RES[@]}" \
  SelfTest.cs CrashForm.cs @"$TMP/core.rsp" @"$TMP/selftest.rsp"
echo "  OK  $TMP/SelfTest.exe"

echo "== linux-compile-check: TechBench (core + app) =="
APP_RES=(
  "${KB_RES[@]}"
  -resource:assets/cheatsheet.rtf,TechBench.assets.cheatsheet.rtf
)
mcs -nologo "${CS_LANG[@]}" -target:winexe -out:"$TMP/TechBench.exe" \
  "${REFS[@]}" \
  "${APP_RES[@]}" \
  @"$TMP/core.rsp" @"$TMP/app.rsp"
echo "  OK  $TMP/TechBench.exe"

echo "linux-compile-check: ALL COMPILE OK (mcs $(mcs --version 2>&1 | head -1))"
