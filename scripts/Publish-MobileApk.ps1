param(
    [string]$Server = 'ubuntu@185.226.119.80'
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
foreach ($name in @('DONYSH_ANDROID_KEYSTORE', 'DONYSH_ANDROID_KEY_ALIAS',
                    'DONYSH_ANDROID_STORE_PASSWORD', 'DONYSH_ANDROID_KEY_PASSWORD')) {
    if (-not [Environment]::GetEnvironmentVariable($name)) { throw "Missing signing setting: $name" }
}

# A fresh, unique output directory prevents an older *-Signed.apk from being selected.
$buildDir = Join-Path ([IO.Path]::GetTempPath()) ("donysh-mobile-release-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $buildDir | Out-Null
$started = Get-Date
dotnet publish $projectFile -c Release -f net9.0-android -p:PublishProfile=WebsiteApk --output $buildDir
if ($LASTEXITCODE -ne 0) { throw 'Signed Android publish failed.' }
$signed = @(Get-ChildItem -LiteralPath $buildDir -Filter '*-Signed.apk' -File -Recurse)
if ($signed.Count -ne 1 -or $signed[0].LastWriteTime -lt $started.AddMinutes(-1)) {
    throw "Expected exactly one freshly signed APK in $buildDir; found $($signed.Count)."
}
$apk = $signed[0]
$sha256 = (Get-FileHash -LiteralPath $apk.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$remoteFile = "/home/ubuntu/donysh-mobile-upload-$sha256.apk"
Write-Host "Built version $versionName ($versionCode): $($apk.FullName)"
Write-Host "SHA-256: $sha256"
scp $apk.FullName "${Server}:$remoteFile"
if ($LASTEXITCODE -ne 0) { throw 'APK upload failed.' }
ssh $Server sudo bash /home/ubuntu/donysh/scripts/publish-mobile-apk.sh $remoteFile $sha256 $versionCode
if ($LASTEXITCODE -ne 0) { throw 'Server publication or public hash verification failed.' }
Write-Host 'Published and verified: https://donysh.ir/downloads/donysh-mobile.apk'
