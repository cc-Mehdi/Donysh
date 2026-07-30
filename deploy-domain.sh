#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")"

if [ ! -f .env ]; then
  echo "ابتدا ./deploy.sh را یک بار اجرا کنید تا فایل .env ساخته شود." >&2
  exit 1
fi

if grep -q '^DOMAIN=example\.com$' .env || grep -q '^ACME_EMAIL=admin@example\.com$' .env; then
  echo "مقادیر DOMAIN و ACME_EMAIL را داخل فایل .env با دامنه و ایمیل واقعی جایگزین کنید." >&2
  exit 1
fi

docker compose --env-file .env -f docker-compose.domain.yml up -d --build
docker compose --env-file .env -f docker-compose.domain.yml ps
