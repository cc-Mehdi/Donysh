# راه‌اندازی CI/CD برای مخزن `donysh`

با این تنظیمات، هر Push روی شاخه `main` ابتدا Build و بررسی می‌شود و سپس GitHub Actions به سرور متصل می‌شود. اسکریپت سرور آخرین Commit را از GitHub دریافت می‌کند، Docker Image جدید را می‌سازد، سرویس‌ها را بدون حذف Volumeها به‌روزرسانی می‌کند و مسیر `/health` را کنترل می‌کند. در صورت شکست Build یا Health Check، نسخه قبلی دوباره اجرا می‌شود.

## ۱. ساخت مخزن GitHub

یک Repository با نام `donysh` بسازید و محتوای همین پوشه را روی شاخه `main` Push کنید.

```bash
git init
git add .
git commit -m "Initial production release"
git branch -M main
git remote add origin git@github.com:YOUR_GITHUB_USERNAME/donysh.git
git push -u origin main
```

## ۲. دسترسی خواندن Repository برای سرور

روی سرور اجرا کنید:

```bash
mkdir -p ~/.ssh
ssh-keygen -t ed25519 -C "donysh-server-read" -f ~/.ssh/donysh_github -N ""
cat ~/.ssh/donysh_github.pub
```

کلید عمومی نمایش‌داده‌شده را در GitHub وارد کنید:

`Repository → Settings → Deploy keys → Add deploy key`

عنوان را `Donysh production server` بگذارید و گزینه Write access را فعال نکنید.

سپس روی سرور فایل SSH config را بسازید:

```bash
cat >> ~/.ssh/config <<'CONFIG'
Host github.com
  HostName github.com
  User git
  IdentityFile ~/.ssh/donysh_github
  IdentitiesOnly yes
CONFIG
chmod 600 ~/.ssh/config
ssh-keyscan -H github.com >> ~/.ssh/known_hosts
ssh -T git@github.com
```

## ۳. انتقال استقرار فعلی به پوشه Git

پیشنهاد مسیر نهایی:

```text
/home/ubuntu/donysh
```

روی سرور:

```bash
cd /home/ubuntu
mv HesabYar-Deploy-Ready/.env /tmp/donysh.env

git clone git@github.com:YOUR_GITHUB_USERNAME/donysh.git donysh
mv /tmp/donysh.env /home/ubuntu/donysh/.env
chmod 600 /home/ubuntu/donysh/.env
chmod +x /home/ubuntu/donysh/scripts/server-deploy.sh

cd /home/ubuntu/donysh
docker compose --env-file .env -f docker-compose.domain.yml up -d --build
```

به دلیل وجود `name: hesabyar` در Compose، Volumeهای PostgreSQL، Caddy و Data Protection قبلی حفظ می‌شوند. از `down -v` استفاده نکنید.

## ۴. ساخت کلید اتصال GitHub Actions به سرور

روی سرور یک کلید اختصاصی بسازید:

```bash
ssh-keygen -t ed25519 -C "github-actions-donysh" -f ~/.ssh/github_actions_donysh -N ""
cat ~/.ssh/github_actions_donysh.pub >> ~/.ssh/authorized_keys
chmod 600 ~/.ssh/authorized_keys
cat ~/.ssh/github_actions_donysh
```

محتوای کامل کلید خصوصی، شامل خطوط BEGIN و END، برای Secret زیر استفاده می‌شود.

## ۵. تعریف Secrets در GitHub

مسیر:

`Repository → Settings → Secrets and variables → Actions → New repository secret`

این Secrets را بسازید:

| Secret | مقدار نمونه |
|---|---|
| `SERVER_HOST` | `185.226.119.80` |
| `SERVER_PORT` | `22` |
| `SERVER_USER` | `root` |
| `SERVER_APP_PATH` | `/home/ubuntu/donysh` |
| `SERVER_SSH_KEY` | کلید خصوصی مرحله قبل |

فایل `.env` و رمز PostgreSQL نباید داخل GitHub Commit شوند.

## ۶. تست CI/CD

یک تغییر کوچک Commit و Push کنید:

```bash
git add .
git commit -m "Test automatic deployment"
git push origin main
```

در GitHub وارد تب `Actions` شوید. Workflow با نام `CI and Deploy` باید مراحل Validate و Deploy را با موفقیت تمام کند.

اگر اجرای قبلی هنوز روی سرور در حال build یا health check باشد، اجرای بعدی تا ۱۵ دقیقه منتظر lock است و سپس خودکار ادامه می‌دهد. برای تغییر این زمان می‌توانید هنگام اجرای اسکریپت مقدار `DEPLOY_LOCK_WAIT_SECONDS` را بر حسب ثانیه تنظیم کنید؛ برای مثال:

```bash
DEPLOY_LOCK_WAIT_SECONDS=1200 DEPLOY_BRANCH=main bash scripts/server-deploy.sh
```

## دستورات بررسی روی سرور

```bash
cd /home/ubuntu/donysh
git log -1 --oneline
docker compose --env-file .env -f docker-compose.domain.yml ps
docker compose --env-file .env -f docker-compose.domain.yml logs --tail=100 web caddy
curl -fsS https://novyx.ir/health
```
