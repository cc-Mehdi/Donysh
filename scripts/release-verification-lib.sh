#!/usr/bin/env bash

resolve_same_origin_location() {
  local location="$1" expected_domain="$2"
  case "$location" in
    /*)
      printf 'https://%s%s\n' "$expected_domain" "$location"
      ;;
    "https://${expected_domain}/"*)
      printf '%s\n' "$location"
      ;;
    *)
      echo "Unexpected redirect target for ${expected_domain}: ${location}" >&2
      return 1
      ;;
  esac
}
