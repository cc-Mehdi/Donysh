$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker نصب نیست یا در PATH قرار ندارد."
}

try {
    docker compose version | Out-Null
} catch {
    throw "Docker Compose در دسترس نیست."
}

if (-not (Test-Path ".env")) {
    $bytes = New-Object byte[] 48
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($bytes)
    $rng.Dispose()
    $password = [Convert]::ToBase64String($bytes)

    $content = @"
POSTGRES_DB=hesabyar
POSTGRES_USER=hesabyar
POSTGRES_PASSWORD=$password
TZ=Asia/Tehran
APP_PORT=8080
DOMAIN=example.com
ACME_EMAIL=admin@example.com
"@

    [System.IO.File]::WriteAllText(
        (Join-Path $PSScriptRoot ".env"),
        $content,
        [System.Text.UTF8Encoding]::new($false)
    )

    Write-Host ".env با رمز امن ساخته شد." -ForegroundColor Green
}

Write-Host "در حال ساخت و اجرای حساب‌یار..." -ForegroundColor Cyan
docker compose --env-file .env up -d --build
if ($LASTEXITCODE -ne 0) { throw "اجرای Docker Compose ناموفق بود." }

Write-Host ""
Write-Host "حساب‌یار اجرا شد:" -ForegroundColor Green
Write-Host "http://localhost:8080"
Write-Host "روی سرور: http://SERVER-IP:8080"
Write-Host ""
docker compose --env-file .env ps
