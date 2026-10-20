# استقرار سرویس ورود مرکزی

## انتشار فایل نصب اندروید

نشانی عمومی نسخهٔ جاری برنامه ثابت است:

```text
https://donysh.ir/downloads/donysh-mobile.apk
```

فایل روی سرور در مسیر زیر نگهداری می‌شود و داخل مخزن گیت قرار نمی‌گیرد:

```text
/home/ubuntu/donysh/artifacts/mobile/donysh-mobile.apk
```

برای انتشار نسخهٔ جدید، ابتدا فایل خروجی را با نام دلخواه روی سرور بارگذاری کنید و سپس از ریشهٔ پروژه فرمان زیر را اجرا کنید:

```bash
bash scripts/publish-mobile-apk.sh /مسیر/فایل/نسخه-جدید.apk
```

اسکریپت، فایل عادی و امضای اولیهٔ قالب APK، اندازهٔ مجاز و سطح دسترسی را بررسی می‌کند و فایل را به‌صورت اتمی جایگزین می‌کند. پوشه فقط‌خواندنی داخل Caddy متصل شده، فهرست فایل‌های آن منتشر نمی‌شود و برای تعویض نسخه نیازی به راه‌اندازی دوبارهٔ سرویس نیست. پس از نخستین استقرار این قابلیت، پاسخ را بررسی کنید:

```bash
curl -fsSI https://donysh.ir/downloads/donysh-mobile.apk
```

## آنچه خودکار نیست

اسکریپت استقرار از `docker-compose.platform.yml` استفاده می‌کند و هر دو سرویس مالی و ورود را می‌سازد. انتقال کاربران و فعال‌سازی ورود مرکزی عمداً خودکار نیست تا امکان بررسی و بازگشت امن وجود داشته باشد.

## پیش‌نیاز سرور

- رکورد DNS نوع A/AAAA برای `auth.donysh.ir` به سرور فعلی.
- پیش از استقرار مرحلهٔ ۳، رکورد DNS نوع A برای `finance.donysh.ir` به همان IP سرور.
- Docker Compose و OpenSSL.
- Backup معتبر volume دیتابیس Finance قبل از نخستین جابه‌جایی فایل Compose.
- پورت‌های ۸۰ و ۴۴۳ در اختیار Caddy.

## آماده‌سازی

```bash
cd /home/ubuntu/donysh
chmod +x scripts/configure-server-env.sh
bash scripts/configure-server-env.sh
sudo bash -c '
AUTH_SIGNING_CERT_PASSWORD="$(sed -n "s/^AUTH_SIGNING_CERT_PASSWORD=//p" .env)" \
AUTH_ENCRYPTION_CERT_PASSWORD="$(sed -n "s/^AUTH_ENCRYPTION_CERT_PASSWORD=//p" .env)" \
  bash scripts/generate-auth-certificates.sh
'
docker compose --env-file .env -f docker-compose.platform.yml config >/dev/null
```

اسکریپت `configure-server-env.sh` دامنه‌ها و ایمیل گواهی را می‌پرسد، Secretهای خالی یا نمونه را
روی خود سرور تولید می‌کند و فایل را با سطح دسترسی `600` به‌شکل اتمی می‌نویسد. رمزهای معتبر موجود
بدون تغییر حفظ می‌شوند تا اتصال volume فعلی PostgreSQL قطع نشود. مقدارهای حساس در خروجی نمایش داده
نمی‌شوند و فایل پیوند نمادین نیز پذیرفته نمی‌شود.
در مرحلهٔ ۳ مقدار `FINANCE_DOMAIN` را برابر `finance.donysh.ir` قرار دهید. مقدار `DOMAIN=donysh.ir`
عمداً بدون تغییر می‌ماند تا سرویس مالی روی هر دو دامنه به‌صورت موازی در دسترس باشد.
فایل `.env` عمداً با `source` اجرا نمی‌شود؛ فقط دو رمز لازم برای ساخت گواهی‌ها به‌عنوان داده خوانده
می‌شوند تا هیچ محتوایی از فایل به‌عنوان دستور پوسته اجرا نشود.

فایل‌های PFX در `secrets/auth` ساخته می‌شوند، در Git نادیده گرفته شده‌اند و باید در backup رمزگذاری‌شده نگهداری شوند. رمز گواهی‌ها باید در Secret manager یا `.env` با دسترسی محدود باشد.

## استقرار سرویس‌ها با ورود مرکزی غیرفعال

```bash
docker compose --env-file .env -f docker-compose.platform.yml build auth
docker compose --env-file .env -f docker-compose.platform.yml up -d auth-db auth
docker compose --env-file .env -f docker-compose.platform.yml up -d caddy
docker compose --env-file .env -f docker-compose.platform.yml ps
curl -fsS https://auth.donysh.ir/health/live
curl -fsS https://auth.donysh.ir/health/ready
curl -fsS https://auth.donysh.ir/.well-known/openid-configuration
```

## استقرار کنترل‌شدهٔ دامنهٔ Finance

ابتدا انتشار DNS را بررسی کنید؛ هر دو پاسخ باید IP همین سرور باشند:

```bash
getent ahostsv4 donysh.ir | head -n 1
getent ahostsv4 finance.donysh.ir | head -n 1
```

سپس مقدار جدید را بدون نمایش Secretها ایجاد و پیکربندی را بررسی کنید:

```bash
cd /home/ubuntu/donysh
sudo bash scripts/configure-server-env.sh </dev/null
sudo grep -E '^(DOMAIN|FINANCE_DOMAIN|AUTH_DOMAIN)=' .env
sudo docker compose --env-file .env -f docker-compose.platform.yml config --quiet
```

بعد از استقرار، سلامت و مسیر ورود هر دو دامنه را بررسی کنید:

```bash
curl -fsS https://donysh.ir/health
curl -fsS https://finance.donysh.ir/health
curl -sS -D - -o /dev/null https://finance.donysh.ir/Account/Login | grep -iE '^(HTTP|location:)'
```

در مرورگر ناشناس، ورود و خروج و سپس ایجاد، ویرایش و حذف یک دادهٔ آزمایشی کنترل‌شده را روی
دامنهٔ جدید بررسی کنید. گزارش‌ها و فایل‌های ایستا نیز باید بدون خطای شبکه بارگذاری شوند. دامنهٔ
قدیم در این مرحله Redirect نمی‌شود.

## استقرار لندینگ روی دامنهٔ اصلی

پیش از استقرار مرحلهٔ ۴ مطمئن شوید `finance.donysh.ir` در مرورگر و Health Check پایدار است. سپس
Compose باید سرویس‌های `landing`، `web` و `auth` را هم‌زمان نشان دهد:

```bash
cd /home/ubuntu/donysh
sudo docker compose --env-file .env -f docker-compose.platform.yml config --quiet
sudo docker compose --env-file .env -f docker-compose.platform.yml ps
```

پس از استقرار:

```bash
curl -fsS https://donysh.ir/health
curl -fsS https://finance.donysh.ir/health
curl -fsS https://auth.donysh.ir/health/ready
curl -sS -D - -o /dev/null https://donysh.ir/Expenses | grep -iE '^(HTTP|location:)'
```

فرمان آخر باید Redirect دائمی به `https://finance.donysh.ir/Expenses` بدهد. مسیرهای
`/api/mobile/*` روی دامنهٔ اصلی عمداً در دورهٔ سازگاری به Finance فرستاده می‌شوند تا نسخه‌های
نصب‌شدهٔ برنامهٔ همراه ناگهان قطع نشوند. Callbackهای ورود و درخواست‌های غیر GET فرم‌های بازشده
پیش از Deploy نیز مستقیم به Finance می‌رسند.

در اولین شروع، Migration فقط روی `auth-db` اجرا می‌شود. تا قبل از اجرای راهنمای `MIGRATION.md`، مقدارهای `CENTRAL_AUTH_ENABLED` و `LEGACY_IDENTITY_IMPORT_COMPLETED` را `false` نگه دارید. برنامهٔ مالی در این وضعیت از ورود قبلی استفاده می‌کند.

## مشاهدهٔ Log

```bash
docker compose --env-file .env -f docker-compose.platform.yml logs --tail=200 auth auth-db caddy
```

Log نباید شامل Secret، password، Token یا connection string کامل باشد.

## Rollback امن

```bash
sed -i 's/^CENTRAL_AUTH_ENABLED=.*/CENTRAL_AUTH_ENABLED=false/' .env
docker compose --env-file .env -f docker-compose.platform.yml up -d web
```

volumeهای `auth_postgres_data` و `auth_dataprotection_keys` را حذف نکنید. با خاموش‌کردن پرچم، ورود محلی قبلی بدون حذف داده‌ها بازمی‌گردد.

اگر فقط دامنهٔ جدید مشکل داشت، رکورد DNS آن را حذف کنید و نسخهٔ قبلی کد را دوباره مستقر کنید؛
`donysh.ir` و دیتابیس مالی دست‌نخورده می‌مانند. هیچ volume یا جدول دیتابیسی را برای بازگشت مرحلهٔ ۳ حذف نکنید.

برای بازگشت مرحلهٔ ۴، کامیت قبلی را دوباره Deploy کنید تا Caddy دامنهٔ اصلی را مستقیماً به `web`
متصل کند. سرویس Landing هیچ volume یا دیتابیسی ندارد و Rollback نیازمند Migration نیست.

## استقرار زیرساخت دُنیش زندگی

پیش از Deploy، رکورد `A` برای `life.donysh.ir` را به همان IP سرور اشاره دهید. اسکریپت تنظیمات
در اولین اجرا رمز پایگاه‌داده و Secret ارتباط OIDC را روی خود سرور می‌سازد و آن‌ها را نمایش نمی‌دهد:

```bash
cd /home/ubuntu/donysh
sudo bash scripts/configure-server-env.sh </dev/null
sudo grep -E '^(LIFE_DOMAIN|LIFE_POSTGRES_DB|LIFE_POSTGRES_USER)=' .env
sudo docker compose --env-file .env -f docker-compose.platform.yml config --quiet
```

پس از Deploy، سرویس‌ها و ورود مرکزی را بررسی کنید:

```bash
curl -fsS https://life.donysh.ir/health/live
curl -fsS https://life.donysh.ir/health/ready
curl -sS -D - -o /dev/null https://life.donysh.ir/ | grep -iE '^(HTTP|location:)'
sudo docker compose --env-file .env -f docker-compose.platform.yml logs --tail=100 life life-db auth caddy
```

در هر استقرار، Migrationهای افزایشی Life روی دیتابیس مستقل `life-db` اجرا می‌شوند. زیرمرحله‌های ۶.۳ تا ۷.۲
Migrationهای غیرمخرب `AddScheduleOccurrenceOverrides`، `AddScheduleOccurrenceRecords` و
`AddManualActualEntriesAndMovedOverrides` و `AddProgressReportingIndex` را برای نگهداری تغییرات یک رخداد،
نتیجهٔ واقعی آن، ثبت دستی فعالیت، بازگردانی انتقال و اجرای سریع‌تر گزارش‌های هفتگی اضافه می‌کنند؛
هیچ جدول یا ستونی حذف نمی‌شود و دیتابیس‌های Finance و Auth تغییر نمی‌کنند. پیش از استقرار عملیاتی
از volume یا دیتابیس Life نسخهٔ پشتیبان بگیرید. برای Rollback، نسخهٔ قبلی کد را Deploy کنید یا سرویس‌های
`life` و `life-db` را متوقف کنید. volumeهای `life_postgres_data` و `life_dataprotection_keys` را
حذف نکنید تا بازگشت رو به جلو بدون ازدست‌رفتن داده ممکن باشد؛ Down کردن Migration فقط زمانی مجاز
است که دادهٔ استثناهای رخداد دیگر موردنیاز نباشد.

زیرمرحلهٔ ۸.۱ هیچ Migration یا متغیر محیطی تازه‌ای ندارد. پس از استقرار، علاوه بر سلامت سرویس، فشرده‌سازی
و کش فایل ایستا را نیز کنترل کنید:

```bash
curl -fsSI -H 'Accept-Encoding: gzip' https://life.donysh.ir/css/app.css \
  | grep -iE '^(HTTP|content-encoding:|cache-control:)'
curl -fsSI https://life.donysh.ir/Account/Login \
  | grep -iE '^(HTTP|content-security-policy:|cross-origin-resource-policy:|x-permitted-cross-domain-policies:)'
```

پاسخ فایل سبک باید `content-encoding: gzip` و `cache-control` هفت‌روزه داشته باشد. صفحهٔ ورود باید سیاست
منبع هم‌مبدأ و منع سیاست‌های میان‌دامنه‌ای قدیمی را برگرداند. بازگشت این زیرمرحله فقط با استقرار نسخهٔ قبلی
انجام می‌شود و به بازگردانی پایگاه‌داده یا تغییر فایل `.env` نیاز ندارد.

زیرمرحلهٔ ۸.۲ نیز Migration و تنظیم محیطی تازه ندارد. پیش از انتشار عمومی، چک‌لیست
[`LIFE-RELEASE.md`](LIFE-RELEASE.md) را اجرا و نتیجهٔ آزمون پذیرش را ثبت کنید. آماده‌شدن کد به‌تنهایی
به‌معنای تأیید آزمون کاربر واقعی نیست.

از زیرمرحلهٔ ۸.۳، `server-deploy.sh` پس از Health Check اولیه، اسکریپت
`scripts/verify-platform-release.sh` را اجرا می‌کند. این دروازه با اتصال مستقیم به Caddy روی سرور، وابستگی
به CDN را از تشخیص سلامت مبدأ جدا می‌کند و در صورت خرابی ورود مرکزی، هدر امنیتی، کش، فشرده‌سازی یا
کانتینر ضروری، استقرار را ناموفق و Rollback را فعال می‌کند. اجرای دستی آن هیچ مقدار حساس را چاپ نمی‌کند:

```bash
cd /home/ubuntu/donysh
sudo bash scripts/verify-platform-release.sh
```

## عیب‌یابی و بازیابی Deploy روی سرور کم‌حافظه

اسکریپت Deploy پیش از تغییر Git و دوباره پس از Build، سلامت `db`، `auth-db` و `life-db` را بررسی می‌کند.
یک دیتابیس ناسالم فقط یک‌بار Restart می‌شود؛ اگر بازیابی نشود، وضعیت، خروج به‌علت کمبود حافظه و پنج نتیجهٔ
آخر Health Check چاپ می‌شوند و Deploy پیش از اتلاف زمان بیشتر متوقف می‌شود. هیچ volume یا داده‌ای حذف نمی‌شود.

ساخت Docker فقط برای محصولاتی انجام می‌شود که فایل‌های مؤثرشان میان کامیت نصب‌شده و کامیت مقصد تغییر کرده‌اند.
ساخت‌ها با `COMPOSE_PARALLEL_LIMIT=1` اجرا می‌شوند تا روی سرور یک‌گیگابایتی چند Build سنگین هم‌زمان اجرا نشود.
همچنین GitHub Actions نسخهٔ جاری اسکریپت Deploy را از همان کامیت روی ورودی SSH اجرا می‌کند؛ در نتیجه اصلاح
خود اسکریپت برای اعمال‌شدن به یک Deploy موفق قبلی وابسته نیست.

پیش از استقرار، فضای آزاد محل برنامه و Docker بررسی می‌شود. اگر فضا از حد امن کمتر باشد، ابتدا Build Cache
بلااستفادهٔ قدیمی و Imageهای dangling پاک می‌شوند و فقط در صورت کافی‌نبودن این پاک‌سازی، همهٔ Build Cache
بلااستفاده حذف می‌شود. volumeها و داده‌های دیتابیس هرگز در این بازیابی خودکار حذف نمی‌شوند. پس از استقرار موفق
نیز Build Cache بلااستفادهٔ قدیمی‌تر از هفت روز پاک می‌شود؛ این زمان با `BUILD_CACHE_RETENTION_HOURS` قابل تنظیم است.

برای بررسی فوری دیتابیس Life:

```bash
cd /home/ubuntu/donysh
sudo docker compose --env-file .env -f docker-compose.platform.yml ps life-db
life_db_id="$(sudo docker compose --env-file .env -f docker-compose.platform.yml ps -q life-db)"
sudo docker inspect --format 'status={{.State.Status}} oom={{.State.OOMKilled}} exit={{.State.ExitCode}} health={{if .State.Health}}{{.State.Health.Status}}{{end}}' "$life_db_id"
sudo docker compose --env-file .env -f docker-compose.platform.yml logs --tail=100 life-db
df -h / /var/lib/docker
free -m
```

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
