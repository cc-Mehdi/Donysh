$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Test-Path ".env")) {
    throw "ابتدا deploy.ps1 را یک بار اجرا کنید تا فایل .env ساخته شود."
}

$envText = Get-Content ".env" -Raw
if ($envText -match "DOMAIN=example\.com" -or $envText -match "ACME_EMAIL=admin@example\.com") {
    throw "مقادیر DOMAIN و ACME_EMAIL را داخل فایل .env با دامنه و ایمیل واقعی جایگزین کنید."
}

docker compose --env-file .env -f docker-compose.domain.yml up -d --build
if ($LASTEXITCODE -ne 0) { throw "اجرای حالت دامنه ناموفق بود." }

docker compose --env-file .env -f docker-compose.domain.yml ps
