#!/usr/bin/env bash
set -euo pipefail
umask 077

: "${AUTH_SIGNING_CERT_PASSWORD:?Set AUTH_SIGNING_CERT_PASSWORD first.}"
: "${AUTH_ENCRYPTION_CERT_PASSWORD:?Set AUTH_ENCRYPTION_CERT_PASSWORD first.}"

container_uid="${AUTH_CONTAINER_UID:-1654}"
container_gid="${AUTH_CONTAINER_GID:-1654}"
[[ "$container_uid" =~ ^[0-9]+$ ]] || { echo "AUTH_CONTAINER_UID must be numeric." >&2; exit 1; }
[[ "$container_gid" =~ ^[0-9]+$ ]] || { echo "AUTH_CONTAINER_GID must be numeric." >&2; exit 1; }

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

# The Auth image deliberately runs as a non-root user. Keep the certificate
# private on the host while allowing only that container identity to read it.
if [[ "$(id -u)" -eq 0 ]]; then
  chown -R "$container_uid:$container_gid" "$output_dir"
elif [[ "$(id -u)" -ne "$container_uid" || "$(id -g)" -ne "$container_gid" ]]; then
  echo "Run this script with sudo so certificates can be assigned to the Auth container user." >&2
  exit 1
fi

echo "Auth certificates generated in $output_dir. The directory is excluded from Git."
