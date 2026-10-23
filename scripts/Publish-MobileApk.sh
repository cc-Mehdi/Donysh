#!/usr/bin/env bash
# Run from Git Bash or WSL after increasing the version in DonyshMobile.csproj.
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
publish_script="$script_dir/Publish-MobileApk.ps1"

if ! command -v powershell.exe >/dev/null 2>&1; then
    printf 'Windows PowerShell is required to run the existing mobile publisher.\n' >&2
    exit 1
fi

if command -v cygpath >/dev/null 2>&1; then
    export DONYSH_PUBLISH_SCRIPT="$(cygpath -w "$publish_script")"
elif command -v wslpath >/dev/null 2>&1; then
    export DONYSH_PUBLISH_SCRIPT="$(wslpath -w "$publish_script")"
else
    printf 'Run this script from Git Bash or WSL so the publish path can be converted for PowerShell.\n' >&2
    exit 1
fi

# Keep password prompts and plaintext environment values inside the short-lived
# PowerShell process. They are never written to this shell, a file, or the log.
ps_command=$(cat <<'POWERSHELL'
$ErrorActionPreference = 'Stop'
try {
    $publishScript = $env:DONYSH_PUBLISH_SCRIPT
    if (-not (Test-Path -LiteralPath $publishScript -PathType Leaf)) {
        throw "Publish script not found: $publishScript"
    }

    if (-not $env:DONYSH_ANDROID_KEYSTORE) {
        $env:DONYSH_ANDROID_KEYSTORE = Join-Path $env:USERPROFILE 'DonyshSigning\donysh-release.jks'
    }
    if (-not (Test-Path -LiteralPath $env:DONYSH_ANDROID_KEYSTORE -PathType Leaf)) {
        throw "Release keystore not found: $env:DONYSH_ANDROID_KEYSTORE"
    }
    if (-not $env:DONYSH_ANDROID_KEY_ALIAS) {
        $env:DONYSH_ANDROID_KEY_ALIAS = 'donysh-mobile'
    }

    $storePassword = Read-Host 'Store Password' -AsSecureString
    $keyPassword = Read-Host 'Key Password' -AsSecureString
    $env:DONYSH_ANDROID_STORE_PASSWORD = [System.Net.NetworkCredential]::new('', $storePassword).Password
    $env:DONYSH_ANDROID_KEY_PASSWORD = [System.Net.NetworkCredential]::new('', $keyPassword).Password
    if (-not $env:DONYSH_ANDROID_STORE_PASSWORD -or -not $env:DONYSH_ANDROID_KEY_PASSWORD) {
        throw 'Both signing passwords are required.'
    }

    & $publishScript
    if (-not $?) { throw 'Mobile publication failed.' }
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
finally {
    Remove-Item Env:\DONYSH_ANDROID_STORE_PASSWORD, Env:\DONYSH_ANDROID_KEY_PASSWORD, Env:\DONYSH_PUBLISH_SCRIPT -ErrorAction SilentlyContinue
    if ($storePassword) { $storePassword.Dispose() }
    if ($keyPassword) { $keyPassword.Dispose() }
}
POWERSHELL
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$ps_command"
