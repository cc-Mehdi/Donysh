# برنامهٔ مهاجرت Finance و کاربران

این سند دستور اجرای کنترل‌شدهٔ مرحلهٔ ۲ است. هیچ‌یک از فرمان‌های `--apply` یا فعال‌سازی زیر به‌صورت خودکار در Production اجرا نمی‌شوند.

## پیش‌نیازها

- Backup قابل بازیابی از دیتابیس Finance و ثبت تعداد کاربران/عضویت‌ها.
- اجرای rehearsal روی clone دیتابیس، نه Production.
- تعیین پنجرهٔ کوتاه read-only یا مکانیزم delta برای تغییرهای Identity.
- ثبت exact redirect URI و Secret در Secret manager.

## اصل حفظ هویت

اولویت با انتقال همان `AspNetUsers.Id` به Auth است. این شناسه در Finance به داده‌های مالی متصل است و تغییر آن بدون نگاشت پایدار مجاز نیست. PasswordHash خام دوباره تولید نمی‌شود و رمز هیچ کاربری درخواست یا ثبت نخواهد شد.

## مراحل پیشنهادی

1. از هر دو پایگاه‌داده نسخهٔ پشتیبان قابل بازیابی بگیرید.
2. بررسی بدون نوشتن را اجرا کنید:

   ```bash
   cd /home/ubuntu/donysh
   bash scripts/migrate-identity-to-auth.sh --dry-run
   ```

3. اگر بررسی بدون برخورد شناسه یا ایمیل تمام شد، انتقال اتمی و تکرارپذیر را اجرا کنید:

   ```bash
   bash scripts/migrate-identity-to-auth.sh --apply
   bash scripts/migrate-identity-to-auth.sh --dry-run
   ```

   اجرای دوم باید تعداد کاربران آمادهٔ افزودن را صفر و کاربران یکسان را برابر تعداد کاربران Finance نشان دهد. ابزار فقط شمارنده‌ها را نمایش می‌دهد و ایمیل، هش رمز یا رشتهٔ اتصال را ثبت نمی‌کند.

4. مقدار `LEGACY_IDENTITY_IMPORT_COMPLETED=true` را در `.env` قرار دهید و فقط سرویس Auth را بازسازی کنید:

   ```bash
   sed -i 's/^LEGACY_IDENTITY_IMPORT_COMPLETED=.*/LEGACY_IDENTITY_IMPORT_COMPLETED=true/' .env
   docker compose --env-file .env -f docker-compose.platform.yml up -d auth
   ```

5. ورود یک حساب کنترل‌شده را در `auth.donysh.ir` آزمایش کنید. سپس ورود مرکزی Finance را فعال کنید:

   ```bash
   sed -i 's/^CENTRAL_AUTH_ENABLED=.*/CENTRAL_AUTH_ENABLED=true/' .env
   docker compose --env-file .env -f docker-compose.platform.yml up -d web
   ```

6. ورود، خروج، دسترسی همان کاربر به فضاهای قبلی و عملیات خواندن/نوشتن مالی را بررسی کنید. شناسهٔ `sub` صادرشده باید دقیقاً در `AspNetUsers` مالی موجود باشد؛ در غیر این صورت ورود Finance رد می‌شود.

   پس از فعال‌سازی، Finance از نام کوکی نشست جدید استفاده می‌کند تا نشست‌های قدیمی نتوانند مسیر ورود مرکزی را دور بزنند. خاموش‌کردن پرچم، کوکی قدیمی را دوباره برای بازگشت اضطراری قابل استفاده می‌کند؛ هیچ کوکی میان دامنهٔ اصلی و دامنهٔ Auth مشترک نیست.

   پاسخ ورود از حالت OIDC `query` استفاده می‌کند تا سیاست امنیت محتوا بدون `unsafe-inline` باقی بماند. Authorization Code کوتاه‌عمر است و همچنان با PKCE، `state` و `nonce` محافظت می‌شود.

7. ثبت‌نام محلی Finance هنگام فعال بودن ورود مرکزی بسته است. بازیابی رمز و ثبت‌نام مرکزی تا مرحلهٔ جداگانه و آماده‌شدن ارسال ایمیل غیرفعال می‌مانند.

## Rollback

- تا پایان دورهٔ گذار، Schema و کاربران Identity موجود Finance حذف نمی‌شوند.
- در صورت شکست، فرمان زیر ورود قبلی را بدون حذف داده بازمی‌گرداند:

  ```bash
  sed -i 's/^CENTRAL_AUTH_ENABLED=.*/CENTRAL_AUTH_ENABLED=false/' .env
  docker compose --env-file .env -f docker-compose.platform.yml up -d web
  ```
- دیتابیس Auth و گزارش Import حفظ می‌شوند تا علت بررسی شود؛ پاک‌کردن خودکار انجام نمی‌شود.
- هر Migration دیتابیس Auth با backup همان دیتابیس و فرمان `dotnet ef database update <PreviousMigration>` بازگردانده می‌شود.

## معیار توقف

هر اختلاف در User ID، ایمیل تکراری حل‌نشده، Hash ناسازگار یا رابطهٔ مالی orphaned باعث توقف مهاجرت می‌شود و اجازهٔ نوشتن روی Production داده نمی‌شود.

## مرحلهٔ ۳: انتقال موازی دامنهٔ Finance

این مرحله انتقال داده نیست. `finance.donysh.ir` و `donysh.ir` موقتاً به همان سرویس، همان
Data Protection volume و همان PostgreSQL متصل می‌شوند. ترتیب اجرا:

1. از volume مالی backup قابل بازیابی بگیرید و DNS دامنهٔ جدید را به IP سرور متصل کنید.
2. با `configure-server-env.sh` مقدار `FINANCE_DOMAIN=finance.donysh.ir` را اضافه کنید.
3. Compose را اعتبارسنجی و سرویس‌ها را مستقر کنید. Auth هنگام شروع، Redirectهای دقیق هر دو دامنه را روی client موجود به‌روزرسانی می‌کند.
4. health، ورود، خروج، فایل‌های ایستا، فضاها، مخارج، بودجه‌ها، پس‌اندازها، اقساط و گزارش‌ها را روی دامنهٔ جدید بررسی کنید.
5. `donysh.ir` را تا پایان دورهٔ سازگاری و تأیید صریح مرحلهٔ Landing نگه دارید. برنامهٔ همراه فعلی نیز در این مدت می‌تواند از آدرس قدیمی استفاده کند.

نشست Finance میان دو میزبان به اشتراک گذاشته نمی‌شود. ورود نخست به دامنهٔ جدید یک نشست host-only
جدید می‌سازد، ولی نشست مرکزی `auth.donysh.ir` باعث می‌شود کاربر معمولاً رمز را دوباره وارد نکند.
این جداسازی از گسترش بی‌دلیل Cookie میان زیردامنه‌ها جلوگیری می‌کند.

بازگشت این مرحله فقط با بازگرداندن نسخهٔ قبلی Compose/Caddy و در صورت نیاز حذف DNS دامنهٔ جدید
انجام می‌شود؛ دیتابیس، کاربران، volumeها و دامنهٔ قدیم نباید حذف شوند.
