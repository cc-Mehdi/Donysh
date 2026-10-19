#!/usr/bin/env bash
set -Eeuo pipefail

APP_DIR="${APP_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
ENV_FILE="${ENV_FILE:-.env}"
COMPOSE_FILE="${COMPOSE_FILE:-docker-compose.platform.yml}"
RESOLVE_IP="${RELEASE_CHECK_RESOLVE_IP:-127.0.0.1}"
REQUEST_TIMEOUT_SECONDS="${RELEASE_CHECK_TIMEOUT_SECONDS:-15}"

for command in curl grep sed mktemp tail tr xargs; do
  command -v "$command" >/dev/null 2>&1 || { echo "Required command not found: $command"; exit 1; }
done

cd "$APP_DIR"
[[ -f "$ENV_FILE" ]] || { echo "Missing environment file: $APP_DIR/$ENV_FILE"; exit 1; }

if [[ ! "$REQUEST_TIMEOUT_SECONDS" =~ ^[1-9][0-9]*$ ]]; then
  echo "RELEASE_CHECK_TIMEOUT_SECONDS must be a positive integer."
  exit 1
fi

read_env_value() {
  local key="$1"
  sed -n "s/^${key}=//p" "$ENV_FILE" | tail -n 1 | tr -d '\r' | xargs
}

validate_domain() {
  local name="$1" value="$2"
  if [[ ! "$value" =~ ^([A-Za-z0-9][A-Za-z0-9-]*\.)+[A-Za-z]{2,63}$ ]]; then
    echo "Invalid or missing ${name}; release verification stopped."
    exit 1
  fi
}

domain="$(read_env_value DOMAIN)"
finance_domain="$(read_env_value FINANCE_DOMAIN)"
auth_domain="$(read_env_value AUTH_DOMAIN)"
life_domain="$(read_env_value LIFE_DOMAIN)"
validate_domain DOMAIN "$domain"
validate_domain FINANCE_DOMAIN "$finance_domain"
validate_domain AUTH_DOMAIN "$auth_domain"
validate_domain LIFE_DOMAIN "$life_domain"

curl_args=(--fail --silent --show-error --max-time "$REQUEST_TIMEOUT_SECONDS")
if [[ -n "$RESOLVE_IP" ]]; then
  curl_args+=(--resolve "${domain}:443:${RESOLVE_IP}")
  curl_args+=(--resolve "${finance_domain}:443:${RESOLVE_IP}")
  curl_args+=(--resolve "${auth_domain}:443:${RESOLVE_IP}")
  curl_args+=(--resolve "${life_domain}:443:${RESOLVE_IP}")
fi

assert_health() {
  local label="$1" url="$2" expected="$3" body
  body="$(curl "${curl_args[@]}" "$url")"
  if ! grep -Eq "\"status\"[[:space:]]*:[[:space:]]*\"${expected}\"" <<<"$body"; then
    echo "${label} returned an unexpected health payload."
    exit 1
  fi
  echo "PASS ${label}"
}

headers_file="$(mktemp)"
trap 'rm -f "$headers_file"' EXIT

assert_header() {
  local header="$1" pattern="$2"
  if ! grep -Eiq "^${header}:[[:space:]]*${pattern}" "$headers_file"; then
    echo "Missing or invalid ${header} header."
    exit 1
  fi
}

assert_status() {
  local pattern="$1"
  if ! grep -Eiq "^HTTP/[0-9.]+[[:space:]]+${pattern}([[:space:]]|$)" "$headers_file"; then
    echo "Unexpected HTTP status; expected ${pattern}."
    exit 1
  fi
}

assert_health "Landing health" "https://${domain}/health" "ok"
assert_health "Finance health" "https://${finance_domain}/health" "ok"
assert_health "Auth readiness" "https://${auth_domain}/health/ready" "ready"
assert_health "Life readiness" "https://${life_domain}/health/ready" "ready"

curl "${curl_args[@]}" -D "$headers_file" -o /dev/null "https://${life_domain}/"
assert_status "30[1278]"
assert_header "location" "https://${auth_domain}/connect/authorize"
echo "PASS Life central login redirect"

: >"$headers_file"
curl "${curl_args[@]}" -D "$headers_file" -o /dev/null "https://${life_domain}/Account/Login"
assert_status "200"
assert_header "content-security-policy" ".*script-src 'self'"
assert_header "content-security-policy" ".*frame-ancestors 'none'"
assert_header "cross-origin-resource-policy" "same-origin"
assert_header "x-permitted-cross-domain-policies" "none"
echo "PASS Life browser security headers"

: >"$headers_file"
curl "${curl_args[@]}" -H "Accept-Encoding: gzip" -D "$headers_file" -o /dev/null \
  "https://${life_domain}/css/app.css"
assert_status "200"
assert_header "cache-control" ".*public.*max-age=604800"
assert_header "content-encoding" "gzip"
echo "PASS Life static asset cache and compression"

if [[ "${RELEASE_CHECK_SKIP_DOCKER:-0}" != "1" ]]; then
  command -v docker >/dev/null 2>&1 || { echo "Required command not found: docker"; exit 1; }
  docker compose version >/dev/null 2>&1 || { echo "Docker Compose v2 is required."; exit 1; }
  [[ -f "$COMPOSE_FILE" ]] || { echo "Missing compose file: $APP_DIR/$COMPOSE_FILE"; exit 1; }
  running_services="$(docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" ps --status running --services)"
  for service in db web auth-db auth landing life-db life caddy; do
    if ! grep -Fxq "$service" <<<"$running_services"; then
      echo "Required service is not running: $service"
      exit 1
    fi
  done
  echo "PASS Required containers are running"
fi

echo "Platform release verification passed."
