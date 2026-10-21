# مسیر خروج مشترک دُنیش

## قرارداد فعلی

هر محصول نشست محلی و Cookie مستقل دارد. خروج با POST همان محصول آغاز می‌شود؛ فرم باید `data-native-submit` و `data-logout-form` داشته باشد تا پیمایش عادی مرورگر انجام شود و بررسی نشست در میانهٔ خروج آن را قطع نکند. ترتیب ادامه‌ها با proof کوتاه‌عمر و امضاشده کنترل می‌شود:

| شروع خروج | زنجیره |
| --- | --- |
| Finance | Finance → Life → Finance (finalize) → Auth → صفحهٔ خروج کامل |
| Life | Life → Finance → Life (finalize) → Auth → صفحهٔ خروج کامل |
| Auth | Auth → Finance → Life → Auth (finalize) → صفحهٔ خروج کامل |

هر endpoint فقط proof مربوط به مخاطب خود را قبول می‌کند. تغییر ترتیب، نشانی endpoint یا پاسخ‌های redirect باید در هر سه سرویس و آزمون‌هایشان هم‌زمان بررسی شود. بازگشت به صفحهٔ محصول پس از خروج نباید با نشست قبلی معتبر باشد.

## CSP و رابط

`form-action` روی صفحهٔ آغازکننده باید مبدأ خود صفحه، Auth و محصول مقابل را مجاز بداند. در مرورگر، redirectهای ادامهٔ فرم هم می‌توانند با همین سیاست سنجیده شوند؛ محدودکردن آن به `'self'` و Auth باعث می‌شد POST خروج محلی در Finance و Life با خطای CSP متوقف شود. فقط مبدأهای HTTPS پیکربندی‌شده را اضافه کنید؛ wildcard یا حذف `form-action` راه‌حل نیست. Auth نیز مبدأ محصول‌های ثبت‌شده را مجاز می‌داند. Caddy در پیکربندی چندسرویسی نباید هدر CSP محصول را بازنویسی کند.

در Life دکمهٔ خروج هم در نوار کناری دسکتاپ و هم در نوار پایین موبایل وجود دارد؛ هر دو همان POST محلی را می‌فرستند. هنگام تغییر layout یا JavaScript همگام‌سازی نشست، هر دو فرم و پیمایش عادی را بررسی کنید.

## آزمون قبل از انتشار

```powershell
dotnet run --project Donysh.Api.Tests --configuration Release
dotnet run --project Donysh.Life.Tests --configuration Release
dotnet run --project Donysh.Auth.Tests --configuration Release
dotnet build HesabYar.sln --configuration Release
```

پس از انتشار، در مرورگر دسکتاپ و موبایل، خروج را یک بار از Finance و یک بار از Life آغاز کنید. نبود خطای CSP، رسیدن به صفحهٔ «خروج کامل» و بی‌اعتبارشدن نشست هر دو محصول و Auth را بررسی کنید. بازگشت مرورگر به تب قدیمی نباید حساب را فعال نشان دهد.
