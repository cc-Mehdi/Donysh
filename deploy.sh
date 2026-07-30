#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")"

if ! command -v docker >/dev/null 2>&1; then
  echo "Docker نصب نیست یا در PATH قرار ندارد." >&2
  exit 1
fi

if ! docker compose version >/dev/null 2>&1; then
  echo "Docker Compose در دسترس نیست." >&2
  exit 1
fi

if [ ! -f .env ]; then
  if command -v openssl >/dev/null 2>&1; then
    password="$(openssl rand -base64 48 | tr -d '\n')"
  else
    password="$(head -c 48 /dev/urandom | base64 | tr -d '\n')"
  fi

  cat > .env <<EOF_ENV
POSTGRES_DB=hesabyar
POSTGRES_USER=hesabyar
POSTGRES_PASSWORD=$password
TZ=Asia/Tehran
APP_PORT=8080
DOMAIN=example.com
ACME_EMAIL=admin@example.com
EOF_ENV
  chmod 600 .env 2>/dev/null || true
  echo ".env با رمز امن ساخته شد."
fi

echo "در حال ساخت و اجرای حساب‌یار..."
docker compose --env-file .env up -d --build

echo
echo "حساب‌یار اجرا شد:"
echo "http://localhost:8080"
echo "روی سرور: http://SERVER-IP:8080"
echo
docker compose --env-file .env ps
