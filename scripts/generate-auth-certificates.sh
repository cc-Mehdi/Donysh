#!/usr/bin/env bash
set -euo pipefail

: "${AUTH_SIGNING_CERT_PASSWORD:?Set AUTH_SIGNING_CERT_PASSWORD first.}"
: "${AUTH_ENCRYPTION_CERT_PASSWORD:?Set AUTH_ENCRYPTION_CERT_PASSWORD first.}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output_dir="$repo_root/secrets/auth"
mkdir -p "$output_dir"

generate_certificate() {
  local name="$1"
  local password="$2"
  local temporary_key="$output_dir/$name.key"
  local temporary_certificate="$output_dir/$name.crt"

  openssl req -x509 -newkey rsa:3072 -sha256 -days 397 -nodes \
    -subj "/CN=Donysh Auth $name" \
    -keyout "$temporary_key" -out "$temporary_certificate"
  openssl pkcs12 -export -out "$output_dir/$name.pfx" \
    -inkey "$temporary_key" -in "$temporary_certificate" \
    -passout "pass:$password"
  rm -f -- "$temporary_key" "$temporary_certificate"
}

generate_certificate signing "$AUTH_SIGNING_CERT_PASSWORD"
generate_certificate encryption "$AUTH_ENCRYPTION_CERT_PASSWORD"
chmod 700 "$output_dir"
chmod 600 "$output_dir/signing.pfx" "$output_dir/encryption.pfx"
echo "Auth certificates generated in $output_dir. The directory is excluded from Git."
