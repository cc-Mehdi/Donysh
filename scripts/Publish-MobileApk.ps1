param(
    [string]$Server = 'ubuntu@185.226.119.80',
    [ValidatePattern('^[a-fA-F0-9]{64}$')]
    [string]$ResumeUploadedSha256
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectFile = Join-Path $projectRoot 'DonyshMobile/DonyshMobile/DonyshMobile.csproj'
[xml]$project = Get-Content -LiteralPath $projectFile
$versionName = $project.SelectSingleNode('//ApplicationDisplayVersion').InnerText
$versionCode = $project.SelectSingleNode('//ApplicationVersion').InnerText
if ([string]::IsNullOrWhiteSpace($versionName) -or $versionCode -notmatch '^[1-9][0-9]*$') {
    throw 'Set ApplicationDisplayVersion and a positive ApplicationVersion in DonyshMobile.csproj.'
}

if ($ResumeUploadedSha256) {
    $sha256 = $ResumeUploadedSha256.ToLowerInvariant()
    $remoteFile = "/home/ubuntu/donysh-mobile-upload-$sha256.apk"
    Write-Host "Resuming server publication of version $versionName ($versionCode): $remoteFile"
    ssh -tt $Server sudo bash /home/ubuntu/donysh/scripts/publish-mobile-apk.sh $remoteFile $sha256 $versionCode
    if ($LASTEXITCODE -ne 0) { throw "Server publication failed. Check SSH access and the uploaded file at $remoteFile." }
    Write-Host 'Published and verified: https://donysh.ir/downloads/donysh-mobile.apk'
    return
}

foreach ($name in @('DONYSH_ANDROID_KEYSTORE', 'DONYSH_ANDROID_KEY_ALIAS',
                    'DONYSH_ANDROID_STORE_PASSWORD', 'DONYSH_ANDROID_KEY_PASSWORD')) {
    if (-not [Environment]::GetEnvironmentVariable($name)) { throw "Missing signing setting: $name" }
}

# A fresh, unique output directory prevents an older *-Signed.apk from being selected.
$buildDir = Join-Path ([IO.Path]::GetTempPath()) ("donysh-mobile-release-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $buildDir | Out-Null
dotnet publish $projectFile -c Release -f net9.0-android -p:PublishProfile=WebsiteApk --output $buildDir
if ($LASTEXITCODE -ne 0) { throw 'Signed Android publish failed.' }
$signed = @(Get-ChildItem -LiteralPath $buildDir -Filter '*-Signed.apk' -File -Recurse)
if ($signed.Count -ne 1) {
    throw "Expected exactly one signed APK in the new output directory $buildDir; found $($signed.Count)."
}
$apk = $signed[0]
$sha256 = (Get-FileHash -LiteralPath $apk.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$remoteFile = "/home/ubuntu/donysh-mobile-upload-$sha256.apk"
Write-Host "Built version $versionName ($versionCode): $($apk.FullName)"
Write-Host "SHA-256: $sha256"
scp $apk.FullName "${Server}:$remoteFile"
if ($LASTEXITCODE -ne 0) { throw 'APK upload failed.' }
ssh -tt $Server sudo bash /home/ubuntu/donysh/scripts/publish-mobile-apk.sh $remoteFile $sha256 $versionCode
if ($LASTEXITCODE -ne 0) {
    throw "Server publication failed. The APK is already uploaded at $remoteFile. Retry with -ResumeUploadedSha256 $sha256."
}
Write-Host 'Published and verified: https://donysh.ir/downloads/donysh-mobile.apk'
