[CmdletBinding()]
param(
    [string] $ExecutablePath
)

$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'The Windows startup check requires Windows.'
}

$taskProjectRoot = Split-Path -Parent $PSScriptRoot
$taskProject = Join-Path $taskProjectRoot 'DonyshMobile\DonyshMobile.csproj'
$taskRunDirectory = Join-Path ([IO.Path]::GetTempPath()) ('donysh-startup-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $taskRunDirectory)
$taskResultPath = Join-Path $taskRunDirectory 'startup-result.json'

if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $taskBuildDirectory = Join-Path $taskRunDirectory 'build'
    # A separate output directory avoids touching a user debugging the normal build.
    Push-Location $taskProjectRoot
    try {
        & dotnet build $taskProject -c Debug -f net9.0-windows10.0.19041.0 `
            -p:TargetFrameworks=net9.0-windows10.0.19041.0 `
            "-p:OutputPath=$($taskBuildDirectory.Replace('\', '/'))/" `
            "-p:MSBuildProjectExtensionsPath=$($taskRunDirectory.Replace('\', '/'))/restore/" `
            "-p:IntermediateOutputPath=$($taskRunDirectory.Replace('\', '/'))/intermediate/" `
            -p:AppendTargetFrameworkToOutputPath=false -p:AppendRuntimeIdentifierToOutputPath=false
        if ($LASTEXITCODE -ne 0) { throw "The Windows debug build failed with exit code $LASTEXITCODE." }
    }
    finally { Pop-Location }
    $ExecutablePath = Join-Path $taskBuildDirectory 'DonyshMobile.exe'
}

$taskExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
if ([IO.Path]::GetExtension($taskExecutable) -ne '.exe') { throw 'Supply the debug DonyshMobile executable.' }
$taskPreviousFlag = $env:DONYSH_STARTUP_CHECK_OUTPUT
try {
    $env:DONYSH_STARTUP_CHECK_OUTPUT = $taskResultPath
    $taskProcess = Start-Process -FilePath $taskExecutable -WorkingDirectory (Split-Path -Parent $taskExecutable) `
        -WindowStyle Hidden -PassThru
}
finally { $env:DONYSH_STARTUP_CHECK_OUTPUT = $taskPreviousFlag }

$taskWatch = [Diagnostics.Stopwatch]::StartNew()
while (-not $taskProcess.HasExited -and $taskWatch.Elapsed.TotalSeconds -lt 60) {
    Start-Sleep -Milliseconds 200
    $taskProcess.Refresh()
}
if (-not $taskProcess.HasExited) {
    # This process object is the child just created above; no existing app is stopped.
    $taskProcess.Kill()
    [void]$taskProcess.WaitForExit(1000)
    throw "The test process timed out after 60 seconds. Diagnostics directory: $taskRunDirectory"
}

if (-not (Test-Path -LiteralPath $taskResultPath)) {
    throw "The app exited with code $($taskProcess.ExitCode) before writing a startup report. Diagnostics directory: $taskRunDirectory"
}
$taskReport = Get-Content -LiteralPath $taskResultPath -Raw | ConvertFrom-Json
Write-Output "Startup report: $taskResultPath"
foreach ($taskCheck in $taskReport.checks) { Write-Output "PASS: $taskCheck" }
if (-not $taskReport.success -or $taskProcess.ExitCode -ne 0) {
    throw "Windows runtime startup check failed: $($taskReport.error)"
}
Write-Output 'Windows startup and page navigation checks passed.'
