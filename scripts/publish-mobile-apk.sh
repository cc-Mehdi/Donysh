#!/usr/bin/env bash
set -Eeuo pipefail

APP_DIR="${APP_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
SOURCE_APK="${1:-}"
EXPECTED_SHA256="${2:-}"
VERSION_CODE="${3:-}"
DESTINATION_DIR="$APP_DIR/artifacts/mobile"
DESTINATION_APK="$DESTINATION_DIR/donysh-mobile.apk"
VERSION_FILE="$DESTINATION_DIR/donysh-mobile.version"
MAX_APK_BYTES=$((250 * 1024 * 1024))

for command in awk curl install mktemp mv od python3 rm sha256sum stat tr; do
  command -v "$command" >/dev/null 2>&1 || { echo "Required command not found: $command"; exit 1; }
done

if [[ -z "$SOURCE_APK" || -z "$EXPECTED_SHA256" || -z "$VERSION_CODE" ]]; then
  echo "Usage: bash scripts/publish-mobile-apk.sh /absolute/path/to/donysh-mobile.apk expected-sha256 version-code"
  exit 2
fi

if [[ ! -f "$SOURCE_APK" || -L "$SOURCE_APK" ]]; then
  echo "APK source must be a regular file and not a symbolic link: $SOURCE_APK"
  exit 2
fi

source_sha256="$(sha256sum "$SOURCE_APK" | awk '{print $1}')"
if [[ ! "$EXPECTED_SHA256" =~ ^[[:xdigit:]]{64}$ || "${EXPECTED_SHA256,,}" != "$source_sha256" ]]; then
  echo "Uploaded APK does not match the SHA-256 of the freshly built file."
  exit 2
fi
if [[ -f "$DESTINATION_APK" && "$(sha256sum "$DESTINATION_APK" | awk '{print $1}')" == "$source_sha256" ]]; then
  echo "This APK is identical to the currently published version; build and sign a new release first."
  exit 2
fi
if [[ ! "$VERSION_CODE" =~ ^[1-9][0-9]*$ ]]; then
  echo "Version code must be a positive integer."
  exit 2
fi
if [[ -f "$VERSION_FILE" ]]; then
  previous_version="$(<"$VERSION_FILE")"
  if [[ "$previous_version" =~ ^[1-9][0-9]*$ ]] && (( VERSION_CODE <= previous_version )); then
    echo "Version code $VERSION_CODE must be greater than published version $previous_version."
    exit 2
  fi
fi

apk_size="$(stat -c '%s' "$SOURCE_APK")"
if [[ ! "$apk_size" =~ ^[0-9]+$ ]] || (( apk_size < 1024 || apk_size > MAX_APK_BYTES )); then
  echo "APK size must be between 1 KB and 250 MB. Actual bytes: $apk_size"
  exit 2
fi

# APK files are ZIP archives and must start with the standard PK signature.
apk_signature="$(od -An -tx1 -N2 "$SOURCE_APK" | tr -d '[:space:]')"
if [[ "$apk_signature" != "504b" ]]; then
  echo "The selected file does not have a valid APK/ZIP signature."
  exit 2
fi

python3 - "$SOURCE_APK" <<'PY'
import sys
import zipfile

path = sys.argv[1]
try:
    with zipfile.ZipFile(path) as archive:
        if "AndroidManifest.xml" not in archive.namelist():
            raise ValueError("AndroidManifest.xml is missing")
        damaged_entry = archive.testzip()
        if damaged_entry is not None:
            raise ValueError(f"damaged archive entry: {damaged_entry}")
except (OSError, ValueError, zipfile.BadZipFile) as error:
    print(f"APK structure validation failed: {error}", file=sys.stderr)
    raise SystemExit(2)
PY

install -d -m 0755 "$DESTINATION_DIR"
temporary_apk="$(mktemp "$DESTINATION_DIR/.donysh-mobile.apk.XXXXXX")"
trap 'rm -f "$temporary_apk"' EXIT
install -m 0644 "$SOURCE_APK" "$temporary_apk"
mv -f "$temporary_apk" "$DESTINATION_APK"
printf '%s\n' "$VERSION_CODE" > "$VERSION_FILE"
trap - EXIT

echo "Published: $DESTINATION_APK"
echo "SHA-256: $(sha256sum "$DESTINATION_APK" | awk '{print $1}')"
echo "Public URL: https://donysh.ir/downloads/donysh-mobile.apk"
public_copy="$(mktemp)"
trap 'rm -f "$public_copy"' EXIT
if ! curl --fail --location --silent --show-error --max-time 120 \
    "https://donysh.ir/downloads/donysh-mobile.apk?release=$source_sha256" -o "$public_copy"; then
  echo "The APK was published, but its public download could not be verified."
  exit 1
fi
public_sha256="$(sha256sum "$public_copy" | awk '{print $1}')"
if [[ "$public_sha256" != "$source_sha256" ]]; then
  echo "The public download is stale: expected $source_sha256, received $public_sha256. Purge the CDN cache for the APK URL."
  exit 1
fi
echo "Public download verified: $public_sha256"
