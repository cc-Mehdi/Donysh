#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="${ENV_FILE:-$repo_root/.env}"
compose_file="${COMPOSE_FILE:-$repo_root/docker-compose.platform.yml}"
mode="${1:---dry-run}"

if [[ "$mode" != "--dry-run" && "$mode" != "--apply" ]]; then
  printf 'Usage: %s [--dry-run|--apply]\n' "$0" >&2
  exit 2
fi

[[ -f "$env_file" ]] || { printf 'Missing environment file: %s\n' "$env_file" >&2; exit 1; }
[[ -f "$compose_file" ]] || { printf 'Missing compose file: %s\n' "$compose_file" >&2; exit 1; }

cd "$repo_root"

# Older installations may not yet contain settings introduced with central
# authentication. Complete them atomically with safe defaults before Compose
# interpolates the environment. Redirecting stdin keeps this operation
# non-interactive and preserves every existing secret.
COMPOSE_FILE="$compose_file" \
  bash "$repo_root/scripts/configure-server-env.sh" "$env_file" </dev/null

docker compose --env-file "$env_file" -f "$compose_file" up -d db auth-db auth

printf 'Waiting for the Auth database schema ...\n'
ready=false
for attempt in $(seq 1 30); do
  if printf '%s\n' \
      "SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'AspNetUsers';" \
      | docker compose --env-file "$env_file" -f "$compose_file" exec -T auth-db \
          sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tA' \
          2>/dev/null \
      | grep -qx '1'; then
    ready=true
    break
  fi
  sleep 2
done

[[ "$ready" == "true" ]] || {
  printf 'Auth database schema was not ready after 60 seconds. No users were copied.\n' >&2
  docker compose --env-file "$env_file" -f "$compose_file" logs --tail=50 auth auth-db >&2 || true
  exit 1
}
printf 'Auth database schema is ready.\n'

args=()
[[ "$mode" == "--apply" ]] && args+=(--apply)
docker compose --profile tools --env-file "$env_file" -f "$compose_file" \
  run --rm --build identity-migration "${args[@]}"

printf 'Identity migration %s finished. No feature flag was changed automatically.\n' "$mode"
