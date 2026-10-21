#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TEMP_DIR="$(mktemp -d)"
[[ "$TEMP_DIR" == /tmp/* ]] || { echo "Unexpected temporary directory: $TEMP_DIR"; exit 1; }
cleanup() {
  rm -f "$TEMP_DIR/source.apk" "$TEMP_DIR/output" \
    "$TEMP_DIR/artifacts/mobile/donysh-mobile.apk" "$TEMP_DIR/artifacts/mobile/donysh-mobile.version"
  rmdir "$TEMP_DIR/artifacts/mobile" "$TEMP_DIR/artifacts" "$TEMP_DIR"
}
trap cleanup EXIT
mkdir -p "$TEMP_DIR/artifacts/mobile"
printf PK > "$TEMP_DIR/source.apk"
sha="$(sha256sum "$TEMP_DIR/source.apk")"
sha="${sha%% *}"

cp "$TEMP_DIR/source.apk" "$TEMP_DIR/artifacts/mobile/donysh-mobile.apk"
if APP_DIR="$TEMP_DIR" bash "$ROOT/scripts/publish-mobile-apk.sh" "$TEMP_DIR/source.apk" "$sha" 9 > "$TEMP_DIR/output" 2>&1; then
  echo "FAIL: identical APK was accepted"; exit 1
fi
grep -q 'identical to the currently published' "$TEMP_DIR/output"

printf different > "$TEMP_DIR/artifacts/mobile/donysh-mobile.apk"
wrong_sha="${sha%?}0"
[[ "$wrong_sha" == "$sha" ]] && wrong_sha="${sha%?}1"
if APP_DIR="$TEMP_DIR" bash "$ROOT/scripts/publish-mobile-apk.sh" "$TEMP_DIR/source.apk" "$wrong_sha" 9 > "$TEMP_DIR/output" 2>&1; then
  echo "FAIL: mismatched source hash was accepted"; exit 1
fi
grep -q 'does not match the SHA-256' "$TEMP_DIR/output"

printf '9\n' > "$TEMP_DIR/artifacts/mobile/donysh-mobile.version"
if APP_DIR="$TEMP_DIR" bash "$ROOT/scripts/publish-mobile-apk.sh" "$TEMP_DIR/source.apk" "$sha" 9 > "$TEMP_DIR/output" 2>&1; then
  echo "FAIL: repeated version code was accepted"; exit 1
fi
grep -q 'must be greater than published version' "$TEMP_DIR/output"
echo "PASS APK publication rejects repeated artifacts, mismatched hashes and old version codes"
