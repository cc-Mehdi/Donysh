#!/usr/bin/env bash
set -Eeuo pipefail

APP_DIR="${APP_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
BRANCH="${DEPLOY_BRANCH:-main}"
ENV_FILE="${ENV_FILE:-.env}"
COMPOSE_FILE="${COMPOSE_FILE:-docker-compose.domain.yml}"
LOCK_FILE="${LOCK_FILE:-/tmp/donysh-deploy.lock}"
LOCK_WAIT_SECONDS="${DEPLOY_LOCK_WAIT_SECONDS:-900}"
GIT_FETCH_TIMEOUT_SECONDS="${GIT_FETCH_TIMEOUT_SECONDS:-120}"
GIT_SSH_COMMAND="${GIT_SSH_COMMAND:-ssh -o BatchMode=yes -o ConnectTimeout=15 -o ServerAliveInterval=15 -o ServerAliveCountMax=3}"

for command in git docker curl flock timeout; do
  command -v "$command" >/dev/null 2>&1 || { echo "Required command not found: $command"; exit 1; }
done

if [[ ! "$LOCK_WAIT_SECONDS" =~ ^[0-9]+$ ]]; then
  echo "DEPLOY_LOCK_WAIT_SECONDS must be a non-negative integer."
  exit 1
fi

if [[ ! "$GIT_FETCH_TIMEOUT_SECONDS" =~ ^[1-9][0-9]*$ ]]; then
  echo "GIT_FETCH_TIMEOUT_SECONDS must be a positive integer."
  exit 1
fi

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

echo "Deploying commit: $target_commit"
git reset --hard "$target_commit" 9>&-

rollback() {
  echo "Deployment failed. Rolling back to $previous_commit ..."
  git reset --hard "$previous_commit" 9>&-
  docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" up -d --build --remove-orphans 9>&- || true
}
trap rollback ERR

docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" config 9>&- >/dev/null
docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" build web 9>&-
docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" up -d --remove-orphans 9>&-

# Read DOMAIN without executing the env file as shell code.
domain="$(sed -n 's/^DOMAIN=//p' "$ENV_FILE" | tail -n 1 | tr -d '\r' | xargs)"
health_url="${HEALTH_URL:-https://${domain}/health}"

for attempt in $(seq 1 24); do
  if curl --fail --silent --show-error --max-time 10 \
      --resolve "${domain}:443:127.0.0.1" \
      "$health_url" 9>&- >/dev/null; then
    echo "Health check passed: $health_url"
    trap - ERR
    docker image prune -f 9>&- >/dev/null 2>&1 || true
    docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" ps 9>&-
    echo "Deployment completed successfully."
    exit 0
  fi

  echo "Waiting for health check ($attempt/24)..."
  sleep 5 9>&-
done

echo "Health check failed: $health_url"
exit 1
