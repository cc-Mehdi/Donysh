#!/usr/bin/env bash
set -Eeuo pipefail

APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$APP_DIR/scripts/release-verification-lib.sh"

domain="finance.donysh.ir"
relative_location="/Account/Login?ReturnUrl=%2F"
absolute_location="https://${domain}/Account/Login?ReturnUrl=%2F"

[[ "$(resolve_same_origin_location "$relative_location" "$domain")" == "$absolute_location" ]]
[[ "$(resolve_same_origin_location "$absolute_location" "$domain")" == "$absolute_location" ]]

if resolve_same_origin_location "https://attacker.example/Account/Login" "$domain" >/dev/null 2>&1; then
  echo "Cross-origin redirect was accepted."
  exit 1
fi

if resolve_same_origin_location "http://${domain}/Account/Login" "$domain" >/dev/null 2>&1; then
  echo "Insecure redirect was accepted."
  exit 1
fi

echo "Release verification URL tests passed."
