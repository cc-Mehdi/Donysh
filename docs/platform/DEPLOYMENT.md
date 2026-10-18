# استقرار مرحلهٔ ۱ سرویس Auth

## آنچه خودکار نیست

این تغییر DNS، Secret، گواهی عمومی، Push، Deploy یا Migration Production را انجام نمی‌دهد. فایل Compose فعلی Finance تغییر نکرده و deploy معمول فعلی همچنان فقط همان برنامه را اجرا می‌کند.

## پیش‌نیاز سرور

- رکورد DNS نوع A/AAAA برای `auth.donysh.ir` به سرور فعلی.
- Docker Compose و OpenSSL.
- Backup معتبر volume دیتابیس Finance قبل از نخستین جابه‌جایی فایل Compose.
- پورت‌های ۸۰ و ۴۴۳ در اختیار Caddy.

## آماده‌سازی

```bash
cd /home/ubuntu/donysh
cp .env.example .env.auth.example
# مقادیر AUTH_* را در فایل .env موجود، با Secretهای تصادفی و واقعی اضافه کنید.
set -a
. ./.env
set +a
bash scripts/generate-auth-certificates.sh
docker compose --env-file .env -f docker-compose.platform.yml config >/dev/null
```

فایل‌های PFX در `secrets/auth` ساخته می‌شوند، در Git نادیده گرفته شده‌اند و باید در backup رمزگذاری‌شده نگهداری شوند. رمز گواهی‌ها باید در Secret manager یا `.env` با دسترسی محدود باشد.

## استقرار محدود مرحلهٔ ۱

```bash
docker compose --env-file .env -f docker-compose.platform.yml build auth
docker compose --env-file .env -f docker-compose.platform.yml up -d auth-db auth
docker compose --env-file .env -f docker-compose.platform.yml up -d caddy
docker compose --env-file .env -f docker-compose.platform.yml ps
curl -fsS https://auth.donysh.ir/health/live
curl -fsS https://auth.donysh.ir/health/ready
curl -fsS https://auth.donysh.ir/.well-known/openid-configuration
```

در اولین شروع، Migration فقط روی `auth-db` اجرا می‌شود. `Features__*` را false نگه دارید. صفحهٔ ورود قابل مشاهده است، اما ورود کاربران Finance هنوز کار نمی‌کند.

## مشاهدهٔ Log

```bash
docker compose --env-file .env -f docker-compose.platform.yml logs --tail=200 auth auth-db caddy
```

Log نباید شامل Secret، password، Token یا connection string کامل باشد.

## Rollback امن

```bash
docker compose --env-file .env -f docker-compose.platform.yml stop auth
docker compose --env-file .env -f docker-compose.domain.yml up -d caddy web db
```

volumeهای `auth_postgres_data` و `auth_dataprotection_keys` را حذف نکنید. برگشت Caddy به Compose فعلی Finance را روی `donysh.ir` نگه می‌دارد. چون Finance هنوز به Auth متصل نیست، سرویس مالی مستقل ادامه می‌دهد.

## اجرای محلی توسعه

یک PostgreSQL محلی مطابق `appsettings.Development.json` فراهم کنید، سپس:

```bash
cd Donysh.Auth
npm ci --ignore-scripts --no-audit --no-fund
npm run build:css
cd ..
dotnet run --project Donysh.Auth
```

گواهی توسعهٔ OpenIddict فقط در Development/Testing استفاده می‌شود و برای Production پذیرفته نیست.
