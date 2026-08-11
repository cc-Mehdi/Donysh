# دنیش / Donysh — نسخه Production و CI/CD

سامانه مدیریت مالی شخصی و مشترک با ASP.NET Core Razor Pages، PostgreSQL، Tailwind CSS، Docker و Caddy.

## قابلیت‌ها

- ثبت‌نام و ورود با ماندگاری ۳۰روزه گزینه «مرا به خاطر بسپار»
- جداسازی کامل اطلاعات کاربران
- فضای مالی شخصی و مشترک
- ثبت و مدیریت مخارج و دسته‌بندی‌ها
- مبلغ‌های ورودی با جداسازی سه‌رقمی
- ورود و نمایش تاریخ به‌صورت شمسی مانند `۱۴۰۵/۰۵/۰۸`
- بودجه‌بندی ماهانه و هشدار عبور از سقف
- هدف‌های پس‌انداز شخصی و مشترک
- گزارش روزانه، هفتگی، ماهانه و سالانه
- راهنمای شروع داخل برنامه برای کاربران جدید
- HTTPS خودکار با Caddy
- CI/CD خودکار پس از Push روی شاخه `main`

## اجرای فعلی با دامنه

فایل `.env` سرور باید شامل دامنه، ایمیل ACME و رمز PostgreSQL باشد. سپس:

```bash
docker compose --env-file .env -f docker-compose.domain.yml up -d --build
```

وضعیت:

```bash
docker compose --env-file .env -f docker-compose.domain.yml ps
```

لاگ‌ها:

```bash
docker compose --env-file .env -f docker-compose.domain.yml logs -f --tail=200
```

## CI/CD

راهنمای کامل تنظیم Repository جدید `donysh`، Deploy Key، GitHub Actions Secrets و آماده‌سازی سرور در فایل زیر است:

```text
CI-CD.md
```

Workflow آماده در مسیر زیر قرار دارد:

```text
.github/workflows/deploy.yml
```

اسکریپت استقرار سرور:

```text
scripts/server-deploy.sh
```

این اسکریپت اطلاعات Docker Volumeها را حذف نمی‌کند و پس از هر استقرار مسیر `/health` را بررسی می‌کند.

## نکته تاریخ شمسی

کاربر تاریخ را شمسی وارد و شمسی مشاهده می‌کند. در PostgreSQL نوع `date` یک تاریخ مطلق و مستقل از تقویم است؛ برنامه تاریخ شمسی واردشده را به همان روز متناظر تبدیل می‌کند و هنگام نمایش دوباره به شمسی برمی‌گرداند. به این ترتیب مرتب‌سازی، فیلتر و گزارش‌گیری دقیق باقی می‌ماند.

## نکته مهم داده‌ها

برای توقف یا به‌روزرسانی عادی از `-v` استفاده نکنید:

```bash
docker compose --env-file .env -f docker-compose.domain.yml down
```

دستور زیر Volumeهای دیتابیس را حذف می‌کند و خطرناک است:

```bash
docker compose --env-file .env -f docker-compose.domain.yml down -v
```

## فونت IRANYekanX Pro

فایل فونت تجاری داخل این بسته بازتوزیع نشده است. اسکریپت نصب فایل مجاز شما در پوشه `scripts` موجود است..
