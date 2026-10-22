$ErrorActionPreference = 'Stop'
$publishScript = (Resolve-Path (Join-Path $PSScriptRoot '../Publish-MobileApk.ps1')).Path
$global:buildDir = $null
$global:scpCalls = 0
$global:sshCalls = 0
$global:sshArguments = @()

function dotnet {
    if ($args[0] -eq 'clean') { $global:LASTEXITCODE = 0; return }
    $outputIndex = [Array]::IndexOf($args, '--output')
    if ($outputIndex -lt 0) { throw 'The publish output directory was not supplied.' }
    $global:buildDir = $args[$outputIndex + 1]
    $apkPath = Join-Path $global:buildDir 'test-Signed.apk'
    [IO.File]::WriteAllBytes($apkPath, [byte[]](80, 75, 0, 0))
    (Get-Item -LiteralPath $apkPath).LastWriteTime = [datetime]'2020-01-01'
    $global:LASTEXITCODE = 0
}
function scp {
    $global:scpCalls++
    $global:LASTEXITCODE = 0
}
function ssh {
    $global:sshCalls++
    $global:sshArguments = @($args)
    $global:LASTEXITCODE = 0
}

try {
    $env:DONYSH_ANDROID_KEYSTORE = 'test-only-keystore'
    $env:DONYSH_ANDROID_KEY_ALIAS = 'test-only-alias'
    $env:DONYSH_ANDROID_STORE_PASSWORD = 'test-only-value'
    $env:DONYSH_ANDROID_KEY_PASSWORD = 'test-only-value'
    & $publishScript -Server 'test@localhost'
    if ($global:scpCalls -ne 1 -or $global:sshCalls -ne 1 -or $global:sshArguments[0] -ne '-tt') {
        throw 'A copied signed APK with an older file timestamp was not published.'
    }

    $sha256 = (Get-FileHash -LiteralPath (Join-Path $global:buildDir 'test-Signed.apk') -Algorithm SHA256).Hash.ToLowerInvariant()
    & $publishScript -Server 'test@localhost' -ResumeUploadedSha256 $sha256
    if ($global:scpCalls -ne 1 -or $global:sshCalls -ne 2 -or $global:sshArguments[-2] -ne $sha256) {
        throw 'Resume attempted to rebuild or upload the APK instead of publishing the staged file.'
    }
    Write-Host 'PASS Mobile APK publish accepts a copied artifact and resumes the uploaded file.'
}
finally {
    Remove-Item Env:\DONYSH_ANDROID_KEYSTORE, Env:\DONYSH_ANDROID_KEY_ALIAS, Env:\DONYSH_ANDROID_STORE_PASSWORD, Env:\DONYSH_ANDROID_KEY_PASSWORD -ErrorAction SilentlyContinue
    if ($global:buildDir) {
        $resolvedBuildDir = [IO.Path]::GetFullPath($global:buildDir)
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        $buildLeaf = Split-Path $resolvedBuildDir -Leaf
        if ($resolvedBuildDir.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and $buildLeaf -like 'donysh-mobile-release-*') {
            Remove-Item -LiteralPath $resolvedBuildDir -Recurse -Force
        }
    }
}
