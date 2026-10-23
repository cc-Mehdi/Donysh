# معماری احراز هویت دُنیش

## تصمیم فنی

سرویس `Donysh.Auth` از ASP.NET Core Identity برای مدیریت کاربر و OpenIddict برای OpenID Connect استفاده می‌کند. دلیل این انتخاب سازگاری Hashهای کاربران فعلی، هم‌خوانی با پشتهٔ .NET موجود، پشتیبانی استاندارد از Authorization Code + PKCE و امکان نگهداری مستقل داده‌های Auth است.

## Endpointها

- Discovery: `/.well-known/openid-configuration`
- Authorization: `/connect/authorize`
- Token: `/connect/token`
- UserInfo: `/connect/userinfo`
- Logout: `/connect/logout`
- Liveness: `/health/live`
- Readiness: `/health/ready`

## ثبت‌نام و بازیابی حساب

- ثبت‌نام فقط وقتی `AUTH_PUBLIC_REGISTRATION=true`، انتقال حساب‌های قدیمی کامل و SMTP پیکربندی شده باشد فعال است.
- کاربر تازه تا زمان تأیید مالکیت ایمیل اجازهٔ ورود ندارد و پیوند تأیید ۲۴ ساعت اعتبار دارد.
- صفحهٔ ارسال دوبارهٔ تأیید، وجود یا نبود حساب را افشا نمی‌کند.
- ساخت حساب و ارسال دوبارهٔ تأیید، محدودیت مستقل پنج درخواست در پانزده دقیقه برای هر نشانی شبکه دارند.
- بازیابی رمز نیز به `AUTH_PASSWORD_RECOVERY=true`، پایان انتقال و ایمیل عملیاتی نیاز دارد.
- پاسخ بازیابی رمز وجود حساب را افشا نمی‌کند.
- نخستین ورود کاربر تازه به دُنیش مالی، حساب محلی متناظر، فضای شخصی و دسته‌بندی‌های اولیه را در یک تراکنش و فقط یک بار می‌سازد.

متغیرهای لازم برای ایمیل در `.env`:

```text
AUTH_SMTP_HOST=smtp.example.com
AUTH_SMTP_PORT=587
AUTH_SMTP_USE_SSL=true
AUTH_SMTP_TIMEOUT_SECONDS=20
AUTH_SMTP_USERNAME=<smtp-user>
AUTH_SMTP_PASSWORD=<smtp-password>
AUTH_SMTP_FROM_ADDRESS=no-reply@example.com
AUTH_SMTP_FROM_NAME=Donysh
```

اسکریپت `scripts/configure-server-env.sh` این مقادیر را بدون نمایش رمز در خروجی نگهداری می‌کند.

پس از دریافت اطلاعات ارسال ایمیل از سرویس‌دهنده، روی سرور اجرا کنید:

```bash
cd /home/ubuntu/donysh
sudo bash scripts/configure-server-env.sh
sudo docker compose --env-file .env -f docker-compose.platform.yml up -d --build auth web
curl -fsS https://auth.donysh.ir/health/ready
```

فعال‌بودن ظاهری ثبت‌نام به‌تنهایی کافی نیست؛ تا زمانی که تنظیمات ایمیل معتبر نباشد، پیوند ساخت حساب نمایش داده نمی‌شود تا حساب تأییدنشده و غیرقابل‌بازیابی ساخته نشود.

## مدیریت حساب مرکزی

- ثبت‌نام تازه به نام، نام خانوادگی، شمارهٔ همراه، ایمیل، نام کاربری یکتا و رمز عبور نیاز دارد. ورود با ایمیل یا نام کاربری انجام می‌شود؛ حساب‌های قدیمی همچنان می‌توانند با ایمیل وارد شوند.
- کاربر واردشده در `/Account/Manage` می‌تواند مشخصات، نام کاربری و ترجیح اعلان‌های اختیاری حساب را ویرایش کند. تغییر ایمیل فقط پس از تأیید پیوند ارسال‌شده به نشانی تازه ثبت می‌شود. تغییر رمز، رمز فعلی را می‌خواهد.
- غیرفعال‌سازی و حذف با رمز عبور و تأیید جداگانه انجام می‌شوند و زنجیرهٔ خروج مرکزی، مالی و زندگی را آغاز می‌کنند. ورود دوباره با رمز صحیح حساب غیرفعال‌شده را فعال می‌کند.
- حذف، هویت مرکزی را حذف می‌کند و دسترسی آن شناسه را از بین می‌برد. داده‌های مشترک کاربران دیگر یا رکوردهای محلی محصولات به‌صورت خودکار پاک نمی‌شوند؛ حذف دادهٔ محصولات نیاز به گردش‌کار جداگانه دارد.

## کنترل‌های امنیتی

- Cookie سرویس HttpOnly، Secure در Production، SameSite=Lax و با نام مستقل است.
- ورود ناموفق موجب Lockout می‌شود و endpointهای حساب Rate Limit دارند.
- Return URL فقط مسیر محلی معتبر است و Open Redirect رد می‌شود.
- فرم‌های Razor از Antiforgery داخلی ASP.NET Core استفاده می‌کنند.
- CSP بدون `unsafe-eval`، منع frame، Referrer Policy و عدم Cache صفحات حساس فعال است.
- Redirect URIهای client دقیق، absolute و در Production فقط HTTPS هستند.
- Authorization code سه دقیقه و access/id token ده دقیقه عمر دارند؛ PKCE برای clientها الزامی است.
- گواهی‌های OIDC و Secretها از environment/mount خوانده می‌شوند و در Git قرار نمی‌گیرند.
- Logها شامل رمز، Secret، Token یا محتوای ایمیل نیستند.

## افزودن client در آینده

clientها از بخش `OidcClients` پیکربندی می‌شوند. نمونهٔ متغیرها:

```text
OidcClients__0__ClientId=finance-web
OidcClients__0__DisplayName=Donysh Finance
OidcClients__0__PublicClient=false
OidcClients__0__ClientSecret=<secret-manager-value>
OidcClients__0__RedirectUris__0=https://finance.donysh.ir/signin-oidc
OidcClients__0__PostLogoutRedirectUris__0=https://finance.donysh.ir/signout-callback-oidc
```

در دورهٔ انتقال مرحلهٔ ۳، هر دو Redirect دقیق زیر برای همان client ثبت می‌شوند:

```text
https://donysh.ir/signin-oidc
https://finance.donysh.ir/signin-oidc
```

هر دو Post-logout redirect متناظر نیز ثبت می‌شوند. Wildcard استفاده نمی‌شود و پس از انتقال کامل،
حذف آدرس قدیمی فقط در یک مرحلهٔ مستقل و پس از پایان دورهٔ سازگاری انجام خواهد شد.

## راهبرد کاربران قدیمی

قالب Hash در آزمون با `PasswordHasher` سرویس جدید سازگار است. راهبرد پیشنهادی مرحلهٔ ۲:

1. Backup و ساخت محیط آزمایشی از دیتابیس Finance.
2. کپی Identity users/roles/claims با حفظ دقیق `Id`، `PasswordHash`، `SecurityStamp` و وضعیت Lockout.
3. جلوگیری از تغییر هم‌زمان حساب در پنجرهٔ انتقال یا همگام‌سازی کنترل‌شدهٔ delta.
4. آزمون ورود با Hash واقعی نمونه‌ها بدون دانستن رمز خام.
5. اتصال Finance به subject ثابت و نگه‌داشتن رابطهٔ داده‌های مالی با همان ID.

در نصب‌های قدیمی، تا اجرای انتقال حساب‌ها مقدار `LEGACY_IDENTITY_IMPORT_COMPLETED=false` باقی می‌ماند و ثبت‌نام فعال نمی‌شود.
