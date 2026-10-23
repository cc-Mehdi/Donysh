# آیکون‌های دُنیش مالی

آیکون‌های رابط از بستهٔ کامل `lucide-static` (نسخهٔ قفل‌شده در `package-lock.json`) می‌آیند. بسته با `npm ci` به‌صورت محلی نصب می‌شود و هیچ آیکونی از CDN بارگذاری نمی‌شود.

برای تولید آیکون‌های مورد استفادهٔ سایت، `npm run build:icons` را اجرا کنید. این فرمان از SVGهای بسته یک sprite کوچک در `wwwroot/icons/lucide.svg` می‌سازد و مجوز ISC را کنار آن می‌گذارد. `npm run build:css` نیز همین فرمان را پیش از ساخت CSS اجرا می‌کند. برای افزودن آیکون، نام فایل SVG را در `scripts/build-icons.mjs` ثبت کنید و از `<use href="/icons/lucide.svg#icon-name">` در HTML استفاده کنید.

منبع: https://lucide.dev/ — مجوز: ISC (`wwwroot/icons/LUCIDE-LICENSE.txt`).
