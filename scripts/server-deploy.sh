#!/usr/bin/env bash
set -Eeuo pipefail

APP_DIR="${APP_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
BRANCH="${DEPLOY_BRANCH:-main}"
ENV_FILE="${ENV_FILE:-.env}"
COMPOSE_FILE="${COMPOSE_FILE:-docker-compose.platform.yml}"
LOCK_FILE="${LOCK_FILE:-/tmp/donysh-deploy.lock}"
LOCK_WAIT_SECONDS="${DEPLOY_LOCK_WAIT_SECONDS:-900}"
GIT_FETCH_TIMEOUT_SECONDS="${GIT_FETCH_TIMEOUT_SECONDS:-120}"
DOCKER_BUILD_TIMEOUT_SECONDS="${DOCKER_BUILD_TIMEOUT_SECONDS:-900}"
DB_HEALTH_WAIT_SECONDS="${DB_HEALTH_WAIT_SECONDS:-60}"
MIN_DISK_FREE_MB="${MIN_DISK_FREE_MB:-1024}"
GIT_SSH_COMMAND="${GIT_SSH_COMMAND:-ssh -o BatchMode=yes -o ConnectTimeout=15 -o ServerAliveInterval=15 -o ServerAliveCountMax=3}"

for command in git docker curl flock timeout sed tail tr xargs df awk seq sleep; do
  command -v "$command" >/dev/null 2>&1 || { echo "Required command not found: $command"; exit 1; }
done

for setting in LOCK_WAIT_SECONDS GIT_FETCH_TIMEOUT_SECONDS DOCKER_BUILD_TIMEOUT_SECONDS DB_HEALTH_WAIT_SECONDS MIN_DISK_FREE_MB; do
  value="${!setting}"
  if [[ ! "$value" =~ ^[1-9][0-9]*$ ]] && [[ "$setting" != "LOCK_WAIT_SECONDS" || ! "$value" =~ ^[0-9]+$ ]]; then
    echo "$setting must be a positive integer."
    exit 1
  fi
done

exec 9>"$LOCK_FILE"
echo "Waiting up to ${LOCK_WAIT_SECONDS}s for the deployment lock ..."
if ! flock -w "$LOCK_WAIT_SECONDS" 9; then
  echo "Another deployment is still running after ${LOCK_WAIT_SECONDS}s."
  exit 1
fi
echo "Deployment lock acquired."

cd "$APP_DIR"

docker compose version 9>&- >/dev/null 2>&1 || { echo "Docker Compose v2 is required."; exit 1; }
[[ -f "$ENV_FILE" ]] || { echo "Missing $APP_DIR/$ENV_FILE"; exit 1; }
[[ -f "$COMPOSE_FILE" ]] || { echo "Missing $APP_DIR/$COMPOSE_FILE"; exit 1; }
[[ -d .git ]] || { echo "$APP_DIR is not a Git repository."; exit 1; }

compose() {
  docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" "$@"
}

check_disk_capacity() {
  local path="$1" label="$2" available_kb required_kb
  available_kb="$(df -Pk "$path" | awk 'NR == 2 { print $4 }')"
  required_kb=$((MIN_DISK_FREE_MB * 1024))
  if [[ ! "$available_kb" =~ ^[0-9]+$ ]] || (( available_kb < required_kb )); then
    echo "Insufficient free disk space for ${label}; at least ${MIN_DISK_FREE_MB} MB is required."
    return 1
  fi
  echo "Disk preflight passed for ${label}: $((available_kb / 1024)) MB free."
}

diagnose_service() {
  local service="$1" container_id
  container_id="$(compose ps -q "$service" 9>&- 2>/dev/null || true)"
  if [[ -z "$container_id" ]]; then
    echo "No container exists for ${service}."
    return
  fi
  docker inspect --format \
    'status={{.State.Status}} oomKilled={{.State.OOMKilled}} exitCode={{.State.ExitCode}} health={{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' \
    "$container_id" 9>&- || true
  docker inspect --format '{{if .State.Health}}{{range .State.Health.Log}}{{println .End "exit=" .ExitCode .Output}}{{end}}{{end}}' \
    "$container_id" 9>&- 2>/dev/null | tail -n 5 || true
}

wait_for_database() {
  local service="$1" deadline container_id health state
  deadline=$((SECONDS + DB_HEALTH_WAIT_SECONDS))
  while (( SECONDS < deadline )); do
    container_id="$(compose ps -q "$service" 9>&- 2>/dev/null || true)"
    if [[ -n "$container_id" ]]; then
      health="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$container_id" 9>&- 2>/dev/null || true)"
      state="$(docker inspect --format '{{.State.Status}}' "$container_id" 9>&- 2>/dev/null || true)"
      [[ "$health" == "healthy" ]] && return 0
      [[ "$health" == "unhealthy" || "$state" == "exited" || "$state" == "dead" ]] && return 1
    fi
    sleep 3 9>&-
  done
  return 1
}

ensure_database() {
  local service="$1"
  echo "Checking database service: ${service}"
  compose up -d "$service" 9>&-
  if wait_for_database "$service"; then
    echo "Database service is healthy: ${service}"
    return 0
  fi

  echo "Database service ${service} is not healthy; diagnostic state follows."
  diagnose_service "$service"
  echo "Restarting ${service} once before stopping the deployment ..."
  compose restart "$service" 9>&-
  if wait_for_database "$service"; then
    echo "Database service recovered after one restart: ${service}"
    return 0
  fi

  echo "Database service did not recover: ${service}"
  diagnose_service "$service"
  return 1
}

add_build_service() {
  local candidate="$1" existing
  for existing in "${build_services[@]:-}"; do
    [[ "$existing" == "$candidate" ]] && return
  done
  build_services+=("$candidate")
}

select_build_services() {
  local file
  while IFS= read -r file; do
    case "$file" in
      HesabYar.Web/wwwroot/fonts/*|HesabYar.Web/wwwroot/images/*)
        add_build_service web; add_build_service auth; add_build_service landing; add_build_service life ;;
      HesabYar.Web/*) add_build_service web ;;
      Donysh.Auth/*) add_build_service auth ;;
      Donysh.Landing/*) add_build_service landing ;;
      Donysh.Life/*) add_build_service life ;;
      docker-compose.platform.yml|.dockerignore|global.json|Directory.*|NuGet.config)
        add_build_service web; add_build_service auth; add_build_service landing; add_build_service life ;;
      Caddyfile.platform) recreate_caddy=1 ;;
    esac
  done < <(git diff --name-only "$previous_commit" "$target_commit" 9>&-)
}

build_selected_images() {
  if (( ${#build_services[@]} == 0 )); then
    echo "No application image changed; Docker build skipped."
    return 0
  fi
  echo "Building changed images sequentially: ${build_services[*]}"
  COMPOSE_PARALLEL_LIMIT=1 timeout --signal=TERM --kill-after=30s \
    "${DOCKER_BUILD_TIMEOUT_SECONDS}s" \
    docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" build "${build_services[@]}" 9>&-
}

check_disk_capacity "$APP_DIR" "application checkout"
docker_root="$(docker info --format '{{.DockerRootDir}}' 9>&- 2>/dev/null || true)"
if [[ -n "$docker_root" && -d "$docker_root" ]]; then
  check_disk_capacity "$docker_root" "Docker storage"
fi

compose config 9>&- >/dev/null
for database_service in db auth-db life-db; do
  ensure_database "$database_service"
done

previous_commit="$(git rev-parse HEAD 9>&-)"
echo "Current commit: $previous_commit"

echo "Fetching origin/$BRANCH (timeout: ${GIT_FETCH_TIMEOUT_SECONDS}s) ..."
if GIT_TERMINAL_PROMPT=0 GIT_SSH_COMMAND="$GIT_SSH_COMMAND" \
    timeout --signal=TERM --kill-after=10s "${GIT_FETCH_TIMEOUT_SECONDS}s" \
    git fetch --prune origin "$BRANCH" 9>&-; then
  :
else
  fetch_status=$?
  if [[ "$fetch_status" -eq 124 || "$fetch_status" -eq 137 ]]; then
    echo "Git fetch timed out after ${GIT_FETCH_TIMEOUT_SECONDS}s. Deployment stopped before changing the application."
  else
    echo "Git fetch failed with exit code $fetch_status. Deployment stopped before changing the application."
  fi
  exit "$fetch_status"
fi
target_commit="$(git rev-parse "origin/$BRANCH" 9>&-)"

if [[ "$previous_commit" == "$target_commit" ]]; then
  echo "Server is already up to date."
  exit 0
fi

build_services=()
recreate_caddy=0
select_build_services

echo "Deploying commit: $target_commit"
git reset --hard "$target_commit" 9>&-

rollback() {
  local exit_code=$?
  trap - ERR
  set +e
  echo "Deployment failed. Rolling back application code to $previous_commit ..."
  git reset --hard "$previous_commit" 9>&-
  bash scripts/configure-server-env.sh "$ENV_FILE" 9>&-
  compose config 9>&- >/dev/null
  build_selected_images
  compose up -d --remove-orphans 9>&-
  echo "Rollback attempt finished with original exit code ${exit_code}."
  exit "$exit_code"
}
trap rollback ERR

# Adds newly introduced settings with secure defaults without replacing existing
# database or certificate secrets. This keeps unattended deployments upgrade-safe.
bash scripts/configure-server-env.sh "$ENV_FILE" 9>&-

compose config 9>&- >/dev/null
build_selected_images

# A resource-constrained host can temporarily disturb PostgreSQL while BuildKit is
# active. Verify databases again before Compose evaluates service dependencies.
for database_service in db auth-db life-db; do
  ensure_database "$database_service"
done

compose up -d --remove-orphans 9>&-
if (( recreate_caddy == 1 )); then
  compose up -d --no-deps --force-recreate caddy 9>&-
fi

# Read DOMAIN without executing the env file as shell code.
domain="$(sed -n 's/^DOMAIN=//p' "$ENV_FILE" | tail -n 1 | tr -d '\r' | xargs)"
finance_domain="$(sed -n 's/^FINANCE_DOMAIN=//p' "$ENV_FILE" | tail -n 1 | tr -d '\r' | xargs)"
auth_domain="$(sed -n 's/^AUTH_DOMAIN=//p' "$ENV_FILE" | tail -n 1 | tr -d '\r' | xargs)"
life_domain="$(sed -n 's/^LIFE_DOMAIN=//p' "$ENV_FILE" | tail -n 1 | tr -d '\r' | xargs)"
health_url="${HEALTH_URL:-https://${domain}/health}"
finance_health_url="${FINANCE_HEALTH_URL:-https://${finance_domain}/health}"
auth_health_url="${AUTH_HEALTH_URL:-https://${auth_domain}/health/ready}"
life_health_url="${LIFE_HEALTH_URL:-https://${life_domain}/health/ready}"

for attempt in $(seq 1 24); do
  if curl --fail --silent --show-error --max-time 10 \
      --resolve "${domain}:443:127.0.0.1" \
      "$health_url" 9>&- >/dev/null \
      && curl --fail --silent --show-error --max-time 10 \
      --resolve "${finance_domain}:443:127.0.0.1" \
      "$finance_health_url" 9>&- >/dev/null \
      && curl --fail --silent --show-error --max-time 10 \
      --resolve "${auth_domain}:443:127.0.0.1" \
      "$auth_health_url" 9>&- >/dev/null \
      && curl --fail --silent --show-error --max-time 10 \
      --resolve "${life_domain}:443:127.0.0.1" \
      "$life_health_url" 9>&- >/dev/null; then
    echo "Health checks passed: Landing $health_url, Finance $finance_health_url, Auth $auth_health_url and Life $life_health_url"
    echo "Running production release verification ..."
    APP_DIR="$APP_DIR" ENV_FILE="$ENV_FILE" COMPOSE_FILE="$COMPOSE_FILE" \
      bash scripts/verify-platform-release.sh 9>&-
    trap - ERR
    docker image prune -f 9>&- >/dev/null 2>&1 || true
    compose ps 9>&-
    echo "Deployment completed successfully."
    exit 0
  fi

  echo "Waiting for health check ($attempt/24)..."
  sleep 5 9>&-
done

echo "A health check failed: $health_url, $finance_health_url, $auth_health_url or $life_health_url"
exit 1
