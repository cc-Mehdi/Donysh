#!/usr/bin/env sh
set -eu

ARCHIVE="${1:-IRANYekanX Pro.rar}"
PROJECT_ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"
TARGET_DIR="$PROJECT_ROOT/HesabYar.Web/wwwroot/fonts"
TARGET_FILE="$TARGET_DIR/IRANYekanXVFaNumVF.woff2"
INNER_PATH='IRANYekanX Pro/Farsi numerals/Variable Font/Webfonts/IRANYekanXVFaNumVF.woff2'
TEMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TEMP_DIR"' EXIT

if [ ! -f "$ARCHIVE" ]; then
  echo "Font archive not found: $ARCHIVE" >&2
  exit 1
fi

mkdir -p "$TARGET_DIR"

if command -v unrar >/dev/null 2>&1; then
  unrar x -inul -y "$ARCHIVE" "$INNER_PATH" "$TEMP_DIR/"
elif command -v 7z >/dev/null 2>&1; then
  7z x -y "-o$TEMP_DIR" "$ARCHIVE" "$INNER_PATH" >/dev/null
else
  echo "Install unrar or 7z, or extract the font manually." >&2
  exit 1
fi

FOUND="$TEMP_DIR/$INNER_PATH"
if [ ! -f "$FOUND" ]; then
  echo "Expected font file was not found in the archive." >&2
  exit 1
fi

cp "$FOUND" "$TARGET_FILE"
echo "IRANYekanX Pro installed at: $TARGET_FILE"
