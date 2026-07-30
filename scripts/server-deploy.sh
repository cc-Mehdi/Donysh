#!/usr/bin/env bash
set -Eeuo pipefail

APP_DIR="${APP_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
BRANCH="${DEPLOY_BRANCH:-main}"
ENV_FILE="${ENV_FILE:-.env}"
COMPOSE_FILE="${COMPOSE_FILE:-docker-compose.domain.yml}"
LOCK_FILE="${LOCK_FILE:-/tmp/donysh-deploy.lock}"

exec 9>"$LOCK_FILE"
if ! flock -n 9; then
  echo "Another deployment is already running."
  exit 1
fi

cd "$APP_DIR"

for command in git docker curl; do
  command -v "$command" >/dev/null 2>&1 || { echo "Required command not found: $command"; exit 1; }
done

docker compose version >/dev/null 2>&1 || { echo "Docker Compose v2 is required."; exit 1; }
[[ -f "$ENV_FILE" ]] || { echo "Missing $APP_DIR/$ENV_FILE"; exit 1; }
[[ -f "$COMPOSE_FILE" ]] || { echo "Missing $APP_DIR/$COMPOSE_FILE"; exit 1; }
[[ -d .git ]] || { echo "$APP_DIR is not a Git repository."; exit 1; }

previous_commit="$(git rev-parse HEAD)"
echo "Current commit: $previous_commit"

git fetch --prune origin "$BRANCH"
target_commit="$(git rev-parse "origin/$BRANCH")"

if [[ "$previous_commit" == "$target_commit" ]]; then
  echo "Server is already up to date."
  exit 0
fi

echo "Deploying commit: $target_commit"
git reset --hard "$target_commit"

rollback() {
  echo "Deployment failed. Rolling back to $previous_commit ..."
  git reset --hard "$previous_commit"
  docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" up -d --build --remove-orphans || true
}
trap rollback ERR

docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" config >/dev/null
docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" build web
docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" up -d --remove-orphans

# Read DOMAIN without executing the env file as shell code.
domain="$(sed -n 's/^DOMAIN=//p' "$ENV_FILE" | tail -n 1 | tr -d '\r' | xargs)"
health_url="${HEALTH_URL:-https://${domain}/health}"

for attempt in $(seq 1 24); do
  if curl --fail --silent --show-error --max-time 10 \
      --resolve "${domain}:443:127.0.0.1" \
      "$health_url" >/dev/null; then
    echo "Health check passed: $health_url"
    trap - ERR
    docker image prune -f >/dev/null 2>&1 || true
    docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" ps
    echo "Deployment completed successfully."
    exit 0
  fi

  echo "Waiting for health check ($attempt/24)..."
  sleep 5
done

echo "Health check failed: $health_url"
exit 1
