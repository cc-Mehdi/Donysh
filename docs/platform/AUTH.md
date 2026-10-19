# معماری احراز هویت دونیش

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

## وضعیت قابلیت‌ها در مرحلهٔ ۱

- صفحهٔ ورود فعال است، اما کاربر Production ندارد و صریحاً اعلام می‌کند حساب‌های Finance هنوز منتقل نشده‌اند.
- ثبت‌نام فقط وقتی `Features__PublicRegistration=true` و `Features__LegacyIdentityImportCompleted=true` و ایمیل پیکربندی شده باشد کار می‌کند.
- بازیابی رمز نیز به `Features__PasswordRecovery=true`، پایان Import و ایمیل عملیاتی نیاز دارد.
- پاسخ بازیابی رمز وجود حساب را افشا نمی‌کند.

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

تا اجرای این برنامه، ثبت‌نام عمومی بسته می‌ماند و هیچ داده‌ای از Finance کپی نشده است.
