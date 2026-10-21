#!/usr/bin/env bash
set -Eeuo pipefail

APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$APP_DIR/scripts/release-verification-lib.sh"

valid_trace=$'200\n3\nhttps://auth.donysh.ir/Account/Login?ReturnUrl=%2Fconnect%2Fauthorize'
wrong_status=$'500\n3\nhttps://auth.donysh.ir/Account/Login'
redirect_loop=$'200\n4\nhttps://auth.donysh.ir/Account/Login'
wrong_origin=$'200\n3\nhttps://attacker.example/Account/Login'

validate_login_trace "$valid_trace" "https://auth.donysh.ir/Account/Login" 3

if validate_login_trace "$wrong_status" "https://auth.donysh.ir/Account/Login" 3 >/dev/null 2>&1; then
  echo "A failed final response was accepted."
  exit 1
fi

if validate_login_trace "$redirect_loop" "https://auth.donysh.ir/Account/Login" 3 >/dev/null 2>&1; then
  echo "An unexpected redirect count was accepted."
  exit 1
fi

if validate_login_trace "$wrong_origin" "https://auth.donysh.ir/Account/Login" 3 >/dev/null 2>&1; then
  echo "A cross-origin final URL was accepted."
  exit 1
fi

echo "Release verification login trace tests passed."

if [[ "${RELEASE_CHECK_LIVE_LOGIN:-0}" == "1" ]]; then
  live_trace="$(curl \
    --fail \
    --silent \
    --show-error \
    --location \
    --max-redirs 4 \
    --output /dev/null \
    --write-out $'%{http_code}\n%{num_redirects}\n%{url_effective}\n' \
    https://finance.donysh.ir/)"
  validate_login_trace "$live_trace" "https://auth.donysh.ir/Account/Login" 3
  echo "Live Finance login trace passed."
fi
