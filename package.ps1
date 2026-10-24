[CmdletBinding()]
param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root
try {
    & (Join-Path $root "build.cmd")
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE."
    }

    $dist = Join-Path $root "dist"
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    $zip = Join-Path $dist ("CurskinInstaller-{0}-win64.zip" -f $Version)
    if (Test-Path $zip) {
        Remove-Item $zip -Force
    }

    $files = @(
        (Join-Path $root "CurskinInstaller.exe"),
        (Join-Path $root "README.txt"),
        (Join-Path $root "THIRD-PARTY-NOTICES.txt"),
        (Join-Path $root "LICENSE")
    )

    foreach ($file in $files) {
        if (-not (Test-Path $file)) {
            throw "Missing release file: $file"
        }
    }

    Compress-Archive -Path $files -DestinationPath $zip -CompressionLevel Optimal
    Write-Host ""
    Write-Host ("Created " + $zip)
    Get-Item $zip | Select-Object Name, Length, LastWriteTime | Format-List
}
finally {
    Pop-Location
}
