param(
    [Parameter(Position = 0)]
    [string]$ArchivePath = "IRANYekanX Pro.rar"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$TargetDir = Join-Path $ProjectRoot "HesabYar.Web\wwwroot\fonts"
$TargetFile = Join-Path $TargetDir "IRANYekanXVFaNumVF.woff2"
$InnerPath = "IRANYekanX Pro\Farsi numerals\Variable Font\Webfonts\IRANYekanXVFaNumVF.woff2"
$TempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("hesabyar-font-" + [guid]::NewGuid().ToString("N"))

if (-not (Test-Path -LiteralPath $ArchivePath)) {
    throw "Font archive not found: $ArchivePath"
}

New-Item -ItemType Directory -Force -Path $TargetDir, $TempDir | Out-Null

try {
    $SevenZip = Get-Command 7z -ErrorAction SilentlyContinue
    $Unrar = Get-Command unrar -ErrorAction SilentlyContinue

    if ($SevenZip) {
        & $SevenZip.Source x -y "-o$TempDir" $ArchivePath ($InnerPath -replace '\\', '/') | Out-Null
    }
    elseif ($Unrar) {
        & $Unrar.Source x -inul -y $ArchivePath $InnerPath ($TempDir + "\")
    }
    else {
        $WinRarCandidates = @(
            "$env:ProgramFiles\WinRAR\UnRAR.exe",
            "${env:ProgramFiles(x86)}\WinRAR\UnRAR.exe"
        ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

        if ($WinRarCandidates.Count -eq 0) {
            throw "7-Zip or WinRAR was not found. Extract the WOFF2 file manually."
        }

        & $WinRarCandidates[0] x -inul -y $ArchivePath $InnerPath ($TempDir + "\")
    }

    $Extracted = Join-Path $TempDir $InnerPath
    if (-not (Test-Path -LiteralPath $Extracted)) {
        throw "Expected font file was not found in the archive."
    }

    Copy-Item -LiteralPath $Extracted -Destination $TargetFile -Force
    Write-Host "IRANYekanX Pro installed at: $TargetFile"
}
finally {
    Remove-Item -LiteralPath $TempDir -Recurse -Force -ErrorAction SilentlyContinue
}
