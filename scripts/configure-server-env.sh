#!/usr/bin/env bash
set -euo pipefail

# Securely creates or updates the server-side .env used by Docker Compose.
# Secrets are generated locally, never printed, and written through an atomic rename.

umask 077

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="${1:-${ENV_FILE:-$repo_root/.env}}"
compose_file="${COMPOSE_FILE:-$repo_root/docker-compose.platform.yml}"

fail() {
  printf 'خطا: %s\n' "$1" >&2
  exit 1
}

[[ "$env_file" = /* ]] || env_file="$repo_root/$env_file"
[[ "$compose_file" = /* ]] || compose_file="$repo_root/$compose_file"
[[ ! -L "$env_file" ]] || fail "فایل .env نباید پیوند نمادین باشد."

env_dir="$(dirname "$env_file")"
[[ -d "$env_dir" ]] || fail "پوشهٔ فایل .env وجود ندارد: $env_dir"
[[ -w "$env_dir" ]] || fail "اجازهٔ نوشتن در پوشهٔ فایل .env وجود ندارد."

declare -A current=()
declare -A desired=()
declare -A managed=()

managed_keys=(
  POSTGRES_DB POSTGRES_USER POSTGRES_PASSWORD TZ APP_PORT DOMAIN FINANCE_DOMAIN ACME_EMAIL
  AUTH_DOMAIN AUTH_POSTGRES_DB AUTH_POSTGRES_USER AUTH_POSTGRES_PASSWORD
  AUTH_SIGNING_CERT_PASSWORD AUTH_ENCRYPTION_CERT_PASSWORD
  FINANCE_OIDC_CLIENT_SECRET CROSS_APP_LOGOUT_SECRET LEGACY_IDENTITY_IMPORT_COMPLETED CENTRAL_AUTH_ENABLED
  LIFE_DOMAIN LIFE_POSTGRES_DB LIFE_POSTGRES_USER LIFE_POSTGRES_PASSWORD LIFE_OIDC_CLIENT_SECRET
)

for key in "${managed_keys[@]}"; do
  managed["$key"]=1
done

if [[ -f "$env_file" ]]; then
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"
    if [[ "$line" =~ ^([A-Z][A-Z0-9_]*)=(.*)$ ]]; then
      current["${BASH_REMATCH[1]}"]="${BASH_REMATCH[2]}"
    fi
  done < "$env_file"
fi

existing_or_default() {
  local key="$1"
  local fallback="$2"
  local value="${current[$key]:-}"
  [[ -n "$value" ]] && printf '%s' "$value" || printf '%s' "$fallback"
}

prompt_value() {
  local key="$1"
  local label="$2"
  local fallback="$3"
  local value="${!key:-$(existing_or_default "$key" "$fallback")}"
  local input=''

  if [[ -t 0 ]]; then
    read -r -p "$label [$value]: " input
    [[ -n "$input" ]] && value="$input"
  fi
  desired["$key"]="$value"
}

is_placeholder_secret() {
  local value="${1:-}"
  [[ -z "$value" || "$value" == REPLACE_WITH_* || "$value" == "changeme" || "$value" == "CHANGE_ME" ]]
}

random_secret() {
  if command -v openssl >/dev/null 2>&1; then
    openssl rand -hex 32
  else
    od -An -N32 -tx1 /dev/urandom | tr -d ' \n'
  fi
}

keep_or_generate_secret() {
  local key="$1"
  local old_value="${current[$key]:-}"
  if is_placeholder_secret "$old_value"; then
    desired["$key"]="$(random_secret)"
    printf 'یک مقدار امن برای %s تولید شد.\n' "$key"
  else
    desired["$key"]="$old_value"
    printf 'مقدار موجود %s بدون تغییر نگه داشته شد.\n' "$key"
  fi
}

prompt_value DOMAIN 'دامنهٔ اصلی' 'donysh.ir'
prompt_value FINANCE_DOMAIN 'دامنهٔ سرویس مالی' "finance.${desired[DOMAIN]}"
prompt_value AUTH_DOMAIN 'دامنهٔ سرویس ورود مرکزی' "auth.${desired[DOMAIN]}"
prompt_value LIFE_DOMAIN 'دامنهٔ دونیش زندگی' "life.${desired[DOMAIN]}"
prompt_value ACME_EMAIL 'ایمیل دریافت هشدارهای گواهی HTTPS' "admin@${desired[DOMAIN]}"
prompt_value APP_PORT 'درگاه داخلی برنامه' '8080'
prompt_value TZ 'منطقهٔ زمانی' 'Asia/Tehran'
prompt_value POSTGRES_DB 'نام پایگاه دادهٔ مالی' 'hesabyar'
prompt_value POSTGRES_USER 'کاربر پایگاه دادهٔ مالی' 'hesabyar'
prompt_value AUTH_POSTGRES_DB 'نام پایگاه دادهٔ ورود مرکزی' 'donysh_auth'
prompt_value AUTH_POSTGRES_USER 'کاربر پایگاه دادهٔ ورود مرکزی' 'donysh_auth'
prompt_value LIFE_POSTGRES_DB 'نام پایگاه دادهٔ دونیش زندگی' 'donysh_life'
prompt_value LIFE_POSTGRES_USER 'کاربر پایگاه دادهٔ دونیش زندگی' 'donysh_life'

[[ "${desired[DOMAIN]}" =~ ^([A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,63}$ ]] \
  || fail "دامنهٔ اصلی معتبر نیست."
[[ "${desired[FINANCE_DOMAIN]}" =~ ^([A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,63}$ ]] \
  || fail "دامنهٔ سرویس مالی معتبر نیست."
[[ "${desired[AUTH_DOMAIN]}" =~ ^([A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,63}$ ]] \
  || fail "دامنهٔ سرویس ورود معتبر نیست."
[[ "${desired[LIFE_DOMAIN]}" =~ ^([A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,63}$ ]] \
  || fail "دامنهٔ دونیش زندگی معتبر نیست."
[[ "${desired[ACME_EMAIL]}" =~ ^[^[:space:]@]+@[^[:space:]@]+\.[^[:space:]@]+$ ]] \
  || fail "ایمیل گواهی HTTPS معتبر نیست."
[[ "${desired[APP_PORT]}" =~ ^[0-9]+$ ]] \
  && (( desired[APP_PORT] >= 1 && desired[APP_PORT] <= 65535 )) \
  || fail "درگاه برنامه باید عددی بین ۱ و ۶۵۵۳۵ باشد."
[[ "${desired[TZ]}" =~ ^[A-Za-z_+-]+(/[A-Za-z0-9_+-]+)+$ ]] \
  || fail "منطقهٔ زمانی معتبر نیست."

for key in POSTGRES_DB POSTGRES_USER AUTH_POSTGRES_DB AUTH_POSTGRES_USER LIFE_POSTGRES_DB LIFE_POSTGRES_USER; do
  [[ "${desired[$key]}" =~ ^[A-Za-z_][A-Za-z0-9_]{0,62}$ ]] \
    || fail "مقدار $key برای PostgreSQL معتبر نیست."
done

keep_or_generate_secret POSTGRES_PASSWORD
keep_or_generate_secret AUTH_POSTGRES_PASSWORD
keep_or_generate_secret AUTH_SIGNING_CERT_PASSWORD
keep_or_generate_secret AUTH_ENCRYPTION_CERT_PASSWORD
keep_or_generate_secret FINANCE_OIDC_CLIENT_SECRET
keep_or_generate_secret CROSS_APP_LOGOUT_SECRET
keep_or_generate_secret LIFE_POSTGRES_PASSWORD
keep_or_generate_secret LIFE_OIDC_CLIENT_SECRET
[[ "${#desired[FINANCE_OIDC_CLIENT_SECRET]}" -ge 32 ]] \
  || fail "مقدار FINANCE_OIDC_CLIENT_SECRET باید دست‌کم ۳۲ نویسه باشد."
[[ "${#desired[CROSS_APP_LOGOUT_SECRET]}" -ge 32 ]] \
  || fail "مقدار CROSS_APP_LOGOUT_SECRET باید دست‌کم ۳۲ نویسه باشد."
[[ "${#desired[LIFE_OIDC_CLIENT_SECRET]}" -ge 32 ]] \
  || fail "مقدار LIFE_OIDC_CLIENT_SECRET باید دست‌کم ۳۲ نویسه باشد."

desired[LEGACY_IDENTITY_IMPORT_COMPLETED]="$(existing_or_default LEGACY_IDENTITY_IMPORT_COMPLETED false)"
desired[CENTRAL_AUTH_ENABLED]="$(existing_or_default CENTRAL_AUTH_ENABLED false)"
for key in LEGACY_IDENTITY_IMPORT_COMPLETED CENTRAL_AUTH_ENABLED; do
  [[ "${desired[$key]}" == "true" || "${desired[$key]}" == "false" ]] \
    || fail "مقدار $key فقط می‌تواند true یا false باشد."
done

temp_file="$(mktemp "$env_dir/.env.donysh.XXXXXX")"
cleanup() {
  rm -f -- "$temp_file"
}
trap cleanup EXIT HUP INT TERM

declare -A written=()
if [[ -f "$env_file" ]]; then
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"
    if [[ "$line" =~ ^([A-Z][A-Z0-9_]*)=(.*)$ ]] && [[ -n "${managed[${BASH_REMATCH[1]}]:-}" ]]; then
      key="${BASH_REMATCH[1]}"
      if [[ -z "${written[$key]:-}" ]]; then
        printf '%s=%s\n' "$key" "${desired[$key]}" >> "$temp_file"
        written["$key"]=1
      fi
    else
      printf '%s\n' "$line" >> "$temp_file"
    fi
  done < "$env_file"
fi

for key in "${managed_keys[@]}"; do
  if [[ -z "${written[$key]:-}" ]]; then
    printf '%s=%s\n' "$key" "${desired[$key]}" >> "$temp_file"
  fi
done

chmod 600 "$temp_file"

if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1 && [[ -f "$compose_file" ]]; then
  if ! docker compose --env-file "$temp_file" -f "$compose_file" config --quiet >/dev/null; then
    fail "اعتبارسنجی Docker Compose ناموفق بود؛ فایل فعلی دست‌نخورده باقی ماند."
  fi
fi

original_uid=''
original_gid=''
if [[ -f "$env_file" ]]; then
  original_uid="$(stat -c '%u' "$env_file")"
  original_gid="$(stat -c '%g' "$env_file")"
fi

mv -f -- "$temp_file" "$env_file"
trap - EXIT HUP INT TERM
chmod 600 "$env_file"

if [[ "$(id -u)" -eq 0 ]]; then
  target_uid="${original_uid:-${SUDO_UID:-0}}"
  target_gid="${original_gid:-${SUDO_GID:-0}}"
  chown "$target_uid:$target_gid" "$env_file"
fi

printf '\nفایل %s با موفقیت و سطح دسترسی 600 به‌روزرسانی شد.\n' "$env_file"
printf 'هیچ رمز یا مقدار حساسی روی خروجی نمایش داده نشد.\n'
printf 'رمزهای موجود حفظ شدند؛ برای تعویض رمز دیتابیس باید خود PostgreSQL نیز هم‌زمان به‌روزرسانی شود.\n'
