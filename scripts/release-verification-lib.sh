#!/usr/bin/env bash

validate_login_trace() {
  local trace="$1" expected_url_prefix="$2" expected_redirects="$3"
  local -a fields
  mapfile -t fields <<<"$trace"

  if [[ "${fields[0]:-}" != "200" ]]; then
    echo "Login flow ended with HTTP ${fields[0]:-missing}; expected 200." >&2
    return 1
  fi
  if [[ "${fields[1]:-}" != "$expected_redirects" ]]; then
    echo "Login flow used ${fields[1]:-missing} redirects; expected ${expected_redirects}." >&2
    return 1
  fi
  if [[ "${fields[2]:-}" != "${expected_url_prefix}"* ]]; then
    echo "Login flow ended at an unexpected URL: ${fields[2]:-missing}" >&2
    return 1
  fi
}
